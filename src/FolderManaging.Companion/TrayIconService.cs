// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;

namespace FolderManaging.Companion;

/// <summary>System tray icon: quick color wheel, open settings, exit.</summary>
internal sealed class TrayIconService : IDisposable
{
    readonly NotifyIcon _icon;
    readonly Icon _baseIcon;
    readonly Icon _badgeIcon;
    MainWindow? _mainWindow;
    bool _exitRequested;

    internal TrayIconService()
    {
        var loaded = LoadTrayIcon();
        _baseIcon = (Icon)loaded.Clone();
        if (!ReferenceEquals(loaded, SystemIcons.Application))
            loaded.Dispose();
        _badgeIcon = MakeBadge(_baseIcon);
        _icon = new NotifyIcon
        {
            Text = "Folder Managing",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
            Icon = _baseIcon,
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
        if (_updateVersion is not null)
        {
            var update = menu.Items.Add(UiText.Get("UpdateAvailableList"), null, (_, _) => UpdateService.Current?.ShowAvailable());
            update.Font = new Font(update.Font, System.Drawing.FontStyle.Bold);
        }

        if (UpdateService.Current is not null)
            menu.Items.Add(UiText.Get("UpdateCheckNow"), null, async (_, _) => await UpdateService.Current.CheckNowAsync());
        menu.Items.Add(UiText.Get("TraySettings"), null, (_, _) => ShowMainWindow());
        menu.Items.Add(UiText.Get("TrayExit"), null, (_, _) => ExitApp());
        return menu;
    }

    FolderManaging.Core.Updates.UpdateVersion? _updateVersion;

    /// <summary>Shows or removes the "Available updates" item and the orange mark on the tray icon.</summary>
    internal void SetUpdateAvailable(FolderManaging.Core.Updates.UpdateVersion? version)
    {
        if (_updateVersion == version && _icon.ContextMenuStrip is not null)
            return;
        _updateVersion = version;
        _icon.Icon = version is null ? _baseIcon : _badgeIcon;
        var tip = version is null ? "Folder Managing" : UiText.Format("UpdateAvailableTitle", version);
        _icon.Text = tip.Length <= 127 ? tip : tip[..127];
        RebuildMenu();
    }

    void RebuildMenu()
    {
        var oldMenu = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = BuildMenu();
        oldMenu?.Dispose();
    }

    internal void ShowBalloon(string text) =>
        _icon.ShowBalloonTip(6000, "Folder Managing", text, ToolTipIcon.Info);

    /// <summary>
    /// After a language change: XAML text is resolved when a window is built, so the main window is rebuilt in place
    /// and opens on <paramref name="page"/>.
    /// </summary>
    internal void ReloadUi(Type page)
    {
        RebuildMenu();

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

                var win = new QuickColorWindow(string.IsNullOrWhiteSpace(folderPath) ? null : [folderPath]);
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

    internal void ExitApp()
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

    /// <summary>The ordinary logo with the orange update mark Windows uses for an update that is still waiting.</summary>
    static Icon MakeBadge(Icon source)
    {
        using var sized = new Icon(source, 32, 32);
        using var bmp = sized.ToBitmap();
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const float d = 15f;
            var x = bmp.Width - d - 1;
            var y = bmp.Height - d - 1;
            using var brush = new SolidBrush(Color.FromArgb(255, 232, 102, 0));
            g.FillEllipse(brush, x, y, d, d);
            using var pen = new Pen(Color.White, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, x + 3.4f, y + 3.4f, d - 6.8f, d - 6.8f, 50, 250);
            g.DrawLine(pen, x + d - 5.4f, y + 4.6f, x + d - 3.2f, y + 6.8f);
            g.DrawLine(pen, x + d - 5.4f, y + 4.6f, x + d - 7.4f, y + 6.2f);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(handle);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon = null;
        _icon.Dispose();
        _badgeIcon.Dispose();
        _baseIcon.Dispose();
    }
}
