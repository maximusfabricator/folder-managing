// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using FolderManaging.Core;
using FolderManaging.Core.Interop;
using FolderManaging.Core.Localization;
using FolderManaging.Core.ShellProperties;
using FolderManaging.Core.Updates;
using Microsoft.Win32;

namespace FolderManaging.Setup;

/// <param name="IsUpdate">
/// <c>--update</c>: started by the app. Keeps the user's autostart and desktop shortcut, does not restart Explorer,
/// asks for administrator rights only when something outside the profile needs it, and starts the tray app again.
/// </param>
/// <param name="StatusPath">File the app watches during an update (<see cref="UpdateHandoff"/>).</param>
internal sealed record InstallOptions(
    bool Autostart,
    bool DesktopShortcut,
    bool RestartExplorer,
    bool LaunchAfterInstall,
    bool IsUpdate = false,
    string? StatusPath = null)
{
    internal static InstallOptions ForUpdate(string? statusPath) =>
        new(Installer.IsAutostartEnabled(), File.Exists(ProductInfo.DesktopShortcutPath),
            RestartExplorer: false, LaunchAfterInstall: true, IsUpdate: true, statusPath);
}

internal static class Installer
{
    /// <summary>How long an update waits for the tray app to close by itself before it is stopped.</summary>
    static readonly TimeSpan GracefulExitWait = TimeSpan.FromSeconds(8);

    /// <summary>Installed version from the uninstall entry, or <c>null</c>.</summary>
    internal static string? GetInstalledVersion()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProductInfo.UninstallKeyPath);
        return key?.GetValue("DisplayVersion") as string;
    }

    internal static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProductInfo.RunKeyPath);
        return key?.GetValue(ProductInfo.RunValueName) is string s && s.Length > 0;
    }

    /// <returns>Warnings that did not stop the install.</returns>
    internal static IReadOnlyList<string> Run(InstallOptions options, IProgress<string> progress)
    {
        SetupLog.Write($"=== install {ProductInfo.Version} options={options}");
        var warnings = new List<string>();

        if (options.IsUpdate &&
            UpdateVersion.TryParse(GetInstalledVersion(), out var installed) &&
            UpdateVersion.TryParse(ProductInfo.Version, out var next) &&
            installed > next)
        {
            throw new InvalidOperationException(UiText.Format("UpdateOlderSetup", ProductInfo.Version, installed));
        }

        progress.Report(UiText.Get("UnpackFiles"));
        var workDir = Payload.ExtractToTempDirectory();
        try
        {
            if (!options.IsUpdate || NeedsAdministrator(workDir))
            {
                progress.Report(SystemTools.IsDesktopRuntimeInstalled()
                    ? UiText.Get("PrepareWindows")
                    : UiText.Get("PrepareDotnet"));
                warnings.AddRange(RunElevatedStep("install", workDir));
            }
            else
            {
                SetupLog.Write("update: nothing outside the profile changes, no administrator step");
            }

            progress.Report(UiText.Get("InstallingApp"));
            if (options.IsUpdate)
            {
                UpdateHandoff.Write(options.StatusPath, UpdateStage.Installing);
                WaitForAppToClose();
            }

            SystemTools.StopProcesses(ProductInfo.AppProcessNames);
            RemovePackage(ProductInfo.DevPackageName);
            InstallPackage(Path.Combine(workDir, Payload.PackageFileName));

            progress.Report(UiText.Get("ShortcutsAutostart"));
            InstallMaintenanceFiles(workDir);
            WriteUninstallEntry(Path.Combine(workDir, Payload.PackageFileName));
            CreateShortcuts(options);
            if (!options.IsUpdate)
                SetAutostart(options.Autostart);

            if (options.RestartExplorer)
            {
                progress.Report(UiText.Get("RestartingExplorer"));
                SystemTools.RestartExplorer();
            }

            if (options.LaunchAfterInstall && !TryLaunchCompanion(options.IsUpdate ? UpdateHandoff.RelaunchArguments : ""))
                warnings.Add(UiText.Get("AutoOpenFail"));

            SetupLog.Write("=== install finished");
            return warnings;
        }
        finally
        {
            SystemTools.TryDeleteDirectory(workDir);
        }
    }

    /// <summary>
    /// What the administrator step of <see cref="ElevatedTasks"/> does is usually done already when an earlier
    /// version is installed: the runtime, the trusted certificate, the shared icon folder and the Explorer columns.
    /// Then an update needs no Windows prompt. A running administrator worker is asked to exit first.
    /// </summary>
    static bool NeedsAdministrator(string workDir)
    {
        var reasons = new List<string>();
        try
        {
            if (!SystemTools.IsDesktopRuntimeInstalled())
                reasons.Add(".NET Desktop Runtime missing");
            if (!IsCertificateTrusted(Path.Combine(workDir, Payload.CertificateFileName)))
                reasons.Add("certificate not trusted yet");
            if (!Directory.Exists(Path.Combine(ProductInfo.ProgramDataDirectory, "Icons")))
                reasons.Add("shared icon folder missing");
            if (!PropertySchemaRegistration.IsCurrent(Path.Combine(workDir, Payload.PropdescFileName)))
                reasons.Add("Explorer columns need registering");

            if (ElevatedCliInvoker.IsWorkerRunning())
            {
                ElevatedCliInvoker.TryStopWorker();
                for (var i = 0; i < 20 && ElevatedCliInvoker.IsWorkerRunning(); i++)
                    Thread.Sleep(250);
                if (ElevatedCliInvoker.IsWorkerRunning())
                    reasons.Add("administrator worker still running");
            }

            SystemTools.StopProcesses(["FolderManaging.Cli"]);
            if (Process.GetProcessesByName("FolderManaging.Cli").Length > 0)
                reasons.Add("a FolderManaging.Cli process with administrator rights is running");
        }
        catch (Exception ex)
        {
            reasons.Add("check failed: " + ex.Message);
        }

        SetupLog.Write(reasons.Count == 0 ? "update: administrator step not needed" : "update: administrator step needed: " + string.Join("; ", reasons));
        return reasons.Count > 0;
    }

    static bool IsCertificateTrusted(string cerPath)
    {
        using var cert = X509CertificateLoader.LoadCertificateFromFile(cerPath);
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false);
        var trusted = found.Count > 0;
        foreach (var c in found)
            c.Dispose();
        return trusted;
    }

    /// <summary>The tray app closes itself (and its tray icon) when it reads <see cref="UpdateStage.Installing"/>.</summary>
    static void WaitForAppToClose()
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < GracefulExitWait && Process.GetProcessesByName("FolderManaging.Companion").Length > 0)
            Thread.Sleep(200);
        SetupLog.Write($"update: tray app closed by itself={Process.GetProcessesByName("FolderManaging.Companion").Length == 0} after {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>Runs an <see cref="ElevatedTasks"/> step; returns its warnings or throws its error.</summary>
    internal static IReadOnlyList<string> RunElevatedStep(string task, string workDir)
    {
        var exit = SystemTools.RunElevated([ElevatedTasks.Switch, task, workDir]);
        if (exit != 0)
        {
            var errorFile = Path.Combine(workDir, ElevatedTasks.ErrorFileName);
            var message = File.Exists(errorFile) ? File.ReadAllText(errorFile) : UiText.Format("AdminStepCode", exit);
            throw new InvalidOperationException(message);
        }

        var warningsFile = Path.Combine(workDir, ElevatedTasks.WarningsFileName);
        return File.Exists(warningsFile) ? File.ReadAllLines(warningsFile) : [];
    }

    internal static void RemovePackage(string packageName) =>
        SystemTools.RunPowerShell(
            $"Get-AppxPackage -Name {SystemTools.Quote(packageName)} | ForEach-Object {{ Remove-AppxPackage -Package $_.PackageFullName }}");

    static void InstallPackage(string msixPath)
    {
        var installed = SystemTools.RunPowerShell(
            $"(Get-AppxPackage -Name {SystemTools.Quote(ProductInfo.PackageName)} | Select-Object -First 1).Version");

        // Same version with different bits cannot be updated in place (0x80073CFB): remove first.
        if (System.Version.TryParse(installed, out var current) &&
            System.Version.TryParse(ProductInfo.Version, out var next) &&
            Normalize(current) >= Normalize(next))
        {
            RemovePackage(ProductInfo.PackageName);
        }

        SystemTools.RunPowerShell(
            $"Add-AppxPackage -Path {SystemTools.Quote(msixPath)} -ForceApplicationShutdown -ForceUpdateFromAnyVersion");
    }

    static System.Version Normalize(System.Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    static void InstallMaintenanceFiles(string workDir)
    {
        Directory.CreateDirectory(ProductInfo.InstallDirectory);
        var running = Environment.ProcessPath;
        var target = ProductInfo.MaintenanceExePath;
        if (!string.Equals(running, target, StringComparison.OrdinalIgnoreCase))
            File.Copy(Path.Combine(workDir, ProductInfo.MaintenanceExeName), target, overwrite: true);
        File.Copy(Path.Combine(workDir, ProductInfo.IconFileName), Path.Combine(ProductInfo.InstallDirectory, ProductInfo.IconFileName), overwrite: true);
    }

    static void WriteUninstallEntry(string msixPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ProductInfo.UninstallKeyPath, writable: true);
        var exe = ProductInfo.MaintenanceExePath;
        key.SetValue("DisplayName", ProductInfo.DisplayName);
        key.SetValue("DisplayVersion", ProductInfo.Version);
        key.SetValue("Publisher", ProductInfo.Publisher);
        key.SetValue("URLInfoAbout", AppInfo.SourceUrl);
        key.SetValue("DisplayIcon", Path.Combine(ProductInfo.InstallDirectory, ProductInfo.IconFileName));
        key.SetValue("InstallLocation", ProductInfo.InstallDirectory);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        var sizeKb = (new FileInfo(msixPath).Length + new FileInfo(exe).Length) / 1024;
        key.SetValue("EstimatedSize", (int)Math.Min(sizeKb, int.MaxValue), RegistryValueKind.DWord);
    }

    static void CreateShortcuts(InstallOptions options)
    {
        var icon = Path.Combine(ProductInfo.InstallDirectory, ProductInfo.IconFileName);
        var description = UiText.Get("ShortcutDescription");
        SystemTools.CreateShortcut(ProductInfo.StartMenuShortcutPath, ProductInfo.CompanionAliasPath, "", icon, description);
        SystemTools.CreateShortcut(
            ProductInfo.StartMenuUninstallShortcutPath,
            ProductInfo.MaintenanceExePath,
            "--uninstall",
            icon,
            UiText.Get("UninstallBlurb"));
        if (options.DesktopShortcut)
            SystemTools.CreateShortcut(ProductInfo.DesktopShortcutPath, ProductInfo.CompanionAliasPath, "", icon, description);
        else
            SystemTools.TryDeleteFile(ProductInfo.DesktopShortcutPath);
    }

    internal static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ProductInfo.RunKeyPath, writable: true);
        if (enabled)
            key.SetValue(ProductInfo.RunValueName, $"\"{ProductInfo.CompanionAliasPath}\" --tray");
        else
            key.DeleteValue(ProductInfo.RunValueName, throwOnMissingValue: false);
    }

    /// <summary>After a failed update: the tray app was closed for it, so it comes back (whatever version is there).</summary>
    internal static void TryRestartApp()
    {
        if (File.Exists(ProductInfo.CompanionAliasPath) && Process.GetProcessesByName("FolderManaging.Companion").Length == 0)
            TryLaunchCompanion(AutostartArgument);
    }

    const string AutostartArgument = "--tray";

    /// <summary>
    /// Through Explorer, so the app runs as the signed-in user without administrator rights and without this
    /// process's environment, even when the setup itself was started elevated or from a console.
    /// </summary>
    static bool TryLaunchCompanion(string arguments)
    {
        for (var i = 0; i < 20 && !File.Exists(ProductInfo.CompanionAliasPath); i++)
            Thread.Sleep(250);

        if (ShellLauncher.TryStartThroughExplorer(ProductInfo.CompanionAliasPath, arguments, out var error))
        {
            SetupLog.Write($"started the app through Explorer: {arguments}");
            return true;
        }

        SetupLog.Write($"start through Explorer failed: {error}");
        try
        {
            Process.Start(new ProcessStartInfo(ProductInfo.CompanionAliasPath, arguments) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            SetupLog.Write($"launch companion failed: {ex.Message}");
            return false;
        }
    }
}
