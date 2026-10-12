using System.Windows;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace FolderManaging.Companion;

public partial class App : System.Windows.Application
{
    const string InstanceMutexName = @"Local\FolderManaging.Companion.Instance";
    const string ShowEventName = @"Local\FolderManaging.Companion.Show";

    TrayIconService? _tray;
    bool _specialModeOnly;
    Mutex? _instanceMutex;
    EventWaitHandle? _showEvent;

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

        var trayOnly = e.Args.Any(a => string.Equals(a.Trim(), AutostartService.TrayArgument, StringComparison.OrdinalIgnoreCase));
        if (!TryBecomePrimaryInstance(showExisting: !trayOnly))
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _tray = new TrayIconService();
        Tray = _tray;

        if (trayOnly)
            return;

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
        _showEvent?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// One tray per user session. A second launch (Start menu, context menu «Настройки») asks the first one
    /// to show its window and exits.
    /// </summary>
    bool TryBecomePrimaryInstance(bool showExisting)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            if (showExisting)
            {
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                    existing.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // first instance is still starting up
                }
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            try
            {
                while (_showEvent.WaitOne())
                    Dispatcher.BeginInvoke(() => Tray?.ShowMainWindow());
            }
            catch (ObjectDisposedException)
            {
                // app exiting
            }
        })
        {
            IsBackground = true,
            Name = "Companion show-request listener",
        };
        listener.Start();
        return true;
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
