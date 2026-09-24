using System.Runtime.InteropServices;

namespace FolderManaging.Core.Interop;

internal static class ShellFileInfo
{
    public const uint ShgfiIcon = 0x000000100;
    public const uint ShgfiShelliconsize = 0x000000004;
    public const uint ShgfiAddoverlays = 0x000000020;
    public const uint FileAttributeDirectory = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint SHGetFileInfoW(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);
}
