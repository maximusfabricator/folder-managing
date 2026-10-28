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
#if FM_DEV
            // Developer builds: UI tours and experiments run on a copy instead of the real state.json.
            if (Environment.GetEnvironmentVariable("FM_DATA_DIR") is { Length: > 0 } devDir)
            {
                Directory.CreateDirectory(devDir);
                return Path.GetFullPath(devDir);
            }
#endif
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

    /// <summary>
    /// Icon store every account on the PC can read: <c>%ProgramData%\FolderManaging\Icons</c>. The installer creates it
    /// with write access for Users; <c>null</c> when it is missing or not writable (developer build, Store edition).
    /// </summary>
    public static string SharedIconsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FolderManaging", "Icons");

    static readonly Lazy<string?> UsableSharedIcons = new(() =>
    {
        var dir = SharedIconsDirectory;
        if (!Directory.Exists(dir))
            return null;
        var probe = Path.Combine(dir, $".probe-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return Path.GetFullPath(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    });

    /// <summary>Per-folder icons named in <c>desktop.ini</c>: the shared store when usable, else the per-user cache.</summary>
    public static string FolderIconsDirectory =>
        Path.Combine(UsableSharedIcons.Value ?? GeneratedIconsDirectory, "Folders");

    /// <summary>Per-user folder icons of 2.0 and older (only other accounts could not read them).</summary>
    public static string PerUserFolderIconsDirectory => Path.Combine(GeneratedIconsDirectory, "Folders");

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
