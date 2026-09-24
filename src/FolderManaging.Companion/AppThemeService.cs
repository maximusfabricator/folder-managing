using FolderManaging.Core.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FolderManaging.Companion;

/// <summary>Applies light / dark / system theme the Windows 11 way via WPF-UI.</summary>
internal static class AppThemeService
{
    internal static void ApplyFromDocument()
    {
        Apply(CompanionState.Document.Theme);
    }

    internal static void Apply(AppThemePreference preference)
    {
        var theme = preference switch
        {
            AppThemePreference.Light => ApplicationTheme.Light,
            AppThemePreference.Dark => ApplicationTheme.Dark,
            _ => ResolveSystemTheme(),
        };

        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);

        if (System.Windows.Application.Current?.MainWindow is FluentWindow main)
            SystemThemeWatcher.Watch(main, WindowBackdropType.Mica, updateAccents: true);
    }

    static ApplicationTheme ResolveSystemTheme()
    {
        var sys = ApplicationThemeManager.GetSystemTheme();
        return sys switch
        {
            SystemTheme.Light => ApplicationTheme.Light,
            SystemTheme.Dark => ApplicationTheme.Dark,
            SystemTheme.HCWhite or SystemTheme.HCBlack or SystemTheme.HC1 or SystemTheme.HC2 => ApplicationTheme.HighContrast,
            _ => ApplicationTheme.Dark,
        };
    }
}
