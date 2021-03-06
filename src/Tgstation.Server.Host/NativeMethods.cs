﻿using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Tgstation.Server.Host
{
	/// <summary>
	/// Native methods used by the code
	/// </summary>
	static class NativeMethods
	{
		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-getwindowthreadprocessid
		/// </summary>
		[DllImport("user32.dll")]
		public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-findwindoww
		/// </summary>
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-sendmessage
		/// </summary>
		[DllImport("user32.dll")]
		public static extern int SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

		/// <summary>
		/// See https://msdn.microsoft.com/en-us/library/ms633493(v=VS.85).aspx
		/// </summary>
		public delegate bool EnumWindowProc(IntPtr hwnd, IntPtr lParam);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-enumchildwindows
		/// </summary>
		[DllImport("user32")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr lParam);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-getwindowtextw
		/// </summary>
		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

		/// <summary>
		/// See https://msdn.microsoft.com/en-us/library/windows/desktop/aa378184(v=vs.85).aspx
		/// </summary>
		[DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern bool LogonUser(string lpszUsername, string lpszDomain, string lpszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);

		/// <summary>
		/// See https://docs.microsoft.com/en-us/windows/desktop/api/winbase/nf-winbase-createsymboliclinkw
		/// </summary>
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);
	}
}
