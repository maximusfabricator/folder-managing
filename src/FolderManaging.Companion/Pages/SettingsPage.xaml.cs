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

            ClickTrace.Step($"settings loaded AutoElevate={AutoElevateToggle.IsChecked} theme={CompanionState.Document.Theme}");
        }
        finally
        {
            _suppressToggleEvents = false;
        }
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
            SelectThemeCombo(CompanionState.Document.Theme);
            FillLanguageCombo();
        }
        finally
        {
            _suppressToggleEvents = false;
        }

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
