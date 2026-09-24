using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace FolderManaging.Shell;

internal static class ShellItemPaths
{
    internal static HRESULT TryGetFirstPath(Shell32.IShellItemArray? items, out string? path)
    {
        path = null;
        if (items is null)
        {
            return HRESULT.E_INVALIDARG;
        }

        var count = items.GetCount();
        if (count == 0)
        {
            return HRESULT.S_FALSE;
        }

        var item = items.GetItemAt(0);
        try
        {
            // Prefer a real filesystem path; parsing names can be non-filesystem for virtual items.
            try
            {
                path = item.GetDisplayName(Shell32.SIGDN.SIGDN_FILESYSPATH);
            }
            catch
            {
                path = null;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    path = item.GetDisplayName(Shell32.SIGDN.SIGDN_DESKTOPABSOLUTEPARSING);
                }
                catch
                {
                    path = null;
                }
            }

            if (string.IsNullOrWhiteSpace(path) ||
                path.StartsWith("::", StringComparison.Ordinal) ||
                !Path.IsPathFullyQualified(path))
            {
                path = null;
                return HRESULT.E_FAIL;
            }

            return HRESULT.S_OK;
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(item);
        }
    }
}
