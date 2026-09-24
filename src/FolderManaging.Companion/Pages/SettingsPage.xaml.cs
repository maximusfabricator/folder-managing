using System.Windows;
using System.Windows.Controls;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;

namespace FolderManaging.Companion.Pages;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    bool _suppressToggleEvents;

    public SettingsPage()
    {
        InitializeComponent();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressToggleEvents = true;
        try
        {
            AutoElevateToggle.IsChecked = CompanionState.Document.AutoElevateProtectedFolders;
            SelectThemeCombo(CompanionState.Document.Theme);
            RefreshWorkerStatus();
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

    static AppThemePreference ParseThemeTag(string? tag) =>
        tag switch
        {
            "Light" => AppThemePreference.Light,
            "Dark" => AppThemePreference.Dark,
            _ => AppThemePreference.System,
        };

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

                System.Windows.MessageBox.Show(
                    Window.GetWindow(this),
                    "Чтобы работать с системными папками без вопроса каждый раз, нужно один раз подтвердить права администратора (UAC).\n\n" +
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
            System.Windows.MessageBox.Show(
                Window.GetWindow(this),
                "Не удалось запустить службу. Подтвердите окно Windows (UAC), если оно появилось.\n" + err,
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
        var answer = System.Windows.MessageBox.Show(
            Window.GetWindow(this),
            "Вернуть все настройки к стандартным?\n\n" +
            "• Контекстное меню — как было изначально\n" +
            "• Тема — как в системе\n" +
            "• Права администратора для системных папок — включены\n\n" +
            "Цвета, метки и покрашенные папки останутся.",
            "Сброс настроек",
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
        }
        finally
        {
            _suppressToggleEvents = false;
        }

        RefreshWorkerStatus();

        System.Windows.MessageBox.Show(
            Window.GetWindow(this),
            "Готово. Новое меню появится при следующем правом клике по папке.",
            "Сброс настроек",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    void RefreshWorkerStatus()
    {
        var running = ElevatedCliInvoker.IsWorkerRunning();
        WorkerStatusText.Text = running
            ? "Статус: служба администратора запущена — системные папки можно менять без повторного UAC."
            : "Статус: служба администратора не запущена. Включите переключатель (или кнопку ниже) и подтвердите UAC один раз.";
        StartWorkerButton.IsEnabled = !running;
    }
}
