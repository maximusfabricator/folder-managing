using System.Windows;
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
        _mode = mode;
        SubtitleText.Text = UiText.Format("VersionLine", ProductInfo.Version);

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
            InstalledVersionText.Text = UiText.Get("Win10Note");
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
            ResultDetails.Text = string.Join(Environment.NewLine, details);
            ResultDetails.Visibility = Visibility.Visible;
        }

        ShowPage(ResultPage);
        PrimaryButton.Content = UiText.Get("Close");
        PrimaryButton.Appearance = ControlAppearance.Primary;
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Visibility = Visibility.Collapsed;
        WindowTitleBar.ShowClose = true;
    }

    void OnSecondaryClick(object sender, RoutedEventArgs e) => Close();

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
            e.Cancel = true;
    }
}
