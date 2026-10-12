using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using FolderManaging.Core;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Diagnostics;
using Wpf.Ui.Controls;

namespace FolderManaging.Companion;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
#if !FM_DEV
        LogsNavItem.Content = UiText.Get("NavErrorLog");
#endif
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppThemeService.ApplyFromDocument();
        RootNav.Navigate(typeof(Pages.ColorsPage));
        ClickTrace.Step("Companion MainWindow loaded");
        TraceLog.EnsureLogLocationsHint();
    }

    internal void NavigateTo(Type pageType) => RootNav.Navigate(pageType);

    void OnQuickColorClick(object sender, RoutedEventArgs e)
    {
        if (App.Tray is not null)
            App.Tray.OpenQuickColor();
        else
            new QuickColorWindow().Show();
    }

    void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        using var scope = ClickTrace.Begin("companion", "open-logs");
        try
        {
            TraceLog.EnsureLogLocationsHint();
            var dir = TraceLog.UserVisibleLogDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
            ClickTrace.Ok($"opened '{dir}'");
            scope.MarkFinished();
        }
        catch (Exception ex)
        {
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            System.Windows.MessageBox.Show(
                this,
                UiText.Format("OpenLogsFail", ex.Message, TraceLog.UserVisibleLogDirectory),
                "Folder Managing",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    void OnResetFolderIconClick(object sender, RoutedEventArgs e) => RestoreDefaultFolderIcon();

    internal void RestoreDefaultFolderIcon()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = UiText.Get("PickResetFolder"),
            UseDescriptionForTitle = true,
        };

        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK ||
            string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            using (ClickTrace.Begin("companion", "reset-icon", path: "(cancelled)")) { }
            return;
        }

        using var scope = ClickTrace.Begin("companion", "reset-icon", path: dlg.SelectedPath);
        try
        {
            FolderIconManager.RemoveCustomization(dlg.SelectedPath);
            ClickTrace.Ok($"reset path='{dlg.SelectedPath}'");
            scope.MarkFinished();
            System.Windows.MessageBox.Show(
                this,
                UiText.Get("IconResetOk"),
                "Folder Managing",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (UnauthorizedAccessException ex)
        {
            ClickTrace.Step($"reset access denied: {ex.Message}");
            if (!TryElevatedReset(dlg.SelectedPath))
            {
                ClickTrace.Fail(ex);
                scope.MarkFinished();
                System.Windows.MessageBox.Show(
                    this,
                    UiText.Format("IconResetFail", dlg.SelectedPath, ex.Message),
                    "Folder Managing",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            ClickTrace.Ok($"reset via elevated CLI path='{dlg.SelectedPath}'");
            scope.MarkFinished();
            System.Windows.MessageBox.Show(
                this,
                UiText.Get("IconResetOk"),
                "Folder Managing",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "Folder Managing",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    bool TryElevatedReset(string path)
    {
        var auto = CompanionState.Document.AutoElevateProtectedFolders;
        if (!auto)
        {
            var answer = System.Windows.MessageBox.Show(
                this,
                UiText.Format("ResetUac", path),
                "Folder Managing",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (answer != System.Windows.MessageBoxResult.Yes)
                return false;
        }
        else
        {
            ClickTrace.Step("companion reset: auto-elevate");
        }

        return ElevatedCliInvoker.TryInvoke("reset", path, value: null, out _);
    }
}
