using System.Runtime.InteropServices;
using System.Text;

namespace FolderManaging.Core.Interop;

internal static class Win32Kernel
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);
}
