// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

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
        _icon = new NotifyIcon
        {
            Text = "Folder Managing",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
            Icon = LoadTrayIcon(),
        };
        _icon.DoubleClick += (_, _) => OpenQuickColor();
        ClickTrace.Step("tray icon created");
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            RightToLeft = UiText.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No,
        };
        menu.Items.Add(UiText.Get("TrayPaint"), null, (_, _) => OpenQuickColor());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiText.Get("TraySettings"), null, (_, _) => ShowMainWindow());
        menu.Items.Add(UiText.Get("TrayExit"), null, (_, _) => ExitApp());
        return menu;
    }

    /// <summary>
    /// After a language change: XAML text is resolved when a window is built, so the main window is rebuilt in place
    /// and opens on <paramref name="page"/>.
    /// </summary>
    internal void ReloadUi(Type page)
    {
        var oldMenu = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = BuildMenu();
        oldMenu?.Dispose();

        var old = _mainWindow;
        var fresh = new MainWindow(page);
        if (old is not null)
        {
            var bounds = old.WindowState == WindowState.Normal
                ? new Rect(old.Left, old.Top, old.Width, old.Height)
                : old.RestoreBounds;
            fresh.WindowStartupLocation = WindowStartupLocation.Manual;
            fresh.Left = bounds.Left;
            fresh.Top = bounds.Top;
            fresh.Width = bounds.Width;
            fresh.Height = bounds.Height;
            if (old.WindowState == WindowState.Maximized)
                fresh.WindowState = WindowState.Maximized;
        }

        AttachMainWindow(fresh);
        System.Windows.Application.Current.MainWindow = fresh;
        fresh.Show();
        fresh.Activate();

        if (old is not null)
        {
            old.Closing -= OnMainWindowClosing;
            old.Close();
        }

        ClickTrace.Step($"ui reloaded for language {UiText.Current.Code}");
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
            var uri = new Uri("pack://application:,,,/Assets/FolderManaging.ico", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo is not null)
            {
                // Clone so the stream can close and both the Win11 tray and the overflow flyout keep the logo.
                using var icon = new Icon(streamInfo.Stream, 32, 32);
                return (Icon)icon.Clone();
            }
        }
        catch
        {
            // fall through
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var associated = Icon.ExtractAssociatedIcon(exe);
                if (associated is not null)
                    return associated;
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
