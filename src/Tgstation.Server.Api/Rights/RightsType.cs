﻿namespace Tgstation.Server.Api.Rights
{
	/// <summary>
	/// The type of rights a model uses
	/// </summary>
	public enum RightsType
	{
		/// <summary>
		/// <see cref="AdministrationRights"/>
		/// </summary>
		Administration,
		/// <summary>
		/// <see cref="InstanceManagerRights"/>
		/// </summary>
		InstanceManager,
		/// <summary>
		/// <see cref="RepositoryRights"/>
		/// </summary>
		Repository,
		/// <summary>
		/// <see cref="ByondRights"/>
		/// </summary>
		Byond,
		/// <summary>
		/// <see cref="DreamMakerRights"/>
		/// </summary>
		DreamMaker,
		/// <summary>
		/// <see cref="DreamDaemonRights"/>
		/// </summary>
		DreamDaemon,
		/// <summary>
		/// <see cref="ChatSettingsRights"/>
		/// </summary>
		Chat,
		/// <summary>
		/// <see cref="ConfigurationRights"/>
		/// </summary>
		Configuration,
		/// <summary>
		/// <see cref="InstanceUserRights"/>
		/// </summary>
		InstanceUser
	}
}
