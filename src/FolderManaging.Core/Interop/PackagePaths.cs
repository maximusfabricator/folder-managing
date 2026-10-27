// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;
using System.Text;

namespace FolderManaging.Core.Interop;

/// <summary>Detect MSIX / sparse package install root (same path Explorer uses to read package files).</summary>
internal static class PackagePaths
{
    const int ErrorSuccess = 0;
    const int ErrorInsufficientBuffer = 122;
    const int AppmodelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackagePath(ref uint pathLength, StringBuilder? path);

    /// <summary>Returns package root (no trailing slash), or <c>null</c> when not running inside an AppX package.</summary>
    internal static string? TryGetPackageInstallRoot()
    {
        uint capacity = 260;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var sb = new StringBuilder((int)capacity);
            var len = capacity;
            var hr = GetCurrentPackagePath(ref len, sb);
            if (hr == AppmodelErrorNoPackage)
                return null;
            if (hr == ErrorSuccess)
            {
                var s = sb.ToString().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrEmpty(s) ? null : s;
            }

            if (hr != ErrorInsufficientBuffer)
                return null;

            capacity = Math.Max(capacity * 2, len);
        }

        return null;
    }
}
