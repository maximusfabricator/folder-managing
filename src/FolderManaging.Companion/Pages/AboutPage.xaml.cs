// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using System.IO;
using System.Windows;
using FolderManaging.Companion.Localization;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;

namespace FolderManaging.Companion.Pages;

public partial class AboutPage : System.Windows.Controls.UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        UiDirection.Apply(this);
        VersionText.Text = UiText.Format("VersionLine", AppInfo.Version);
        AuthorText.Text = UiText.Format("AboutAuthor", AppInfo.Author);
        CopyrightText.Text = AppInfo.Copyright;
        NameLogoText.Text = UiText.Format("AboutNameLogo", AppInfo.Author);
        SourceHintText.Text = UiText.Format("AboutSourceHint", AppInfo.Author);
        SourceUrlText.Text = AppInfo.SourceUrl;

        if (UpdateService.Current is { } updates)
        {
            Loaded += (_, _) =>
            {
                updates.StatusChanged += OnUpdateStatusChanged;
                RefreshUpdateStatus();
            };
            Unloaded += (_, _) => updates.StatusChanged -= OnUpdateStatusChanged;
        }
        else
        {
            UpdatesCard.Visibility = Visibility.Collapsed;
        }
    }

    void OnUpdateStatusChanged(object? sender, EventArgs e) => RefreshUpdateStatus();

    void RefreshUpdateStatus()
    {
        if (UpdateService.Current is not { } updates)
            return;
        UpdateStatusText.Text = updates.StatusLine();
        CheckUpdatesButton.IsEnabled = !updates.IsChecking;
    }

    async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (UpdateService.Current is { } updates)
            await updates.CheckNowAsync();
    }

    void OnOpenLicenseClick(object sender, RoutedEventArgs e) =>
        ShowShippedText(AppInfo.LicenseFileName, UiText.Get("AboutLicense"), AppInfo.LicenseUrl);

    void OnOpenNoticesClick(object sender, RoutedEventArgs e) =>
        ShowShippedText(AppInfo.NoticesFileName, UiText.Get("AboutThirdParty"), AppInfo.NoticesUrl);

    void OnOpenSourceClick(object sender, RoutedEventArgs e) => OpenInBrowser(AppInfo.SourceUrl);

    /// <summary>Shows the copy shipped next to the app; developer builds without it open the web page.</summary>
    void ShowShippedText(string fileName, string title, string fallbackUrl)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            OpenInBrowser(fallbackUrl);
            return;
        }

        TextViewerWindow.Show(Window.GetWindow(this), title, File.ReadAllText(path));
    }

    void OpenInBrowser(string url)
    {
        using var scope = ClickTrace.Begin("companion", "about-open", detail: url);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            ClickTrace.Ok();
            scope.MarkFinished();
        }
        catch (Exception ex)
        {
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            FmMessageBox.Show(Window.GetWindow(this), ex.Message, "Folder Managing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
