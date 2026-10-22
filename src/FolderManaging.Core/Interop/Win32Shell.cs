using System.Runtime.InteropServices;

namespace FolderManaging.Core.Interop;

internal static class Win32Shell
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
}
