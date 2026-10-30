// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using System.Windows;
using FolderManaging.Companion.Localization;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Updates;
using Wpf.Ui.Controls;

namespace FolderManaging.Companion;

internal enum UpdateWindowState
{
    Checking,
    Available,
    Downloading,
    Starting,
    UpToDate,
    Error,
}

/// <summary>
/// "A new version is out": the release notes, Update / Later / Skip this version, then the download with progress.
/// The notes scroll inside the window; the buttons stay on screen. Opened by <see cref="UpdateService"/>.
/// </summary>
public partial class UpdateWindow : FluentWindow
{
    readonly UpdateService _service;
    UpdateManifest? _manifest;
    CancellationTokenSource? _download;
    Func<Task>? _retry;

    internal UpdateWindow(UpdateService service)
    {
        InitializeComponent();
        UiDirection.Apply(this);
        Notes.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en");
        _service = service;
    }

    internal UpdateWindowState State { get; private set; }

    internal void ShowChecking()
    {
        SetState(UpdateWindowState.Checking);
        Headline.Text = UiText.Get("UpdateStatusChecking");
        Body.Text = UiText.Format("UpdateCurrentVersion", UpdateVersion.Current.ToString());
        SetButtons(primary: null, secondary: UiText.Get("Close"));
    }

    internal void ShowAvailable(UpdateManifest manifest)
    {
        _manifest = manifest;
        SetState(UpdateWindowState.Available);
        Headline.Text = UiText.Format("UpdateAvailableTitle", manifest.Version);
        Body.Text = UiText.Format("UpdateAvailableBody", UpdateVersion.Current.ToString());
        Notes.Text = manifest.Notes.Length > 0 ? manifest.Notes : manifest.ReleaseUrl;
        NotesPanel.Visibility = Visibility.Visible;
        SetButtons(primary: UiText.Get("Update"), secondary: UiText.Get("UpdateLater"), skip: true);
    }

    internal void ShowProgress(long done, long total)
    {
        if (State != UpdateWindowState.Downloading)
            return;
        DownloadBar.IsIndeterminate = total <= 0;
        DownloadBar.Value = total > 0 ? 100.0 * done / total : 0;
        ProgressText.Text = UiText.Format("UpdateProgress",
            (done / 1048576.0).ToString("0.0", UiText.Culture),
            (total / 1048576.0).ToString("0.0", UiText.Culture));
    }

    internal void ShowStarting()
    {
        SetState(UpdateWindowState.Starting);
        Headline.Text = UiText.Format("UpdateInstallingTitle", _manifest?.Version.ToString() ?? "");
        Body.Text = UiText.Get("UpdateStarting");
        SetButtons(primary: null, secondary: null);
    }

    internal void ShowUpToDate()
    {
        SetState(UpdateWindowState.UpToDate);
        Headline.Text = UiText.Get("UpdateUpToDateTitle");
        Body.Text = UiText.Format("UpdateUpToDateBody", UpdateVersion.Current.ToString());
        SetButtons(primary: UiText.Get("Close"), secondary: null);
    }

    /// <param name="retry">What "Try again" does; <c>null</c> shows only Close.</param>
    internal void ShowError(string title, string message, string? details, Func<Task>? retry)
    {
        SetState(UpdateWindowState.Error);
        Headline.Text = title;
        Body.Text = message;
        if (!string.IsNullOrWhiteSpace(details))
        {
            Details.Text = details;
            Details.FlowDirection = System.Windows.FlowDirection.LeftToRight;
            Details.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            Details.Visibility = Visibility.Visible;
        }

        _retry = retry;
        SetButtons(primary: retry is null ? UiText.Get("Close") : UiText.Get("TryAgain"), secondary: retry is null ? null : UiText.Get("Close"));
    }

    void SetState(UpdateWindowState state)
    {
        State = state;
        var busy = state is UpdateWindowState.Checking or UpdateWindowState.Starting;
        Ring.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Glyph.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        Glyph.Symbol = state switch
        {
            UpdateWindowState.UpToDate => SymbolRegular.CheckmarkCircle24,
            UpdateWindowState.Error => SymbolRegular.ErrorCircle24,
            _ => SymbolRegular.ArrowCircleDown24,
        };
        NotesPanel.Visibility = state is UpdateWindowState.Available or UpdateWindowState.Downloading
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProgressPanel.Visibility = state == UpdateWindowState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        Details.Visibility = Visibility.Collapsed;
        WindowTitleBarClose(state != UpdateWindowState.Starting);
    }

    void WindowTitleBarClose(bool enabled) => Bar.ShowClose = enabled;

    void SetButtons(string? primary, string? secondary, bool skip = false)
    {
        PrimaryButton.Visibility = primary is null ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = primary;
        SecondaryButton.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
        SecondaryButton.Content = secondary;
        SkipButton.Visibility = skip ? Visibility.Visible : Visibility.Collapsed;
        Footer.Visibility = primary is null && secondary is null && !skip ? Visibility.Collapsed : Visibility.Visible;
        Refit();
    }

    /// <summary>
    /// Grows to the notes, but never past the screen. What does not fit scrolls; the buttons stay in view.
    /// <see cref="SizeToContent"/> does not notice a panel changing from collapsed to visible, which left the
    /// notes and the buttons cut off.
    /// </summary>
    void Refit()
    {
        if (Content is not FrameworkElement root)
            return;

        var max = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32);
        MaxHeight = max;

        var width = ActualWidth > 1 ? ActualWidth : Width;
        if (double.IsNaN(width) || width < 1)
            width = 580;
        root.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        var desired = root.DesiredSize.Height;
        if (desired < 1)
            desired = NotesPanel.Visibility == Visibility.Visible ? 520 : MinHeight;

        var chrome = ActualHeight > 0 && root.ActualHeight > 0 ? Math.Max(0, ActualHeight - root.ActualHeight) : 0;
        SizeToContent = SizeToContent.Manual;
        Height = Math.Clamp(desired + chrome, MinHeight, max);
        KeepOnScreen();
        UpdateLayout();
        BodyScroll.ScrollToTop();
    }

    /// <summary>
    /// Growing after the window was centered must not push the buttons below the screen.
    /// A window that does not touch the work area (the off-screen tour) stays where it was put.
    /// </summary>
    void KeepOnScreen()
    {
        if (!IsVisible || double.IsNaN(Left) || double.IsNaN(Top))
            return;
        var area = SystemParameters.WorkArea;
        var bounds = new Rect(Left, Top, ActualWidth > 1 ? ActualWidth : Width, Height);
        if (!area.IntersectsWith(bounds))
            return;
        if (Top < area.Top)
            Top = area.Top;
        if (Top + Height > area.Bottom)
            Top = Math.Max(area.Top, area.Bottom - Height);
        var width = bounds.Width;
        if (Left < area.Left)
            Left = area.Left;
        if (Left + width > area.Right)
            Left = Math.Max(area.Left, area.Right - width);
    }

    /// <summary>The corner notice already asked; start the download in this window.</summary>
    internal void StartInstall()
    {
        if (State == UpdateWindowState.Available && _manifest is not null)
            _ = InstallAsync(_manifest);
    }

    async void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        switch (State)
        {
            case UpdateWindowState.Available when _manifest is not null:
                await InstallAsync(_manifest);
                break;
            case UpdateWindowState.Error when _retry is not null:
                var retry = _retry;
                _retry = null;
                await retry();
                break;
            default:
                Close();
                break;
        }
    }

    void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        if (State == UpdateWindowState.Downloading)
        {
            _download?.Cancel();
            return;
        }

        if (State == UpdateWindowState.Available)
            _service.Later();
        Close();
    }

    void OnSkipClick(object sender, RoutedEventArgs e)
    {
        if (_manifest is not null)
            _service.Skip(_manifest.Version);
        Close();
    }

    async Task InstallAsync(UpdateManifest manifest)
    {
        using var scope = ClickTrace.Begin("companion", "update-install", detail: manifest.Version.ToString());
        _download = new CancellationTokenSource();
        SetState(UpdateWindowState.Downloading);
        Headline.Text = UiText.Format("UpdateDownloading", manifest.Version);
        Body.Text = UiText.Get("UpdateAvailableBodyShort");
        DownloadBar.IsIndeterminate = true;
        ProgressText.Text = "";
        SetButtons(primary: null, secondary: UiText.Get("Cancel"));
        try
        {
            var outcome = await _service.InstallAsync(manifest, this, _download.Token);
            if (outcome is null)
            {
                ClickTrace.Ok("installer took over");
                return;
            }

            ClickTrace.Fail(outcome.Details ?? outcome.Message);
            if (outcome.Cancelled)
                ShowAvailable(manifest);
            else
                ShowError(outcome.Title, outcome.Message, outcome.Details, outcome.CanRetry ? () => InstallAsync(manifest) : null);
        }
        finally
        {
            scope.MarkFinished();
            _download.Dispose();
            _download = null;
        }
    }

    void OnReleaseLinkClick(object sender, RoutedEventArgs e)
    {
        var url = _manifest?.ReleaseUrl ?? UpdateEndpoint.ReleasesPageUrl;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            TraceLog.Append("updates.log", $"[UPDATE] open release page failed: {ex.Message}");
        }
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (State == UpdateWindowState.Starting)
        {
            e.Cancel = true;
            return;
        }

        if (State == UpdateWindowState.Available)
            _service.Later();
        _download?.Cancel();
    }

#if FM_DEV
    /// <summary>UI tour: the download state at a given point, without downloading.</summary>
    internal void ShowDownloadingForTour(UpdateManifest manifest, long done)
    {
        _manifest = manifest;
        SetState(UpdateWindowState.Downloading);
        Headline.Text = UiText.Format("UpdateDownloading", manifest.Version);
        Body.Text = UiText.Get("UpdateAvailableBodyShort");
        Notes.Text = manifest.Notes;
        SetButtons(primary: null, secondary: UiText.Get("Cancel"));
        ShowProgress(done, manifest.SetupSize);
    }

    internal void ShowStartingForTour(UpdateManifest manifest)
    {
        _manifest = manifest;
        ShowStarting();
    }

    internal Task PressUpdateForRehearsal() =>
        _manifest is null ? Task.CompletedTask : InstallAsync(_manifest);
#endif
}

/// <summary>Why an update did not go ahead, ready to show.</summary>
internal sealed record UpdateOutcome(string Title, string Message, string? Details, bool CanRetry, bool Cancelled = false);
