// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Interop;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Updates;

namespace FolderManaging.Companion;

/// <summary>
/// Update checks of the tray app (see <see cref="UpdateSchedule"/>): about 90 seconds after start, then once a day.
/// Automatic checks are silent when they fail (error log only). A found version opens <see cref="UpdateWindow"/>
/// without taking the focus, and only when Windows says the user is not in a full-screen app, a presentation or
/// focus time; otherwise it waits and the tray menu offers the update meanwhile.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);
    static readonly TimeSpan BusyRetry = TimeSpan.FromMinutes(10);
    static readonly TimeSpan MaxTimerWait = TimeSpan.FromHours(1);
    static readonly TimeSpan SetupStartTimeout = TimeSpan.FromSeconds(90);

    readonly DispatcherTimer _timer;
    readonly DateTime _startedUtc = DateTime.UtcNow;
    DateTime _notBeforeUtc = DateTime.MinValue;
    UpdateWindow? _window;
    bool _checking;
    bool _manualWaiting;
    bool _offerWaiting;

    UpdateService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += OnTick;
    }

    internal static UpdateService? Current { get; private set; }

    /// <summary>The newer version found this session (also when skipped), or <c>null</c>.</summary>
    internal UpdateManifest? Available { get; private set; }

    internal bool IsChecking => _checking;

    /// <summary>The last check of this session failed.</summary>
    internal UpdateException? LastError { get; private set; }

    /// <summary>The last check of this session found no newer version.</summary>
    internal bool UpToDate { get; private set; }

    internal event EventHandler? StatusChanged;

    internal static void Start()
    {
        if (!UpdateSchedule.IsSupported || Current is not null)
            return;
        Current = new UpdateService();
        Current.Schedule();
        Task.Run(() => SafeClean(null));
    }

    /// <summary>One line for Settings and About.</summary>
    internal string StatusLine()
    {
        string line;
        if (_checking)
            line = UiText.Get("UpdateStatusChecking");
        else if (Available is { } available)
            line = UiText.Format("UpdateStatusAvailable", available.Version);
        else if (LastError is { } error)
            line = UiText.Format("UpdateStatusFailed", Describe(error, checking: true).Message);
        else if (UpToDate)
            line = UiText.Format("UpdateStatusUpToDate", FormatTime(CompanionState.Document.UpdateLastCheckUtc));
        else if (CompanionState.Document.UpdateLastCheckUtc is { } last)
            line = UiText.Format("UpdateStatusLastCheck", FormatTime(last));
        else
            line = UiText.Get("UpdateStatusNever");

        return CompanionState.Document.CheckForUpdates ? line : line + " " + UiText.Get("UpdateStatusOff");
    }

    static string FormatTime(DateTime? utc) =>
        utc is { } t ? t.ToLocalTime().ToString("g", UiText.Culture) : "";

    /// <summary>The toggle in Settings changed: the next check is planned again.</summary>
    internal void SettingsChanged()
    {
        Schedule();
        Raise();
    }

    void Schedule()
    {
        _timer.Stop();
        var now = DateTime.UtcNow;
        var due = UpdateSchedule.NextCheckUtc(CompanionState.Document.UpdateLastCheckUtc, _startedUtc, now);
        if (due < _notBeforeUtc)
            due = _notBeforeUtc;
        if (_offerWaiting && now + BusyRetry < due)
            due = now + BusyRetry;
        var wait = due - now;
        if (wait < TimeSpan.FromSeconds(1))
            wait = TimeSpan.FromSeconds(1);
        // Checked again at least hourly: sleep, a changed clock or the toggle can move the due time.
        _timer.Interval = wait > MaxTimerWait ? MaxTimerWait : wait;
        _timer.Start();
    }

    async void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        try
        {
            var now = DateTime.UtcNow;
            if (_offerWaiting && Available is { } waiting)
                Offer(waiting, manual: false);

            var doc = CompanionState.Document;
            var due = UpdateSchedule.NextCheckUtc(doc.UpdateLastCheckUtc, _startedUtc, now);
            if (doc.CheckForUpdates && now >= due && now >= _notBeforeUtc)
                await CheckAsync(manual: false);
        }
        catch (Exception ex)
        {
            TraceLog.Append("updates.log", $"[UPDATE] scheduled check failed: {ex}");
        }
        finally
        {
            Schedule();
        }
    }

    /// <summary>"Check for updates": always ends with a window that says what happened.</summary>
    internal Task CheckNowAsync() => CheckAsync(manual: true);

    async Task CheckAsync(bool manual)
    {
        if (manual)
            EnsureWindow(activate: true).ShowChecking();
        if (_checking)
        {
            _manualWaiting |= manual;
            return;
        }

        _checking = true;
        Raise();
        try
        {
            using var client = new UpdateClient();
            var result = await client.CheckAsync();
            manual |= _manualWaiting;
            _manualWaiting = false;
            LastError = null;
            CompanionState.Document.UpdateLastCheckUtc = DateTime.UtcNow;
            CompanionState.Save();
            Available = result.IsNewer ? result.Manifest : null;
            UpToDate = !result.IsNewer;
            _checking = false;
            Raise();

            var doc = CompanionState.Document;
            if (UpdateSchedule.ShouldOffer(result.Manifest.Version, result.Current, doc.UpdateSkippedVersion, manual))
                Offer(result.Manifest, manual);
            else if (manual)
                EnsureWindow(activate: true).ShowUpToDate();
        }
        catch (Exception ex) when (ex is UpdateException or HttpRequestException or IOException)
        {
            var error = ex as UpdateException ?? new UpdateException(UpdateError.Network, ex.Message, ex);
            manual |= _manualWaiting;
            _manualWaiting = false;
            LastError = error;
            UpToDate = false;
            _notBeforeUtc = DateTime.UtcNow + RetryAfterFailure;
            TraceLog.Append("updates.log", $"[UPDATE] check failed ({error.Error}): {error.Message}");
            _checking = false;
            Raise();
            if (manual)
            {
                var (title, message) = Describe(error, checking: true);
                EnsureWindow(activate: true).ShowError(title, message, error.Message, CheckNowAsync);
            }
        }
        finally
        {
            _checking = false;
        }
    }

    void Offer(UpdateManifest manifest, bool manual)
    {
        if (!manual && !UserAcceptsWindows())
        {
            _offerWaiting = true;
            Raise();
            return;
        }

        _offerWaiting = false;
        var window = EnsureWindow(activate: manual);
        if (window.State is UpdateWindowState.Downloading or UpdateWindowState.Starting)
            return;
        window.ShowAvailable(manifest);
    }

    /// <summary>"Later": nothing is stored; the next daily check shows the window again.</summary>
    internal void Later()
    {
        _offerWaiting = false;
        Raise();
    }

    /// <summary>"Skip this version": not offered again by automatic checks; a newer version is.</summary>
    internal void Skip(UpdateVersion version)
    {
        _offerWaiting = false;
        CompanionState.Document.UpdateSkippedVersion = version.ToString();
        CompanionState.Save();
        Raise();
    }

    /// <summary>
    /// Downloads and checks the setup, then lets it replace the app. Returns <c>null</c> when the setup took over
    /// (the app is closing), otherwise what went wrong.
    /// </summary>
    internal async Task<UpdateOutcome?> InstallAsync(UpdateManifest manifest, UpdateWindow window, CancellationToken cancellationToken)
    {
        VerifiedSetup setup;
        try
        {
            using var client = new UpdateClient();
            var progress = new Progress<(long Done, long Total)>(p => window.ShowProgress(p.Done, p.Total));
            setup = await client.DownloadSetupAsync(manifest, UpdatePaths.DownloadDirectory, progress, cancellationToken);
        }
        catch (UpdateException ex) when (ex.Error == UpdateError.Cancelled)
        {
            return new UpdateOutcome("", "", null, CanRetry: true, Cancelled: true);
        }
        catch (UpdateException ex)
        {
            TraceLog.Append("updates.log", $"[UPDATE] download failed ({ex.Error}): {ex.Message}");
            var (title, message) = Describe(ex);
            return new UpdateOutcome(title, message, ex.Message, CanRetry: true);
        }

        using (setup)
        {
#if FM_DEV
            if (Environment.GetEnvironmentVariable("FM_UPDATE_DRY_RUN") == "1")
            {
                DryRunSetup = setup.Path;
                TraceLog.Append("updates.log", $"[UPDATE] dry run: verified {setup.Path}, not started");
                return new UpdateOutcome(UiText.Get("UpdateFailedTitle"), "Dry run: the verified setup was not started.", setup.Path, CanRetry: false);
            }
#endif
            window.ShowStarting();
            CompanionState.Save();
            var statusPath = UpdateHandoff.NewStatusPath(UpdatePaths.DownloadDirectory);
            var arguments = $"{UpdateHandoff.UpdateSwitch} {UpdateHandoff.StatusSwitch} \"{statusPath}\"";
            TraceLog.Append("updates.log", $"[UPDATE] starting {setup.Path} {arguments}");
            if (!ShellLauncher.Start(setup.Path, arguments))
                return new UpdateOutcome(UiText.Get("UpdateFailedTitle"), UiText.Get("UpdateErrorNoStart"), null, CanRetry: true);

            var processName = Path.GetFileNameWithoutExtension(setup.Path);
            var started = false;
            var clock = Stopwatch.StartNew();
            var lastProcessCheck = TimeSpan.Zero;
            while (true)
            {
                await Task.Delay(250, CancellationToken.None);
                var last = UpdateHandoff.ReadLast(statusPath);
                if (!started && last is not null)
                {
                    started = true;
                    // The setup runs from its own mapped image now; the file no longer needs our lock.
                    setup.Handle.Dispose();
                }

                switch (last?.Stage)
                {
                    case UpdateStage.Cancelled:
                        TraceLog.Append("updates.log", "[UPDATE] cancelled in the Windows prompt (failed to elevate)");
                        return new UpdateOutcome(UiText.Get("UpdateFailedTitle"), UiText.Get("UpdateCancelled"), null, CanRetry: true);
                    case UpdateStage.Failed:
                        TraceLog.Append("updates.log", $"[UPDATE] setup failed: {last.Value.Message}");
                        return new UpdateOutcome(UiText.Get("UpdateFailedTitle"), UiText.Format("UpdateErrorSetup", last.Value.Message), null, CanRetry: true);
                    case UpdateStage.Installing or UpdateStage.Done:
                        CloseForUpdate();
                        return null;
                }

                if (!started && clock.Elapsed > SetupStartTimeout)
                    return new UpdateOutcome(UiText.Get("UpdateFailedTitle"), UiText.Get("UpdateErrorNoStart"), null, CanRetry: true);

                if (started && clock.Elapsed - lastProcessCheck > TimeSpan.FromSeconds(2))
                {
                    lastProcessCheck = clock.Elapsed;
                    if (Process.GetProcessesByName(processName).Length == 0 &&
                        UpdateHandoff.ReadLast(statusPath) is not { Stage: not UpdateStage.Started })
                    {
                        TraceLog.Append("updates.log", "[UPDATE] setup ended without a result (failed)");
                        return new UpdateOutcome(UiText.Get("UpdateFailedTitle"), UiText.Get("UpdateErrorNoStart"), null, CanRetry: true);
                    }
                }
            }
        }
    }

#if FM_DEV
    /// <summary>Developer rehearsal: the setup that a dry run verified and kept from starting.</summary>
    internal static string? DryRunSetup { get; private set; }

    /// <summary>UI tour and rehearsal: the service without its timer.</summary>
    internal static UpdateService ForTour() => Current ??= new UpdateService();

    /// <summary>UI tour: keeps the window off screen.</summary>
    internal static Action<Window>? BeforeShowForTour { get; set; }

    internal UpdateWindow OpenWindowForTour() => EnsureWindow(activate: false);

    /// <summary>Rehearsal: an automatic check, offered as if the user were free to see it.</summary>
    internal async Task<UpdateManifest?> CheckForRehearsalAsync()
    {
        using var client = new UpdateClient();
        var result = await client.CheckAsync();
        Available = result.IsNewer ? result.Manifest : null;
        if (Available is { } manifest && UpdateSchedule.ShouldOffer(manifest.Version, result.Current, CompanionState.Document.UpdateSkippedVersion, manual: false))
            EnsureWindow(activate: false).ShowAvailable(manifest);
        return Available;
    }
#endif

    /// <summary>The setup replaces the app now: close properly so the tray icon goes away and nothing is lost.</summary>
    static void CloseForUpdate()
    {
        TraceLog.Append("updates.log", "[UPDATE] closing for the update");
        CompanionState.Save();
        if (App.Tray is { } tray)
            tray.ExitApp();
        else
            System.Windows.Application.Current.Shutdown();
    }

    UpdateWindow EnsureWindow(bool activate)
    {
        if (_window is { IsLoaded: true })
        {
            if (activate)
            {
                _window.WindowState = WindowState.Normal;
                _window.Activate();
            }

            return _window;
        }

        _window = new UpdateWindow(this) { ShowActivated = activate };
        _window.Closed += (_, _) => _window = null;
#if FM_DEV
        BeforeShowForTour?.Invoke(_window);
#endif
        _window.Show();
        return _window;
    }

    /// <summary>Opens the window with the version found earlier (tray menu).</summary>
    internal void ShowAvailable()
    {
        if (Available is { } manifest)
            Offer(manifest, manual: true);
    }

    void Raise()
    {
        App.Tray?.SetUpdateAvailable(Available is { } a && IsNotSkipped(a) ? a.Version : null);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    static bool IsNotSkipped(UpdateManifest manifest) =>
        !string.Equals(CompanionState.Document.UpdateSkippedVersion, manifest.Version.ToString(), StringComparison.Ordinal);

    internal static (string Title, string Message) Describe(UpdateException error, bool checking = false)
    {
        var title = UiText.Get(checking ? "UpdateCheckFailedTitle" : "UpdateFailedTitle");
        var message = error.Error switch
        {
            UpdateError.NotFound => UiText.Get("UpdateErrorNotFound"),
            UpdateError.BadSignature or UpdateError.BadManifest or UpdateError.BlockedHost => UiText.Get("UpdateErrorTrust"),
            UpdateError.BadDownload or UpdateError.TooLarge => UiText.Get("UpdateErrorDownload"),
            UpdateError.Disk => UiText.Format("UpdateErrorDisk", error.Message),
            _ => UiText.Get("UpdateErrorNetwork"),
        };
        return (title, message);
    }

    static void SafeClean(string? keep)
    {
        try
        {
            UpdateClient.CleanDirectory(UpdatePaths.DownloadDirectory, keep);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TraceLog.Append("updates.log", $"[UPDATE] cleanup failed: {ex.Message}");
        }
    }

    /// <summary>False during full-screen games and videos, presentations, focus time and on the lock screen.</summary>
    static bool UserAcceptsWindows()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) != 0 || state == QunsAcceptsNotifications;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    const int QunsAcceptsNotifications = 5;

    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);

    public void Dispose() => _timer.Stop();
}
