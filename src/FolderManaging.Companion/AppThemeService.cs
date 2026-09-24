using System.Windows.Threading;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfApplication = System.Windows.Application;

namespace FolderManaging.Companion;

/// <summary>Applies light / dark / system theme and the Windows accent palette via WPF-UI.</summary>
internal static class AppThemeService
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    static readonly string[] OverrideKeys =
    {
        "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
        "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed",
        "FmCautionBackground", "FmCautionBackgroundPointerOver", "FmCautionBackgroundPressed",
        "FmCautionForeground", "FmCautionForegroundPressed",
    };

    static bool _systemHooked;
    static DispatcherTimer? _systemChangeDebounce;

    internal static void ApplyFromDocument() => Apply(CompanionState.Document.Theme);

    internal static void Apply(AppThemePreference preference)
    {
        EnsureSystemHook();

        var theme = preference switch
        {
            AppThemePreference.Light => ApplicationTheme.Light,
            AppThemePreference.Dark => ApplicationTheme.Dark,
            _ => ReadSystemAppTheme(),
        };
        var palette = ReadAccentPalette();

        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
        if (theme == ApplicationTheme.Dark)
            ApplicationAccentColorManager.Apply(palette.Base, palette.Light1, palette.Light2, palette.Light3);
        else
            ApplicationAccentColorManager.Apply(palette.Base, palette.Dark1, palette.Dark2, palette.Dark3);

        ApplyButtonColors(theme, palette);

        foreach (System.Windows.Window window in WpfApplication.Current?.Windows ?? default!)
            WindowBackgroundManager.UpdateBackground(window, theme, WindowBackdropType.Mica);

        ClickTrace.Step($"theme applied pref={preference} resolved={theme} accent={palette.Base}");
    }

    internal static void Shutdown()
    {
        if (!_systemHooked)
            return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _systemHooked = false;
    }

    static void EnsureSystemHook()
    {
        if (_systemHooked || WpfApplication.Current is null)
            return;
        _systemHooked = true;

        // Windows sends several setting-change broadcasts per theme/accent switch; re-apply once they settle.
        _systemChangeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _systemChangeDebounce.Tick += (_, _) =>
        {
            _systemChangeDebounce.Stop();
            ApplyFromDocument();
        };
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
            return;
        WpfApplication.Current?.Dispatcher.BeginInvoke(() =>
        {
            _systemChangeDebounce?.Stop();
            _systemChangeDebounce?.Start();
        });
    }

    /// <summary>
    /// Primary: dark UI gets the strong accent, light UI the soft one (inverse of Fluent defaults).
    /// Caution (delete / reset) takes the opposite shade so it never matches Primary.
    /// </summary>
    static void ApplyButtonColors(ApplicationTheme theme, AccentPalette palette)
    {
        var res = WpfApplication.Current?.Resources;
        if (res is null)
            return;

        if (theme == ApplicationTheme.HighContrast)
        {
            foreach (var key in OverrideKeys)
                res.Remove(key);
            return;
        }

        var isDark = theme == ApplicationTheme.Dark;
        var primary = isDark ? palette.Dark1 : palette.Light2;
        var caution = isDark ? palette.Light2 : palette.Dark2;

        SetButtonBrushes(res, "AccentButton", primary);
        SetButtonBrushes(res, "FmCaution", caution);
    }

    static void SetButtonBrushes(System.Windows.ResourceDictionary res, string prefix, MediaColor fill)
    {
        var fg = ReadableTextOn(fill);
        res[prefix + "Background"] = new SolidColorBrush(fill);
        res[prefix + "BackgroundPointerOver"] = new SolidColorBrush(WithOpacity(fill, 0.90));
        res[prefix + "BackgroundPressed"] = new SolidColorBrush(WithOpacity(fill, 0.80));
        res[prefix + "Foreground"] = new SolidColorBrush(fg);
        res[prefix + "ForegroundPressed"] = new SolidColorBrush(WithOpacity(fg, 0.80));
        if (prefix == "AccentButton")
            res[prefix + "ForegroundPointerOver"] = new SolidColorBrush(fg);
    }

    static ApplicationTheme ReadSystemAppTheme()
    {
        if (System.Windows.SystemParameters.HighContrast)
            return ApplicationTheme.HighContrast;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue("AppsUseLightTheme") is int light)
                return light == 0 ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"theme: AppsUseLightTheme unreadable: {ex.Message}");
        }

        return ApplicationTheme.Light;
    }

    readonly record struct AccentPalette(
        MediaColor Light3, MediaColor Light2, MediaColor Light1, MediaColor Base,
        MediaColor Dark1, MediaColor Dark2, MediaColor Dark3);

    /// <summary>The same 7 shades Windows itself uses (Settings toggles, Start, taskbar).</summary>
    static AccentPalette ReadAccentPalette()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentKey);
            if (key?.GetValue("AccentPalette") is byte[] b && b.Length >= 28)
            {
                MediaColor At(int i) => MediaColor.FromRgb(b[i * 4], b[i * 4 + 1], b[i * 4 + 2]);
                return new AccentPalette(At(0), At(1), At(2), At(3), At(4), At(5), At(6));
            }
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"theme: AccentPalette unreadable: {ex.Message}");
        }

        var c = ApplicationAccentColorManager.GetColorizationColor();
        return new AccentPalette(
            Mix(c, 255, 0.60), Mix(c, 255, 0.40), Mix(c, 255, 0.20), c,
            Mix(c, 0, 0.20), Mix(c, 0, 0.40), Mix(c, 0, 0.60));
    }

    static MediaColor Mix(MediaColor c, byte target, double amount) =>
        MediaColor.FromRgb(
            (byte)Math.Round(c.R + (target - c.R) * amount),
            (byte)Math.Round(c.G + (target - c.G) * amount),
            (byte)Math.Round(c.B + (target - c.B) * amount));

    static MediaColor ReadableTextOn(MediaColor fill)
    {
        var l = RelativeLuminance(fill);
        var contrastWithBlack = (l + 0.05) / 0.05;
        var contrastWithWhite = 1.05 / (l + 0.05);
        return contrastWithBlack >= contrastWithWhite
            ? MediaColor.FromRgb(0, 0, 0)
            : MediaColor.FromRgb(255, 255, 255);
    }

    static double RelativeLuminance(MediaColor c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    static MediaColor WithOpacity(MediaColor c, double opacity) =>
        MediaColor.FromArgb((byte)Math.Clamp((int)Math.Round(c.A * opacity), 0, 255), c.R, c.G, c.B);
}
