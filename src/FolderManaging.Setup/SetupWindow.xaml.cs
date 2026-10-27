// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Windows;
using System.Windows.Markup;
using FolderManaging.Core;
using FolderManaging.Core.Localization;
using Wpf.Ui.Controls;

namespace FolderManaging.Setup;

internal enum SetupMode
{
    Install,
    Uninstall,
}

public partial class SetupWindow : FluentWindow
{
    readonly SetupMode _mode;
    bool _busy;
    bool _finished;

    internal SetupWindow(SetupMode mode)
    {
        InitializeComponent();
        FlowDirection = UiText.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Language = XmlLanguage.GetLanguage(UiText.Current.Code);
        _mode = mode;
        SubtitleText.Text = UiText.Format("VersionLine", ProductInfo.Version);
        CopyrightText.Text = AppInfo.Copyright;

        if (mode == SetupMode.Uninstall)
        {
            Title = UiText.Get("SetupUninstallTitle");
            ShowPage(UninstallPage);
            PrimaryButton.Content = UiText.Get("Uninstall");
            PrimaryButton.Appearance = ControlAppearance.Danger;
            return;
        }

        Title = UiText.Get("SetupInstallTitle");
        ShowPage(InstallPage);
        var installed = Installer.GetInstalledVersion();
        if (installed is not null)
        {
            InstalledVersionText.Text = installed == ProductInfo.Version
                ? UiText.Get("AlreadyInstalled")
                : UiText.Format("WillUpdate", installed);
            InstalledVersionText.Visibility = Visibility.Visible;
            PrimaryButton.Content = installed == ProductInfo.Version ? UiText.Get("Reinstall") : UiText.Get("Update");
        }

        if (Environment.OSVersion.Version.Build < 22000)
        {
            InstalledVersionText.Text = installed is null
                ? UiText.Get("Win10Note")
                : InstalledVersionText.Text + Environment.NewLine + UiText.Get("Win10Note");
            InstalledVersionText.Visibility = Visibility.Visible;
        }
    }

    void ShowPage(UIElement page)
    {
        foreach (var p in new UIElement[] { InstallPage, UninstallPage, ProgressPage, ResultPage })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
    }

    async void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        if (_finished)
        {
            Close();
            return;
        }

        _busy = true;
        ShowPage(ProgressPage);
        PrimaryButton.Visibility = Visibility.Collapsed;
        SecondaryButton.IsEnabled = false;
        WindowTitleBar.ShowClose = false;
        var progress = new Progress<string>(text => ProgressText.Text = text);

        try
        {
            IReadOnlyList<string> warnings;
            if (_mode == SetupMode.Install)
            {
                var options = new InstallOptions(
                    AutostartCheck.IsChecked == true,
                    DesktopShortcutCheck.IsChecked == true,
                    RestartExplorerCheck.IsChecked == true,
                    LaunchCheck.IsChecked == true);
                warnings = await Task.Run(() => Installer.Run(options, progress));
                ShowResult(
                    success: true,
                    UiText.Get("Done"),
                    UiText.Get("InstalledBody"),
                    warnings);
            }
            else
            {
                var options = new UninstallOptions(ResetFoldersCheck.IsChecked == true, DeleteSettingsCheck.IsChecked == true);
                warnings = await Task.Run(() => Uninstaller.Run(options, progress));
                ShowResult(success: true, UiText.Get("Removed"), UiText.Get("RemovedBody"), warnings);
            }
        }
        catch (OperationCanceledException ex)
        {
            SetupLog.Write($"cancelled: {ex.Message}");
            ShowResult(
                success: false,
                UiText.Get("NotFinished"),
                _mode == SetupMode.Install
                    ? UiText.Get("NeedAdminInstall")
                    : UiText.Get("NeedAdminUninstall"),
                []);
        }
        catch (Exception ex)
        {
            SetupLog.Write($"FAILED: {ex}");
            ShowResult(success: false, UiText.Get("Error"), ex.Message, [UiText.Format("DetailsLog", SetupLog.FilePath)]);
        }
    }

    void ShowResult(bool success, string title, string text, IReadOnlyList<string> details)
    {
        _busy = false;
        _finished = true;
        ResultTitle.Text = title;
        ResultText.Text = text;
        ResultIcon.Symbol = success ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.ErrorCircle24;
        if (details.Count > 0)
        {
            SetText(ResultDetails, string.Join(Environment.NewLine, details));
            ResultDetails.Visibility = Visibility.Visible;
        }

        ShowPage(ResultPage);
        PrimaryButton.Content = UiText.Get("Close");
        PrimaryButton.Appearance = ControlAppearance.Primary;
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Visibility = Visibility.Collapsed;
        WindowTitleBar.ShowClose = true;
    }

    /// <summary>
    /// WPF ignores the U+202A…U+202C marks that <see cref="UiText.LeftToRight"/> puts around paths,
    /// so those parts become left-to-right runs instead.
    /// </summary>
    static void SetText(System.Windows.Controls.TextBlock block, string text)
    {
        block.Inlines.Clear();
        var pieces = text.Split('\u202A');
        block.Inlines.Add(new System.Windows.Documents.Run(pieces[0]));
        foreach (var piece in pieces.Skip(1))
        {
            var end = piece.IndexOf('\u202C');
            block.Inlines.Add(new System.Windows.Documents.Run(end < 0 ? piece : piece[..end])
            {
                FlowDirection = FlowDirection.LeftToRight,
            });
            if (end >= 0)
                block.Inlines.Add(new System.Windows.Documents.Run(piece[(end + 1)..]));
        }
    }

#if FM_DEV
    /// <summary>Developer UI tour: the progress and result pages without installing anything.</summary>
    internal void ShowProgressForTour(string text)
    {
        ShowPage(ProgressPage);
        ProgressText.Text = text;
        PrimaryButton.Visibility = Visibility.Collapsed;
        SecondaryButton.IsEnabled = false;
    }

    internal void ShowResultForTour(bool success, string title, string text, IReadOnlyList<string> details) =>
        ShowResult(success, title, text, details);
#endif

    void OnSecondaryClick(object sender, RoutedEventArgs e) => Close();

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
            e.Cancel = true;
    }
}
