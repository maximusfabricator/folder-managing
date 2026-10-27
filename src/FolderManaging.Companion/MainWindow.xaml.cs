// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using FolderManaging.Companion.Localization;
using FolderManaging.Core;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Diagnostics;
using Wpf.Ui.Controls;

namespace FolderManaging.Companion;

public partial class MainWindow : FluentWindow
{
    readonly Type _startPage;

    public MainWindow(Type? startPage = null)
    {
        InitializeComponent();
        UiDirection.Apply(this);
        _startPage = startPage ?? typeof(Pages.ColorsPage);
#if !FM_DEV
        LogsNavItem.Content = UiText.Get("NavErrorLog");
#endif
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppThemeService.ApplyFromDocument();
        RootNav.Navigate(_startPage);
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
            FmMessageBox.Show(
                this,
                UiText.Format("OpenLogsFail", ex.Message, UiText.LeftToRight(TraceLog.UserVisibleLogDirectory)),
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
            FolderIconManager.RemoveCustomizationIfOurs(dlg.SelectedPath);
            ForgetFolder(dlg.SelectedPath);
            ClickTrace.Ok($"reset path='{dlg.SelectedPath}'");
            scope.MarkFinished();
            FmMessageBox.Show(
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
                FmMessageBox.Show(
                    this,
                    UiText.Format("IconResetFail", UiText.LeftToRight(dlg.SelectedPath), ex.Message),
                    "Folder Managing",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            CompanionState.ReloadFromDiskIfStale();
            ClickTrace.Ok($"reset via elevated CLI path='{dlg.SelectedPath}'");
            scope.MarkFinished();
            FmMessageBox.Show(
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
            FmMessageBox.Show(
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
            var answer = FmMessageBox.Show(
                this,
                UiText.Format("ResetUac", UiText.LeftToRight(path)),
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

    /// <summary>Drops the folder's color and tags from state (a Quick access pin stays).</summary>
    static void ForgetFolder(string folderPath)
    {
        var doc = CompanionState.Document;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        var entry = doc.Folders.FirstOrDefault(f =>
            string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(f.Path)),
                full,
                StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return;
        entry.ColorToken = null;
        entry.Tags.Clear();
        if (!entry.IsPinned)
            doc.Folders.Remove(entry);
        CompanionState.Save();
    }
}
