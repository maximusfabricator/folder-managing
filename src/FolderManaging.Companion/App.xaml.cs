using System.Windows;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace FolderManaging.Companion;

public partial class App : System.Windows.Application
{
    TrayIconService? _tray;
    bool _specialModeOnly;

    internal static TrayIconService? Tray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppBootstrapper.Initialize();
        AppThemeService.ApplyFromDocument();

        if (TryRunSpecialMode(e.Args))
        {
            _specialModeOnly = true;
            return;
        }

        _tray = new TrayIconService();
        Tray = _tray;

        var main = new MainWindow();
        MainWindow = main;
        _tray.AttachMainWindow(main);
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        Tray = null;
        CompanionState.Shutdown();
        AppThemeService.Shutdown();
        base.OnExit(e);
    }

    /// <summary>
    /// <c>--quick-color [--path PATH]</c>.
    /// Returns true when the app should not open the main settings window.
    /// </summary>
    bool TryRunSpecialMode(string[] args)
    {
        if (args.Length == 0)
            return false;

        string? mode = null;
        string? path = null;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i].Trim();
            if (string.Equals(a, "--quick-color", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "quick-color", StringComparison.OrdinalIgnoreCase))
            {
                mode = "quick-color";
                continue;
            }

            if (string.Equals(a, "--path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                path = args[++i].Trim().Trim('"');
                continue;
            }
        }

        if (mode is null)
            return false;

        ClickTrace.Step($"Companion special mode={mode} path='{path ?? "(none)"}'");

        var win = new QuickColorWindow(path);
        win.Closed += (_, _) =>
        {
            if (_specialModeOnly)
                Shutdown();
        };
        win.Show();
        return true;
    }
}
