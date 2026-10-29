// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Windows;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Updates;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FolderManaging.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        UiText.ApplyToProcess();

        if (e.Args.Length > 0 && string.Equals(e.Args[0], ElevatedTasks.Switch, StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(ElevatedTasks.Run(e.Args));
            return;
        }

#if FM_DEV
        if (SetupUiTour.TryStart(this, e.Args))
        {
            ApplicationThemeManager.Apply(ReadSystemTheme(), WindowBackdropType.Mica, updateAccent: true);
            return;
        }
#endif

        var uninstall = !Payload.IsEmbedded || HasSwitch(e.Args, "--uninstall");
        if (!uninstall && HasSwitch(e.Args, UpdateHandoff.UpdateSwitch))
        {
            var statusPath = ValueAfter(e.Args, UpdateHandoff.StatusSwitch);
            UpdateHandoff.Write(statusPath, UpdateStage.Started);
            ApplicationThemeManager.Apply(ReadSystemTheme(), WindowBackdropType.Mica, updateAccent: true);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var updateWindow = new SetupWindow(SetupMode.Update, statusPath);
            MainWindow = updateWindow;
            updateWindow.Closed += (_, _) => Shutdown(updateWindow.ExitCode);
            updateWindow.Show();
            return;
        }

        if (HasSwitch(e.Args, "--quiet"))
        {
            Shutdown(RunQuiet(uninstall));
            return;
        }

        ApplicationThemeManager.Apply(ReadSystemTheme(), WindowBackdropType.Mica, updateAccent: true);

        var window = new SetupWindow(uninstall ? SetupMode.Uninstall : SetupMode.Install);
        MainWindow = window;
        window.Show();
    }

    static bool HasSwitch(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(a, "/" + name.TrimStart('-'), StringComparison.OrdinalIgnoreCase));

    static string? ValueAfter(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && args[i + 1].Length > 0 ? args[i + 1] : null;
    }

    /// <summary>
    /// <c>--quiet</c>: no window, default options (autostart on, no desktop shortcut), result in the setup log and
    /// exit code. Updates use <c>--update</c>, which keeps the user's choices.
    /// </summary>
    static int RunQuiet(bool uninstall)
    {
        var progress = new LogProgress();
        try
        {
            var warnings = uninstall
                ? Uninstaller.Run(new UninstallOptions(ResetFolders: true, DeleteSettings: false), progress)
                : Installer.Run(new InstallOptions(Autostart: true, DesktopShortcut: false, RestartExplorer: true, LaunchAfterInstall: false), progress);
            foreach (var w in warnings)
                SetupLog.Write("warning: " + w);
            return 0;
        }
        catch (Exception ex)
        {
            SetupLog.Write($"FAILED: {ex}");
            return ex is OperationCanceledException ? 2 : 1;
        }
    }

    sealed class LogProgress : IProgress<string>
    {
        public void Report(string value) => SetupLog.Write(value);
    }

    static ApplicationTheme ReadSystemTheme()
    {
        if (SystemParameters.HighContrast)
            return ApplicationTheme.HighContrast;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 0 ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }
}
