// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Interop;

namespace FolderManaging.Core.Storage;

/// <summary>
/// Paths for app state vs preset icons.
/// </summary>
/// <remarks>
/// <para><b>state.json</b> lives under <c>%USERPROFILE%\.FolderManaging\</c> so the packaged CLI and the shell DLL
/// always agree (MSIX virtualizes writes under <c>%LocalAppData%</c> into the package <c>LocalCache</c>).</para>
/// <para><b>Tinted .ico</b> files are generated at runtime under <see cref="GeneratedIconsDirectory"/> (always writable).
/// Build scripts may set <c>FOLDERMANAGING_ICONS_DIR</c> to stage icons elsewhere; the installed app does not rely on bundled presets.</para>
/// </remarks>
public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".FolderManaging");
            Directory.CreateDirectory(dir);
            TrySetHidden(dir);
            return dir;
        }
    }

    public static string StateFilePath => Path.Combine(DataDirectory, "state.json");

    /// <summary>Writable cache for tinted folder icons (shell capture + HSL tint). Used by <see cref="FolderIconManager"/> at runtime.</summary>
    public static string GeneratedIconsDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FolderManaging",
                "Icons");
            Directory.CreateDirectory(dir);
            return Path.GetFullPath(dir);
        }
    }

    /// <summary>Optional override via <c>FOLDERMANAGING_ICONS_DIR</c>, else package <c>Icons</c> when packaged, else same as generated path.</summary>
    public static string IconsDirectory
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("FOLDERMANAGING_ICONS_DIR");
            if (!string.IsNullOrWhiteSpace(env))
                return Path.GetFullPath(env.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var pkg = PackagePaths.TryGetPackageInstallRoot();
            if (!string.IsNullOrEmpty(pkg))
                return Path.Combine(pkg, "Icons");
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FolderManaging",
                "Icons");
        }
    }

    private static void TrySetHidden(string dir)
    {
        try
        {
            var attrs = File.GetAttributes(dir);
            if (!attrs.HasFlag(FileAttributes.Hidden))
                File.SetAttributes(dir, attrs | FileAttributes.Hidden);
        }
        catch
        {
            // best-effort
        }
    }
}
