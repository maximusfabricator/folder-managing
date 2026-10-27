// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using FolderManaging.Core;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;
using Microsoft.Win32;

namespace FolderManaging.Setup;

internal sealed record UninstallOptions(bool ResetFolders, bool DeleteSettings);

internal static class Uninstaller
{
    /// <returns>Warnings that did not stop the uninstall.</returns>
    internal static IReadOnlyList<string> Run(UninstallOptions options, IProgress<string> progress)
    {
        SetupLog.Write($"=== uninstall options={options}");
        var warnings = new List<string>();
        var workDir = Path.Combine(Path.GetTempPath(), "FolderManagingUninstall-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        try
        {
            progress.Report(UiText.Get("ClosingApp"));
            SystemTools.StopProcesses(ProductInfo.AppProcessNames);

            var allFoldersReset = true;
            if (options.ResetFolders)
            {
                progress.Report(UiText.Get("RestoringIcons"));
                var failed = ResetColoredFolders();
                if (failed.Count > 0)
                    File.WriteAllLines(Path.Combine(workDir, ElevatedTasks.ProtectedFoldersFileName), failed);
            }

            progress.Report(UiText.Get("RemovingPackage"));
            Installer.RemovePackage(ProductInfo.PackageName);

            progress.Report(UiText.Get("RemovingSystemSettings"));
            try
            {
                // Uninstall warnings are only folders that stayed colored even with admin rights.
                var elevatedWarnings = Installer.RunElevatedStep("uninstall", workDir);
                warnings.AddRange(elevatedWarnings.Select(w => UiText.Format("FolderNotReset", UiText.LeftToRight(w))));
                if (elevatedWarnings.Count > 0)
                    allFoldersReset = false;
            }
            catch (Exception ex)
            {
                SetupLog.Write($"elevated uninstall skipped: {ex.Message}");
                warnings.Add(UiText.Format("AdminLeftColumn", ex.Message));
                if (File.Exists(Path.Combine(workDir, ElevatedTasks.ProtectedFoldersFileName)))
                    allFoldersReset = false;
            }

            progress.Report(UiText.Get("RemovingShortcuts"));
            Installer.SetAutostart(false);
            SystemTools.TryDeleteFile(ProductInfo.StartMenuShortcutPath);
            SystemTools.TryDeleteFile(ProductInfo.StartMenuUninstallShortcutPath);
            SystemTools.TryDeleteFile(ProductInfo.DesktopShortcutPath);
            Registry.CurrentUser.DeleteSubKeyTree(ProductInfo.UninstallKeyPath, throwOnMissingSubKey: false);

            // Colored folders keep pointing at the generated .ico files, so they stay unless every folder was reset.
            if (options.ResetFolders && allFoldersReset)
                SystemTools.TryDeleteDirectory(ProductInfo.LocalDataDirectory);
            if (options.DeleteSettings)
                SystemTools.TryDeleteDirectory(ProductInfo.ProfileDataDirectory);

            ScheduleInstallDirectoryRemoval();
            SetupLog.Write("=== uninstall finished");
            return warnings;
        }
        finally
        {
            SystemTools.TryDeleteDirectory(workDir);
        }
    }

    /// <returns>Folders that could not be reset without admin rights.</returns>
    static List<string> ResetColoredFolders()
    {
        var failed = new List<string>();
        AppStateDocument doc;
        try
        {
            doc = JsonFolderStateStore.LoadSynchronously();
        }
        catch (Exception ex)
        {
            SetupLog.Write($"state.json unreadable: {ex.Message}");
            return failed;
        }

        foreach (var entry in doc.Folders)
        {
            if (string.IsNullOrWhiteSpace(entry.ColorToken) || !Directory.Exists(entry.Path))
                continue;
            if (!FolderIconManager.TryRemoveCustomization(entry.Path, out var message))
            {
                SetupLog.Write($"reset failed '{entry.Path}': {message}");
                failed.Add(entry.Path);
            }
        }

        // Also sweep the user profile so colored folders that are no longer listed in state.json lose desktop.ini.
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            FolderIconManager.CleanupManagedDesktopInis(profile, maxDepth: 8, recursive: true);
        }
        catch (Exception ex)
        {
            SetupLog.Write($"profile desktop.ini sweep: {ex.Message}");
        }

        return failed;
    }

    static void ScheduleInstallDirectoryRemoval()
    {
        var dir = ProductInfo.InstallDirectory;
        if (!Directory.Exists(dir))
            return;

        var running = Environment.ProcessPath ?? "";
        if (!running.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
        {
            SystemTools.TryDeleteDirectory(dir);
            return;
        }

        // The maintenance exe cannot delete itself while running.
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"{dir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        })?.Dispose();
    }
}
