// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Windows;
using FolderManaging.Companion.Localization;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Updates;
using Wpf.Ui.Controls;

namespace FolderManaging.Companion;

/// <summary>
/// The corner notice that stays until the user updates or chooses Later, the way Windows keeps
/// "Available updates" on screen. It does not take the focus.
/// </summary>
public partial class UpdateNoticeWindow : FluentWindow
{
    readonly UpdateService _service;
    readonly UpdateManifest _manifest;
    bool _installing;
    bool _closedFromService;

    internal UpdateNoticeWindow(UpdateService service, UpdateManifest manifest)
    {
        InitializeComponent();
        UiDirection.Apply(this);
        _service = service;
        _manifest = manifest;
        Headline.Text = UiText.Format("UpdateAvailableTitle", manifest.Version);
        Body.Text = UiText.Format("UpdateAvailableBody", UpdateVersion.Current.ToString());
        ContentRendered += (_, _) => Fit();
        SizeChanged += (_, _) => Fit();
    }

    bool _fitted;

    /// <summary>
    /// The notice is only as tall as its text and buttons. Measuring before the window has its real width
    /// wraps every word and leaves an empty band under the buttons.
    /// </summary>
    void Fit()
    {
        if (_fitted)
            return;
        var width = ActualWidth > 200 ? ActualWidth : Width;
        if (double.IsNaN(width) || width < 200)
            return;
        _fitted = true;
        Card.InvalidateMeasure();
        Card.Measure(new System.Windows.Size(width - 40, double.PositiveInfinity));
        var title = Bar.ActualHeight > 1 ? Bar.ActualHeight : 40;
        var desired = Card.DesiredSize.Height + title + 4;
        if (desired < 100)
            desired = 160;
        SizeToContent = SizeToContent.Manual;
        MinHeight = 0;
        Height = desired;
        Place();
    }

    /// <summary>Puts the notice in the corner above the taskbar. An off-screen tour window is left where it was put.</summary>
    internal void Place()
    {
        if (Left < -1000 || Top < -1000)
            return;
        var area = SystemParameters.WorkArea;
        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : 180;
        Left = UiText.IsRightToLeft ? area.Left + 12 : area.Right - width - 12;
        Top = area.Bottom - height - 12;
    }

    internal void DismissWithoutCallback()
    {
        _closedFromService = true;
        Close();
    }

    void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        _installing = true;
        var manifest = _manifest;
        Close();
        _service.StartInstall(manifest);
    }

    void OnLaterClick(object sender, RoutedEventArgs e) => Close();

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closedFromService)
            return;
        _closedFromService = true;
        if (!_installing)
            _service.NoticeDismissed();
    }
}
