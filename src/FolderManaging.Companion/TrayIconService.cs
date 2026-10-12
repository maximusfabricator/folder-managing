using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;

namespace FolderManaging.Companion;

/// <summary>System tray icon: quick color wheel, open settings, exit.</summary>
internal sealed class TrayIconService : IDisposable
{
    readonly NotifyIcon _icon;
    MainWindow? _mainWindow;
    bool _exitRequested;

    internal TrayIconService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(UiText.Get("TrayPaint"), null, (_, _) => OpenQuickColor());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiText.Get("TraySettings"), null, (_, _) => ShowMainWindow());
        menu.Items.Add(UiText.Get("TrayExit"), null, (_, _) => ExitApp());

        _icon = new NotifyIcon
        {
            Text = "Folder Managing",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = LoadTrayIcon(),
        };
        _icon.DoubleClick += (_, _) => OpenQuickColor();
        ClickTrace.Step("tray icon created");
    }

    internal bool ExitRequested => _exitRequested;

    internal void AttachMainWindow(MainWindow window)
    {
        _mainWindow = window;
        window.Closing += OnMainWindowClosing;
    }

    void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested)
            return;
        e.Cancel = true;
        _mainWindow?.Hide();
        ClickTrace.Step("main window hidden to tray");
    }

    internal void OpenQuickColor(string? folderPath = null)
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (Window w in System.Windows.Application.Current.Windows)
                {
                    if (w is QuickColorWindow existing)
                    {
                        existing.Activate();
                        existing.Topmost = true;
                        existing.Topmost = false;
                        return;
                    }
                }

                var win = new QuickColorWindow(folderPath);
                win.Show();
                win.Activate();
            });
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"tray quick-color failed: {ex.Message}");
        }
    }

    internal void ShowMainWindow()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_mainWindow is null)
            {
                _mainWindow = new MainWindow();
                AttachMainWindow(_mainWindow);
            }

            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    void ExitApp()
    {
        _exitRequested = true;
        ClickTrace.Step("tray exit");
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _icon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        });
    }

    static Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo is not null)
            {
                using var bmp = new Bitmap(streamInfo.Stream);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch
        {
            // fall through
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
