using System.Diagnostics;
using FolderManaging.Core.Localization;
using Microsoft.Win32;

namespace FolderManaging.Setup;

internal sealed record InstallOptions(bool Autostart, bool DesktopShortcut, bool RestartExplorer, bool LaunchAfterInstall);

internal static class Installer
{
    /// <summary>Installed version from the uninstall entry, or <c>null</c>.</summary>
    internal static string? GetInstalledVersion()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProductInfo.UninstallKeyPath);
        return key?.GetValue("DisplayVersion") as string;
    }

    /// <returns>Warnings that did not stop the install.</returns>
    internal static IReadOnlyList<string> Run(InstallOptions options, IProgress<string> progress)
    {
        SetupLog.Write($"=== install {ProductInfo.Version} options={options}");
        var warnings = new List<string>();

        progress.Report(UiText.Get("UnpackFiles"));
        var workDir = Payload.ExtractToTempDirectory();
        try
        {
            progress.Report(SystemTools.IsDesktopRuntime8Installed()
                ? UiText.Get("PrepareWindows")
                : UiText.Get("PrepareDotnet"));
            warnings.AddRange(RunElevatedStep("install", workDir));

            progress.Report(UiText.Get("InstallingApp"));
            SystemTools.StopProcesses(ProductInfo.AppProcessNames);
            RemovePackage(ProductInfo.DevPackageName);
            InstallPackage(Path.Combine(workDir, Payload.PackageFileName));

            progress.Report(UiText.Get("ShortcutsAutostart"));
            InstallMaintenanceFiles(workDir);
            WriteUninstallEntry(Path.Combine(workDir, Payload.PackageFileName));
            CreateShortcuts(options);
            SetAutostart(options.Autostart);

            if (options.RestartExplorer)
            {
                progress.Report(UiText.Get("RestartingExplorer"));
                SystemTools.RestartExplorer();
            }

            if (options.LaunchAfterInstall && !TryLaunchCompanion())
                warnings.Add(UiText.Get("AutoOpenFail"));

            SetupLog.Write("=== install finished");
            return warnings;
        }
        finally
        {
            SystemTools.TryDeleteDirectory(workDir);
        }
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

    static bool TryLaunchCompanion()
    {
        for (var i = 0; i < 20 && !File.Exists(ProductInfo.CompanionAliasPath); i++)
            Thread.Sleep(250);

        try
        {
            Process.Start(new ProcessStartInfo(ProductInfo.CompanionAliasPath) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            SetupLog.Write($"launch companion failed: {ex.Message}");
            return false;
        }
    }
}
