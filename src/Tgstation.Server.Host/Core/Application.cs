﻿using Byond.TopicSender;
using Cyberboss.AspNetCore.AsyncInitializer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tgstation.Server.Host.Components;
using Tgstation.Server.Host.Components.Byond;
using Tgstation.Server.Host.Components.Chat;
using Tgstation.Server.Host.Components.Repository;
using Tgstation.Server.Host.Components.Watchdog;
using Tgstation.Server.Host.Configuration;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Models;
using Tgstation.Server.Host.Security;

namespace Tgstation.Server.Host.Core
{
	/// <inheritdoc />
	sealed class Application : IApplication
	{
		/// <inheritdoc />
		public string VersionPrefix => "tgstation-server";

		/// <inheritdoc />
		public Version Version { get; }

		/// <inheritdoc />
		public string VersionString { get; }

		/// <summary>
		/// The <see cref="IConfiguration"/> for the <see cref="Application"/>
		/// </summary>
		readonly IConfiguration configuration;

		/// <summary>
		/// The <see cref="Microsoft.AspNetCore.Hosting.IHostingEnvironment"/> for the <see cref="Application"/>
		/// </summary>
		readonly Microsoft.AspNetCore.Hosting.IHostingEnvironment hostingEnvironment;

		readonly TaskCompletionSource<object> startupTcs;

		/// <summary>
		/// Construct an <see cref="Application"/>
		/// </summary>
		/// <param name="configuration">The value of <see cref="configuration"/></param>
		/// <param name="hostingEnvironment">The value of <see cref="hostingEnvironment"/></param>
		public Application(IConfiguration configuration, Microsoft.AspNetCore.Hosting.IHostingEnvironment hostingEnvironment)
		{
			this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
			this.hostingEnvironment = hostingEnvironment ?? throw new ArgumentNullException(nameof(hostingEnvironment));

			startupTcs = new TaskCompletionSource<object>();

			Version = Assembly.GetExecutingAssembly().GetName().Version;
			VersionString = String.Format(CultureInfo.InvariantCulture, "{0} v{1}", VersionPrefix, Version);
		}

		/// <summary>
		/// Configure dependency injected services
		/// </summary>
		/// <param name="services">The <see cref="IServiceCollection"/> to configure</param>
		public void ConfigureServices(IServiceCollection services)
		{
			if (services == null)
				throw new ArgumentNullException(nameof(services));

			services.Configure<UpdatesConfiguration>(configuration.GetSection(UpdatesConfiguration.Section));
			var databaseConfigurationSection = configuration.GetSection(DatabaseConfiguration.Section);
			services.Configure<DatabaseConfiguration>(databaseConfigurationSection);
			services.Configure<GeneralConfiguration>(configuration.GetSection(GeneralConfiguration.Section));

			var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
			var ioManager = new DefaultIOManager();

			//remember, anything you .Get manually can be null if the config is missing
			var fileLoggingConfigurationSection = configuration.GetSection(FileLoggingConfiguration.Section);
			var fileLoggingConfiguration = fileLoggingConfigurationSection.Get<FileLoggingConfiguration>();
			if (fileLoggingConfiguration?.Disable != true)
			{
				var logPath = !String.IsNullOrEmpty(fileLoggingConfiguration?.Directory) ? fileLoggingConfiguration.Directory : ioManager.ConcatPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), VersionPrefix, "Logs");

				logPath = ioManager.ConcatPath(logPath, "tgs-{Date}.log");

				services.AddLogging(builder =>
				{
					LogLevel GetMinimumLogLevel(string stringLevel)
					{
						if (String.IsNullOrWhiteSpace(stringLevel) || !Enum.TryParse<LogLevel>(stringLevel, out var minimumLevel))
							minimumLevel = LogLevel.Information;
						return minimumLevel;
					}

					LogEventLevel? ConvertLogLevel(LogLevel logLevel)
					{
						switch (logLevel)
						{
							case LogLevel.Critical:
								return LogEventLevel.Fatal;
							case LogLevel.Debug:
								return LogEventLevel.Debug;
							case LogLevel.Error:
								return LogEventLevel.Error;
							case LogLevel.Information:
								return LogEventLevel.Information;
							case LogLevel.Trace:
								return LogEventLevel.Verbose;
							case LogLevel.Warning:
								return LogEventLevel.Warning;
							case LogLevel.None:
								return null;
							default:
								throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, "Invalid log level {0}", logLevel));
						}
					};

					var logEventLevel = ConvertLogLevel(GetMinimumLogLevel(fileLoggingConfiguration?.LogLevel));
					var microsoftEventLevel = ConvertLogLevel(GetMinimumLogLevel(fileLoggingConfiguration?.MicrosoftLogLevel));

					var formatter = new MessageTemplateTextFormatter("{Timestamp:o} {RequestId,13} [{Level:u3}] {SourceContext:l}: {Message} ({EventId:x8}){NewLine}{Exception}", null);

					var configuration = new LoggerConfiguration()
					.Enrich.FromLogContext()
					.WriteTo.Async(w => w.RollingFile(formatter, logPath, shared: true, flushToDiskInterval: TimeSpan.FromSeconds(2)));

					if (logEventLevel.HasValue)
						configuration.MinimumLevel.Is(logEventLevel.Value);

					if (microsoftEventLevel.HasValue)
						configuration.MinimumLevel.Override("Microsoft", microsoftEventLevel.Value);

					builder.AddSerilog(configuration.CreateLogger(), true);
				});
			}

			services.AddOptions();

			services.AddScoped<IClaimsInjector, ClaimsInjector>();

			const string scheme = "JwtBearer";
			services.AddAuthentication((options) =>
			{
				options.DefaultAuthenticateScheme = scheme;
				options.DefaultChallengeScheme = scheme;
			}).AddJwtBearer(scheme, jwtBearerOptions =>
			{
				jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuerSigningKey = true,
					IssuerSigningKey = new SymmetricSecurityKey(TokenFactory.TokenSigningKey),

					ValidateIssuer = true,
					ValidIssuer = TokenFactory.TokenIssuer,

					ValidateLifetime = true,
					ValidateAudience = true,
					ValidAudience = TokenFactory.TokenAudience,

					ClockSkew = TimeSpan.FromMinutes(1),

					RequireSignedTokens = true,

					RequireExpirationTime = true
				};
				jwtBearerOptions.Events = new JwtBearerEvents
				{
					//Application is our composition root so this monstrosity of a line is okay
					OnTokenValidated = ctx => ctx.HttpContext.RequestServices.GetRequiredService<IClaimsInjector>().InjectClaimsIntoContext(ctx, ctx.HttpContext.RequestAborted)
				};
			});

			JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear(); //fucking converts 'sub' to M$ bs

			services.AddMvc().AddJsonOptions(options =>
			{
				options.AllowInputFormatterExceptionMessages = true;
				options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
				options.SerializerSettings.CheckAdditionalContent = true;
				options.SerializerSettings.MissingMemberHandling = MissingMemberHandling.Error;
				options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
				options.SerializerSettings.Converters = new[] { new VersionConverter() };
			});

			var databaseConfiguration = databaseConfigurationSection.Get<DatabaseConfiguration>();

			void AddTypedContext<TContext>() where TContext : DatabaseContext<TContext>
			{
				services.AddDbContext<TContext>(builder =>
				{
					if (hostingEnvironment.IsDevelopment())
						builder.EnableSensitiveDataLogging();
				});
				services.AddScoped<IDatabaseContext>(x => x.GetRequiredService<TContext>());
			}

			var dbType = databaseConfiguration?.DatabaseType;
			switch (databaseConfiguration?.DatabaseType)
			{
				case DatabaseType.MySql:
				case DatabaseType.MariaDB:
					AddTypedContext<MySqlDatabaseContext>();
					break;
				case DatabaseType.SqlServer:
					AddTypedContext<SqlServerDatabaseContext>();
					break;
				default:
					throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, "Invalid {0}: {1}!", nameof(DatabaseType), dbType));
			}

			services.AddScoped<IAuthenticationContextFactory, AuthenticationContextFactory>();
			services.AddSingleton<IIdentityCache, IdentityCache>();

			services.AddSingleton<ICryptographySuite, CryptographySuite>();
			services.AddSingleton<IDatabaseSeeder, DatabaseSeeder>();
			services.AddSingleton<IPasswordHasher<Models.User>, PasswordHasher<Models.User>>();
			services.AddSingleton<ITokenFactory, TokenFactory>();
			services.AddSingleton<ISynchronousIOManager, SynchronousIOManager>();
			services.AddSingleton<ICredentialsProvider, CredentialsProvider>();

			services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();

			if (isWindows)
			{
				services.AddSingleton<ISystemIdentityFactory, WindowsSystemIdentityFactory>();
				services.AddSingleton<ISymlinkFactory, WindowsSymlinkFactory>();
				services.AddSingleton<IByondInstaller, WindowsByondInstaller>();
				services.AddSingleton<IPostWriteHandler, WindowsPostWriteHandler>();

				services.AddSingleton<WindowsNetworkPromptReaper>();
				services.AddSingleton<INetworkPromptReaper>(x => x.GetRequiredService<WindowsNetworkPromptReaper>());
				services.AddSingleton<IHostedService>(x => x.GetRequiredService<WindowsNetworkPromptReaper>());
			}
			else
			{
				services.AddSingleton<ISystemIdentityFactory, PosixSystemIdentityFactory>();
				services.AddSingleton<ISymlinkFactory, PosixSymlinkFactory>();
				services.AddSingleton<IByondInstaller, PosixByondInstaller>();
				services.AddSingleton<IPostWriteHandler, PosixPostWriteHandler>();
				services.AddSingleton<INetworkPromptReaper, PosixNetworkPromptReaper>();
			}

			services.AddSingleton<IProcessExecutor, ProcessExecutor>();
			services.AddSingleton<IProviderFactory, ProviderFactory>();
			services.AddSingleton<IByondTopicSender>(new ByondTopicSender
			{
				ReceiveTimeout = 5000,
				SendTimeout = 5000
			});

			services.AddSingleton<IChatFactory, ChatFactory>();
			services.AddSingleton<IWatchdogFactory, WatchdogFactory>();
			services.AddSingleton<IInstanceFactory, InstanceFactory>();

			services.AddSingleton<InstanceManager>();
			services.AddSingleton<IInstanceManager>(x => x.GetRequiredService<InstanceManager>());
			services.AddSingleton<IHostedService>(x => x.GetRequiredService<InstanceManager>());

			services.AddSingleton<IJobManager, JobManager>();

			services.AddSingleton<IIOManager>(ioManager);

			services.AddSingleton<DatabaseContextFactory>();
			services.AddSingleton<IDatabaseContextFactory>(x => x.GetRequiredService<DatabaseContextFactory>());

			services.AddSingleton<IApplication>(this);
		}

		/// <summary>
		/// Configure the <see cref="Application"/>
		/// </summary>
		/// <param name="applicationBuilder">The <see cref="IApplicationBuilder"/> to configure</param>
		/// <param name="logger">The <see cref="Microsoft.Extensions.Logging.ILogger"/> for the <see cref="Application"/></param>
		/// <param name="serverControl">The <see cref="IServerControl"/> for the application</param>
		public void Configure(IApplicationBuilder applicationBuilder, ILogger<Application> logger, IServerControl serverControl)
		{
			if (applicationBuilder == null)
				throw new ArgumentNullException(nameof(applicationBuilder));
			if (logger == null)
				throw new ArgumentNullException(nameof(logger));
			if (serverControl == null)
				throw new ArgumentNullException(nameof(serverControl));

			logger.LogInformation(VersionString);
			
			//attempt to restart the server if the configuration changes
			ChangeToken.OnChange(configuration.GetReloadToken, () => serverControl.Restart());

			applicationBuilder.UseDeveloperExceptionPage(); //it is not worth it to limit this, you should only ever get it if you're an authorized user
			
			applicationBuilder.UseCancelledRequestSuppression();

			applicationBuilder.UseAsyncInitialization(async cancellationToken =>
			{
				using (cancellationToken.Register(() => startupTcs.SetCanceled()))
					await startupTcs.Task.ConfigureAwait(false);
			});

			applicationBuilder.UseAuthentication();

			applicationBuilder.UseDbConflictHandling();

			applicationBuilder.UseMvc();
		}

		///<inheritdoc />
		public void Ready(Exception initializationError)
		{
			lock (startupTcs)
			{
				if (startupTcs.Task.IsCompleted)
					throw new InvalidOperationException("Ready has already been called!");
				if (initializationError == null)
					startupTcs.SetResult(null);
				else
					startupTcs.SetException(initializationError);
			}
		}
	}
}