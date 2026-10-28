// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

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

        path = TryGetPathAt(items, 0);
        return path is null ? HRESULT.E_FAIL : HRESULT.S_OK;
    }

    /// <summary>File system folders of the whole selection, in order. Virtual items (Control Panel, libraries) are left out.</summary>
    internal static IReadOnlyList<string> GetAllPaths(Shell32.IShellItemArray? items)
    {
        var paths = new List<string>();
        if (items is null)
            return paths;
        var count = items.GetCount();
        for (uint i = 0; i < count; i++)
        {
            if (TryGetPathAt(items, i) is { } path &&
                !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }

        return paths;
    }

    static string? TryGetPathAt(Shell32.IShellItemArray items, uint index)
    {
        var item = items.GetItemAt(index);
        try
        {
            // Prefer a real filesystem path; parsing names can be non-filesystem for virtual items.
            string? path;
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
                return null;
            return path;
        }
        finally
        {
            _ = Marshal.FinalReleaseComObject(item);
        }
    }
}
