// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using FolderManaging.Companion.Localization;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Companion.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    bool _suppressToggleEvents;

    public SettingsPage()
    {
        InitializeComponent();
        UiDirection.Apply(this);
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressToggleEvents = true;
        try
        {
            AutoElevateToggle.IsChecked = CompanionState.Document.AutoElevateProtectedFolders;
            SelectThemeCombo(CompanionState.Document.Theme);
            FillLanguageCombo();
            RefreshWorkerStatus();
            if (AutostartService.IsAvailable)
            {
                AutostartCard.Visibility = Visibility.Visible;
                AutostartToggle.IsChecked = AutostartService.IsEnabled();
            }

            UpdateToggle.IsChecked = CompanionState.Document.CheckForUpdates;
            if (UpdateService.Current is { } updates)
            {
                updates.StatusChanged -= OnUpdateStatusChanged;
                updates.StatusChanged += OnUpdateStatusChanged;
                RefreshUpdateStatus();
            }
            else
            {
                UpdatesCard.Visibility = Visibility.Collapsed;
            }

            ClickTrace.Step($"settings loaded AutoElevate={AutoElevateToggle.IsChecked} theme={CompanionState.Document.Theme}");
        }
        finally
        {
            _suppressToggleEvents = false;
        }
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (UpdateService.Current is { } updates)
            updates.StatusChanged -= OnUpdateStatusChanged;
    }

    void OnUpdateStatusChanged(object? sender, EventArgs e) => RefreshUpdateStatus();

    void RefreshUpdateStatus()
    {
        if (UpdateService.Current is not { } updates)
            return;
        UpdateStatusText.Text = updates.StatusLine();
        CheckUpdatesButton.IsEnabled = !updates.IsChecking;
    }

    void OnUpdateToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents)
            return;

        var enabled = UpdateToggle.IsChecked == true;
        using var scope = ClickTrace.Begin("companion", "settings-check-updates", detail: enabled ? "on" : "off");
        CompanionState.Document.CheckForUpdates = enabled;
        CompanionState.Save();
        UpdateService.Current?.SettingsChanged();
        ClickTrace.Ok();
        scope.MarkFinished();
    }

    async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (UpdateService.Current is { } updates)
            await updates.CheckNowAsync();
    }

    void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToggleEvents || ThemeCombo.SelectedItem is not ComboBoxItem item)
            return;

        var theme = ParseThemeTag(item.Tag?.ToString());
        using var scope = ClickTrace.Begin("companion", "settings-theme", detail: theme.ToString());
        CompanionState.Document.Theme = theme;
        CompanionState.Save();
        AppThemeService.Apply(theme);
        ClickTrace.Ok($"Theme={theme}");
        scope.MarkFinished();
    }

    void SelectThemeCombo(AppThemePreference theme)
    {
        var tag = theme switch
        {
            AppThemePreference.Light => "Light",
            AppThemePreference.Dark => "Dark",
            _ => "System",
        };
        foreach (ComboBoxItem item in ThemeCombo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                ThemeCombo.SelectedItem = item;
                return;
            }
        }

        ThemeCombo.SelectedIndex = 0;
    }

    void FillLanguageCombo()
    {
        LanguageCombo.Items.Clear();
        var matchWindows = new ComboBoxItem
        {
            Tag = "",
            Content = UiText.Format("LanguageSystem", UiText.WindowsLanguage.NativeName),
        };
        LanguageCombo.Items.Add(matchWindows);
        foreach (var language in UiText.Languages)
        {
            LanguageCombo.Items.Add(new ComboBoxItem
            {
                Tag = language.Code,
                Content = language.NativeName,
                Language = XmlLanguage.GetLanguage(language.Code),
            });
        }

        var saved = CompanionState.Document.Language;
        var code = string.IsNullOrWhiteSpace(saved) ? "" : UiText.ResolveLanguage(saved);
        LanguageCombo.SelectedItem = LanguageCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals((string)i.Tag, code, StringComparison.OrdinalIgnoreCase)) ?? matchWindows;
    }

    void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToggleEvents || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string code })
            return;

        var preference = code.Length == 0 ? null : code;
        if (string.Equals(preference, CompanionState.Document.Language, StringComparison.OrdinalIgnoreCase))
            return;

        using var scope = ClickTrace.Begin("companion", "settings-language", detail: preference ?? "windows");
        CompanionState.Document.Language = preference;
        CompanionState.Save();
        ApplySavedLanguage();
        ClickTrace.Ok($"Language={UiText.Current.Code}");
        scope.MarkFinished();
    }

    /// <summary>Switches the app to the saved language; the Explorer menu follows on its next right-click.</summary>
    static void ApplySavedLanguage()
    {
        var before = UiText.Current;
        UiText.Use(CompanionState.Document.Language);
        if (UiText.Current == before)
            return;

        Task.Run(ElevatedCliInvoker.TrySyncLanguage);
        App.ReloadUi(typeof(SettingsPage));
    }

    static AppThemePreference ParseThemeTag(string? tag) =>
        tag switch
        {
            "Light" => AppThemePreference.Light,
            "Dark" => AppThemePreference.Dark,
            _ => AppThemePreference.System,
        };

    void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents)
            return;

        var enabled = AutostartToggle.IsChecked == true;
        using var scope = ClickTrace.Begin("companion", "settings-autostart", detail: enabled ? "on" : "off");
        try
        {
            AutostartService.SetEnabled(enabled);
            ClickTrace.Ok();
            scope.MarkFinished();
        }
        catch (Exception ex)
        {
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            FmMessageBox.Show(
                Window.GetWindow(this),
                UiText.Format("AutostartFail", ex.Message),
                "Folder Managing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    void OnAutoElevateChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents)
            return;

        var enabled = AutoElevateToggle.IsChecked == true;
        using var scope = ClickTrace.Begin("companion", "settings-auto-elevate", detail: enabled ? "on" : "off");
        CompanionState.Document.AutoElevateProtectedFolders = enabled;
        CompanionState.Save();

        if (enabled)
        {
            if (!ElevatedCliInvoker.EnsureWorkerStarted(out var err))
            {
                ClickTrace.Fail(err);
                scope.MarkFinished();
                _suppressToggleEvents = true;
                try
                {
                    CompanionState.Document.AutoElevateProtectedFolders = false;
                    CompanionState.Save();
                    AutoElevateToggle.IsChecked = false;
                }
                finally
                {
                    _suppressToggleEvents = false;
                }

                FmMessageBox.Show(
                    Window.GetWindow(this),
                    UiText.Get("AdminExplain") +
                    (string.IsNullOrWhiteSpace(err) ? "" : err),
                    "Folder Managing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RefreshWorkerStatus();
                return;
            }

            ClickTrace.Ok("AutoElevate on + worker started");
        }
        else
        {
            ElevatedCliInvoker.TryStopWorker();
            ClickTrace.Ok("AutoElevate off + worker stopped");
        }

        scope.MarkFinished();
        RefreshWorkerStatus();
    }

    void OnStartWorkerClick(object sender, RoutedEventArgs e)
    {
        using var scope = ClickTrace.Begin("companion", "settings-start-worker");
        if (!CompanionState.Document.AutoElevateProtectedFolders)
        {
            CompanionState.Document.AutoElevateProtectedFolders = true;
            CompanionState.Save();
            _suppressToggleEvents = true;
            try { AutoElevateToggle.IsChecked = true; }
            finally { _suppressToggleEvents = false; }
        }

        if (ElevatedCliInvoker.EnsureWorkerStarted(out var err))
        {
            ClickTrace.Ok("worker started");
            scope.MarkFinished();
        }
        else
        {
            ClickTrace.Fail(err);
            scope.MarkFinished();
            FmMessageBox.Show(
                Window.GetWindow(this),
                UiText.Format("ServiceStartFail", err),
                "Folder Managing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        RefreshWorkerStatus();
    }

    void OnOpenContextMenuPageClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main)
            main.NavigateTo(typeof(ContextMenuPage));
    }

    /// <summary>Folders listed under the check result; the rest is "…".</summary>
    const int ListedChecks = 12;

    IReadOnlyList<FolderCheck> _checks = [];

    async void OnCheckFoldersClick(object sender, RoutedEventArgs e) => await CheckFoldersAsync();

    async Task CheckFoldersAsync()
    {
        using var scope = ClickTrace.Begin("companion", "settings-check-folders");
        CheckFoldersButton.IsEnabled = false;
        RepairButton.Visibility = Visibility.Collapsed;
        RepairResultText.Visibility = Visibility.Visible;
        RepairResultText.Text = UiText.Get("Checking");
        RepairList.Children.Clear();
        try
        {
            var doc = CompanionState.Document;
            _checks = await Task.Run(() => FolderDiagnostics.CheckAll(doc));
            ShowChecks();
            ClickTrace.Ok($"{_checks.Count(c => !c.IsHealthy)} of {_checks.Count} with issues");
        }
        catch (Exception ex)
        {
            ClickTrace.Fail(ex);
            RepairResultText.Text = ex.Message;
        }
        finally
        {
            scope.MarkFinished();
            CheckFoldersButton.IsEnabled = true;
        }
    }

    void ShowChecks()
    {
        RepairList.Children.Clear();
        var bad = _checks.Where(c => !c.IsHealthy).ToList();
        if (bad.Count == 0)
        {
            RepairResultText.Text = UiText.Format("CheckAllGood", _checks.Count);
            return;
        }

        RepairResultText.Text = UiText.Format("CheckFound", bad.Count, _checks.Count);
        foreach (var check in bad.Take(ListedChecks))
        {
            // Its own left-to-right line: inside right-to-left text a path loses its digits to the wrong end.
            RepairList.Children.Add(ResultLine(check.Path, top: 10, System.Windows.FlowDirection.LeftToRight));
            foreach (var issue in check.Issues)
                RepairList.Children.Add(ResultLine("•  " + UiText.Get("Issue" + issue), top: 2, null));
        }

        if (bad.Count > ListedChecks)
            RepairList.Children.Add(ResultLine("…", top: 6, null));
        RepairButton.Visibility = bad.Any(c => c.Issues.Any(FolderDiagnostics.IsRepairable))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

#if FM_DEV
    /// <summary>UI tour: the result area filled with made-up problems, to see that long texts fit.</summary>
    internal void ShowChecksForTour(IReadOnlyList<FolderCheck> checks)
    {
        _checks = checks;
        RepairResultText.Visibility = Visibility.Visible;
        ShowChecks();
    }
#endif

    TextBlock ResultLine(string text, double top, System.Windows.FlowDirection? direction)
    {
        var line = new TextBlock
        {
            Text = text,
            Style = (Style)FindResource("FmText"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, top, 0, 0),
        };
        if (direction is { } d)
        {
            line.FlowDirection = d;
            line.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        }

        return line;
    }

    async void OnRepairClick(object sender, RoutedEventArgs e)
    {
        var repairable = _checks
            .Where(c => c.Issues.Any(FolderDiagnostics.IsRepairable))
            .Select(c => c with { Issues = c.Issues.Where(FolderDiagnostics.IsRepairable).ToList() })
            .ToList();
        if (repairable.Count == 0)
            return;

        using var scope = ClickTrace.Begin("companion", "settings-repair", detail: $"x{repairable.Count}");
        CheckFoldersButton.IsEnabled = false;
        RepairButton.IsEnabled = false;
        RepairResultText.Text = UiText.Get("Repairing");
        RepairList.Children.Clear();
        FolderBatchResult result;
        try
        {
            result = await FolderWriteGuard.RepairAsync(Window.GetWindow(this), repairable);
            ClickTrace.Ok($"done {result.Done.Count}, failed {result.Failed.Count}");
        }
        finally
        {
            scope.MarkFinished();
            RepairButton.IsEnabled = true;
        }

        FolderWriteGuard.Report(Window.GetWindow(this), result);
        await CheckFoldersAsync();
    }

    void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = UiText.Get("SettingsFileFilter") + " (*.json)|*.json",
            FileName = "FolderManaging-settings.json",
            DefaultExt = ".json",
            AddExtension = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        using var scope = ClickTrace.Begin("companion", "settings-export");
        try
        {
            File.WriteAllText(dialog.FileName, SettingsTransfer.Export(CompanionState.Document, AppInfo.Version));
            ClickTrace.Ok();
            scope.MarkFinished();
            FmMessageBox.Show(
                Window.GetWindow(this),
                UiText.Format("ExportDone", UiText.LeftToRight(dialog.FileName)),
                "Folder Managing",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            FmMessageBox.Show(
                Window.GetWindow(this),
                UiText.Format("ExportFail", ex.Message),
                "Folder Managing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = UiText.Get("SettingsFileFilter") + " (*.json)|*.json",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(owner) != true)
            return;

        SettingsExport import;
        try
        {
            import = SettingsTransfer.Parse(await File.ReadAllTextAsync(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            FmMessageBox.Show(owner, UiText.Format("ImportFail", ex.Message), "Folder Managing",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (FmMessageBox.Show(owner, UiText.Get("ImportConfirm"), UiText.Get("ImportSettings"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        using var scope = ClickTrace.Begin("companion", "settings-import");
        var doc = CompanionState.Document;
        var imported = SettingsTransfer.Apply(doc, import, Directory.Exists);
        CompanionState.Save();
        AppThemeService.ApplyFromDocument();
        IsEnabled = false;
        FolderBatchResult? result = null;
        try
        {
            await Task.Run(FolderIconManager.EnsureMenuIconsForAllPresets);
            if (imported.Folders.Count > 0)
                result = await FolderWriteGuard.RunAsync(owner,
                    imported.Folders.Select(p => new FolderAction(FolderActionKind.Sync, p, "force")).ToList());
            ClickTrace.Ok($"colors {imported.Colors}, tags {imported.Tags}, folders {imported.Folders.Count}, missing {imported.MissingFolders.Count}");
        }
        finally
        {
            scope.MarkFinished();
            IsEnabled = true;
        }

        if (result is not null)
            FolderWriteGuard.Report(owner, result);
        FmMessageBox.Show(owner,
            UiText.Format("ImportDone", imported.Colors, imported.Tags, imported.Folders.Count, imported.MissingFolders.Count),
            UiText.Get("ImportSettings"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        _suppressToggleEvents = true;
        try
        {
            AutoElevateToggle.IsChecked = CompanionState.Document.AutoElevateProtectedFolders;
            UpdateToggle.IsChecked = CompanionState.Document.CheckForUpdates;
            SelectThemeCombo(CompanionState.Document.Theme);
        }
        finally
        {
            _suppressToggleEvents = false;
        }

        UpdateService.Current?.SettingsChanged();
        ApplySavedLanguage();
    }

    void OnResetSettingsClick(object sender, RoutedEventArgs e)
    {
        var answer = FmMessageBox.Show(
            Window.GetWindow(this),
            UiText.Get("ResetConfirmBody"),
            UiText.Get("ResetSettings"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        using var scope = ClickTrace.Begin("companion", "settings-reset");
        CompanionState.Document.ResetSettingsToDefaults();
        CompanionState.Save();
        AppThemeService.ApplyFromDocument();
        ClickTrace.Ok("settings reset to defaults");
        scope.MarkFinished();

        _suppressToggleEvents = true;
        try
        {
            AutoElevateToggle.IsChecked = CompanionState.Document.AutoElevateProtectedFolders;
            UpdateToggle.IsChecked = CompanionState.Document.CheckForUpdates;
            SelectThemeCombo(CompanionState.Document.Theme);
            FillLanguageCombo();
        }
        finally
        {
            _suppressToggleEvents = false;
        }

        UpdateService.Current?.SettingsChanged();
        RefreshWorkerStatus();

        FmMessageBox.Show(
            Window.GetWindow(this),
            UiText.Get("ResetDoneBody"),
            UiText.Get("ResetSettings"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        // After the dialog: rebuilding the window while it is open would close it together with its owner.
        ApplySavedLanguage();
    }

    void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        var exe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "FolderManaging",
            "FolderManaging.Setup.exe");
        if (!File.Exists(exe))
        {
            FmMessageBox.Show(
                Window.GetWindow(this),
                UiText.Get("UninstallMissing"),
                "Folder Managing",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(exe, "--uninstall") { UseShellExecute = true });
    }

    void RefreshWorkerStatus()
    {
        var running = ElevatedCliInvoker.IsWorkerRunning();
        WorkerStatusText.Text = running
            ? UiText.Get("StatusAdminOn")
            : UiText.Get("StatusAdminOff");
        StartWorkerButton.IsEnabled = !running;
    }
}
