// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;

namespace FolderManaging.Core.Interop;

public static class Win32Shell
{
    public const uint SHCNE_ASSOCCHANGED = 0x0800_0000;
    public const uint SHCNE_UPDATEDIR = 0x0000_1000;
    public const uint SHCNE_UPDATEITEM = 0x0000_2000;
    public const uint SHCNE_ATTRIBUTES = 0x0000_0800;
    public const uint SHCNF_IDLIST = 0x0000;
    public const uint SHCNF_PATHW = 0x0005;
    public const uint SHCNF_FLUSH = 0x1000;
    public const uint SHCNF_FLUSHNOWAIT = 0x3000;

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint wEventId, uint uFlags, nint dwItem1, nint dwItem2);

    /// <summary>Drops Explorer's cached read of <c>desktop.ini</c> so the open window picks up the new icon and columns.</summary>
    [DllImport("shell32.dll")]
    public static extern void SHFlushSFCache();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(nint handle);

    public const uint FCSM_INFOTIP = 0x0000_0004;
    public const uint FCSM_ICONFILE = 0x0000_0010;
    public const uint FCS_FORCEWRITE = 0x0000_0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFOLDERCUSTOMSETTINGS
    {
        public uint dwSize;
        public uint dwMask;
        public nint pvid;
        public string? pszWebViewTemplate;
        public uint cchWebViewTemplate;
        public string? pszWebViewTemplateVersion;
        public string? pszInfoTip;
        public uint cchInfoTip;
        public nint pclsid;
        public uint dwFlags;
        public string? pszIconFile;
        public uint cchIconFile;
        public int iIconIndex;
        public string? pszLogo;
        public uint cchLogo;
    }

    /// <summary>Writes folder customization and invalidates Explorer's in-memory desktop.ini cache for that folder.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHGetSetFolderCustomSettings(ref SHFOLDERCUSTOMSETTINGS pfcs, string pszPath, uint dwReadWrite);

    public static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);

    public static string? TryGetKnownFolderPath(Guid folderId)
    {
        if (SHGetKnownFolderPath(folderId, 0, 0, out var buffer) != 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    public const uint SHCNE_RENAMEFOLDER = 0x0002_0000;
    public const uint SHCNE_RMDIR = 0x0000_0010;
    public const int SHCNRF_InterruptLevel = 0x0001;
    public const int SHCNRF_ShellLevel = 0x0002;
    public const int SHCNRF_NewDelivery = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SHChangeNotifyEntry
    {
        public nint pidl;
        [MarshalAs(UnmanagedType.Bool)] public bool fRecursive;
    }

    [DllImport("shell32.dll")]
    public static extern uint SHChangeNotifyRegister(nint hwnd, int fSources, uint fEvents, uint wMsg, int cEntries, ref SHChangeNotifyEntry pshcne);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SHChangeNotifyDeregister(uint ulId);

    [DllImport("shell32.dll")]
    public static extern nint SHChangeNotification_Lock(nint hChange, uint dwProcId, out nint pppidl, out int plEvent);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SHChangeNotification_Unlock(nint hLock);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SHGetPathFromIDListW(nint pidl, [Out] char[] pszPath);

    public static string? TryGetPathFromIdList(nint pidl)
    {
        if (pidl == 0)
            return null;
        var buffer = new char[32768];
        if (!SHGetPathFromIDListW(pidl, buffer))
            return null;
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }
}
