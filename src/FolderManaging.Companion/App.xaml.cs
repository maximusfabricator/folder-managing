// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Windows;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.QuickAccess;
using FolderManaging.Core.Updates;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace FolderManaging.Companion;

public partial class App : System.Windows.Application
{
    const string InstanceMutexName = @"Local\FolderManaging.Companion.Instance";
    const string ShowEventName = @"Local\FolderManaging.Companion.Show";

    TrayIconService? _tray;
    FolderRenameWatcher? _renameWatcher;
    bool _specialModeOnly;
    Mutex? _instanceMutex;
    EventWaitHandle? _showEvent;

    internal static TrayIconService? Tray { get; private set; }

    /// <summary>Rebuilds the windows in the current <see cref="UiText.Current"/> language once the calling handler returns.</summary>
    internal static void ReloadUi(Type page) =>
        Current.Dispatcher.BeginInvoke(() => Tray?.ReloadUi(page));

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && string.Equals(e.Args[0], "update", StringComparison.OrdinalIgnoreCase))
        {
            _specialModeOnly = true;
            Shutdown(RunConsoleUpdateCheck(e.Args));
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppBootstrapper.Initialize();
        AppThemeService.ApplyFromDocument();

#if FM_DEV
        if (Diagnostics.UiTour.TryStart(e.Args))
        {
            _specialModeOnly = true;
            return;
        }
#endif

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

        // Renamed or moved while the app was not running: follow them before anything drops "missing" folders.
        RelocateMovedFolders();
        _renameWatcher = new FolderRenameWatcher();

        // Only the tray instance repairs desktop.ini files: a second launch exits at once and would cut the work off.
        // Protected folders (Program Files, Windows) come back as "denied" and need the elevated worker,
        // otherwise state.json and the Explorer column stay apart forever.
        Task.Run(async () =>
        {
            try
            {
                var denied = FolderIconManager.RefreshAllFolderTags();
                foreach (var path in denied)
                {
                    var error = await FolderWriteGuard.SyncAsync(owner: null, path);
                    if (error is null)
                        TraceLog.Append("shell-menu.log", $"[BOOT] elevated sync ok '{path}'");
                    else
                        TraceLog.Append("shell-menu.log", $"[BOOT] elevated sync failed '{path}': {error}");
                }
            }
            catch (Exception ex)
            {
                TraceLog.Append("shell-menu.log", $"[BOOT] RefreshAllFolderTags failed: {ex.Message}");
            }

            // Quick access: finish a re-pin cut off last time, re-pin pins that show an old picture, then let the
            // "Pinned" column follow what Windows really has pinned.
            try
            {
                QuickAccessService.RefreshStalePins(automatic: true);
                var pins = QuickAccessService.GetPinned();
                await Dispatcher.InvokeAsync(() =>
                {
                    if (QuickAccessService.SyncPinnedFlags(CompanionState.Document, pins))
                        CompanionState.Save();
                });
            }
            catch (Exception ex)
            {
                TraceLog.Append("pins.log", $"[BOOT] Quick access check failed: {ex.Message}");
            }
        });

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        UpdateService.Start();
        _tray = new TrayIconService();
        Tray = _tray;
        if (e.Args.Any(a => string.Equals(a.Trim(), UpdateHandoff.UpdatedSwitch, StringComparison.OrdinalIgnoreCase)))
            _tray.ShowBalloon(UiText.Format("UpdatedBalloon", AppInfo.Version));

        if (trayOnly)
            return;

        var main = new MainWindow();
        MainWindow = main;
        _tray.AttachMainWindow(main);
        main.Show();
    }

    /// <summary>
    /// <c>foldermanaging update --check [--json]</c>: the same report as <c>foldermanaging-cli update --check</c>,
    /// printed into the console that started the app (the CLI alias also returns the exit code to the console).
    /// </summary>
    static int RunConsoleUpdateCheck(string[] args)
    {
        AttachConsole(AttachParentProcess);
        if (!UpdateSchedule.IsSupported || !args.Skip(1).Any(a => string.Equals(a, "--check", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Usage: foldermanaging update --check [--json]");
            return 64;
        }

        var json = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        var (exit, text) = Task.Run(() => UpdateCheckReport.RunAsync(json)).GetAwaiter().GetResult();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine();
        Console.WriteLine(text);
        return exit;
    }

    const int AttachParentProcess = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    /// <summary>Points entries at the folders' new paths and rewrites their <c>desktop.ini</c> in the background.</summary>
    internal static void RelocateMovedFolders()
    {
        try
        {
            var moved = FolderRelocator.FindMoved(CompanionState.Document);
            if (moved.Count == 0)
                return;
            CompanionState.Save();
            foreach (var (from, to) in moved)
                TraceLog.Append("shell-menu.log", $"[BOOT] folder moved '{from}' -> '{to}'");
            SyncMovedInBackground(moved.Select(m => m.To).ToList());
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[BOOT] relocate failed: {ex.Message}");
        }
    }

    /// <summary>The icon file is named after the folder path; a protected folder keeps its old one until Repair.</summary>
    internal static void SyncMovedInBackground(IReadOnlyList<string> paths) =>
        Task.Run(() =>
        {
            foreach (var path in paths)
            {
                try
                {
                    FolderIconManager.SyncFolderTags(path);
                }
                catch (Exception ex)
                {
                    TraceLog.Append("shell-menu.log", $"sync after move '{path}': {ex.Message}");
                }
            }
        });

    protected override void OnExit(ExitEventArgs e)
    {
        _renameWatcher?.Dispose();
        UpdateService.Current?.Dispose();
        _tray?.Dispose();
        Tray = null;
        CompanionState.Shutdown();
        AppThemeService.Shutdown();
        _showEvent?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>An error in a button handler shows a message; the app and the tray stay alive.</summary>
    void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        TraceLog.Append("shell-menu.log", $"[COMPANION] [FAIL] unhandled UI error: {e.Exception}");
        FmMessageBox.Show(
            UiText.Format("UnexpectedError", e.Exception.Message),
            "Folder Managing",
            MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
        e.Handled = true;
    }

    /// <summary>
    /// One tray per user session. A second launch (Start menu, context menu “Settings”) asks the first one
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
    /// <c>--quick-color [--path PATH]…</c> (one <c>--path</c> per selected folder).
    /// Returns true when the app should not open the main settings window.
    /// </summary>
    bool TryRunSpecialMode(string[] args)
    {
        if (args.Length == 0)
            return false;

        string? mode = null;
        var paths = new List<string>();
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
                paths.Add(args[++i].Trim().Trim('"'));
                continue;
            }
        }

        if (mode is null)
            return false;

        ClickTrace.Step($"Companion special mode={mode} paths={paths.Count} first='{paths.FirstOrDefault() ?? "(none)"}'");

        var win = new QuickColorWindow(paths);
        win.Closed += (_, _) =>
        {
            if (_specialModeOnly)
                Shutdown();
        };
        win.Show();
        return true;
    }
}
