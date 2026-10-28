// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

#if FM_DEV
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Interop;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace FolderManaging.Companion.Diagnostics;

/// <summary>
/// Developer builds only. <c>--ui-tour DIR [--theme light|dark] [--off-screen]</c> opens every page and dialog, saves
/// a PNG of each into DIR and exits; FM_UI_LANGUAGE picks the language. Texts that do not fit go to DIR\cut-text.txt.
/// <c>--off-screen</c> keeps the windows off screen and has WPF draw the PNGs, without the system backdrop and without
/// the native message box. The tour only looks: it presses nothing that saves.
/// </summary>
internal static class UiTour
{
    static bool _offScreen;

    static readonly (string Name, Type Page)[] Pages =
    [
        ("colors", typeof(Pages.ColorsPage)),
        ("tags", typeof(Pages.TagsPage)),
        ("folders", typeof(Pages.ManageFoldersPage)),
        ("menu", typeof(Pages.ContextMenuPage)),
        ("settings", typeof(Pages.SettingsPage)),
        ("about", typeof(Pages.AboutPage)),
    ];

    internal static bool TryStart(string[] args)
    {
        var dir = ValueAfter(args, "--ui-tour");
        if (dir is null)
            return false;

        _offScreen = args.Contains("--off-screen", StringComparer.OrdinalIgnoreCase);
        AppThemeService.TourTheme = ValueAfter(args, "--theme")?.ToLowerInvariant() switch
        {
            "dark" => AppThemePreference.Dark,
            "light" => AppThemePreference.Light,
            _ => null,
        };
        AppThemeService.ApplyFromDocument();

        var app = Application.Current;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await RunAsync(dir);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(dir, "tour-error.txt"), ex.ToString());
            }
            finally
            {
                app.Shutdown();
            }
        });
        return true;
    }

    static async Task RunAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var theme = AppThemeService.TourTheme?.ToString().ToLowerInvariant() ?? "system";
        var prefix = Path.Combine(dir, $"{UiText.Current.Code}-{theme}-");

        var main = new MainWindow { Topmost = true, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        KeepOffScreen(main);
        main.Show();
        await Settle(main, 1500);

        foreach (var (name, page) in Pages)
        {
            main.NavigateTo(page);
            await Settle(main, 900);
            Save(main, prefix + name);
            if (ScrollToEnd(main))
            {
                await Settle(main, 500);
                Save(main, prefix + name + "-end");
            }
        }

        main.NavigateTo(typeof(Pages.SettingsPage));
        await Settle(main, 900);
        if (Descendants(main).OfType<Pages.SettingsPage>().FirstOrDefault() is { } settings)
        {
            settings.ShowChecksForTour(Enum.GetValues<FolderIssue>()
                .Select((issue, i) => new FolderCheck($@"C:\Users\Public\Documents\Project folder {i + 1}", [issue]))
                .Append(new FolderCheck(@"D:\Photos\2026", [FolderIssue.IconMissing, FolderIssue.StalePin]))
                .ToList());
            await Settle(main, 600);
            if (ScrollToEnd(main))
                await Settle(main, 500);
            Save(main, prefix + "settings-repair");
        }

        main.NavigateTo(typeof(Pages.ColorsPage));
        await Settle(main, 900);
        if (Press(main, UiText.Get("Edit")))
        {
            await Settle(main, 900);
            Save(main, prefix + "colors-editor");
        }

        main.Width = main.MinWidth;
        main.Height = main.MinHeight;
        foreach (var (name, page) in Pages)
        {
            main.NavigateTo(page);
            await Settle(main, 900);
            Save(main, prefix + name + "-small");
        }

        CaptureModal(prefix + "dialog-question", () => FmMessageBox.Show(
            main, UiText.Get("ResetConfirmBody"), UiText.Get("ResetSettings"), MessageBoxButton.YesNo, MessageBoxImage.Question));
        CaptureModal(prefix + "dialog-warning", () => FmMessageBox.Show(
            main,
            UiText.Format("UacProtected", UiText.LeftToRight(@"C:\Program Files (x86)\Contoso Software\A rather long folder name (2)")),
            "Folder Managing",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning));
        string[] samplePaths = [@"C:\Program Files\Contoso Software", @"C:\Program Files (x86)\Fabrikam\Data", @"C:\Windows\Web\Wallpaper"];
        CaptureModal(prefix + "dialog-uac-many", () => FmMessageBox.Show(
            main,
            UiText.Format("UacProtectedMany", samplePaths.Length, string.Join("\n", samplePaths.Select(UiText.LeftToRight))),
            "Folder Managing",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning));
        CaptureModal(prefix + "dialog-reset-all", () => FmMessageBox.Show(
            main, UiText.Format("ResetAllConfirm", 12), "Folder Managing", MessageBoxButton.YesNo, MessageBoxImage.Question));
        CaptureModal(prefix + "dialog-import", () => FmMessageBox.Show(
            main, UiText.Get("ImportConfirm"), UiText.Get("ImportSettings"), MessageBoxButton.YesNo, MessageBoxImage.Question));
        CaptureModal(prefix + "dialog-import-done", () => FmMessageBox.Show(
            main, UiText.Format("ImportDone", 6, 4, 10, 2), UiText.Get("ImportSettings"), MessageBoxButton.OK, MessageBoxImage.Information));
        if (!_offScreen)
        {
            CaptureModal(prefix + "native-tip", () => NativeMessageBox.Show(
                UiText.Get("ColorSortTip"),
                "Folder Managing",
                NativeMessageBox.Ok | NativeMessageBox.IconInformation | NativeMessageBox.SetForeground | NativeMessageBox.Topmost));
        }

        var quick = new QuickColorWindow { Topmost = true, ShowActivated = false };
        KeepOffScreen(quick);
        quick.Show();
        await Settle(quick, 1200);
        Save(quick, prefix + "quick-color");
        quick.Close();

        var quickMany = new QuickColorWindow(samplePaths) { Topmost = true, ShowActivated = false };
        KeepOffScreen(quickMany);
        quickMany.Show();
        await Settle(quickMany, 1200);
        Save(quickMany, prefix + "quick-color-many");
        quickMany.Close();

        var license = Path.Combine(AppContext.BaseDirectory, AppInfo.LicenseFileName);
        if (File.Exists(license))
        {
            var viewer = new TextViewerWindow(UiText.Get("AboutLicense"), File.ReadAllText(license)) { Owner = main, ShowActivated = false };
            KeepOffScreen(viewer);
            viewer.Show();
            await Settle(viewer, 900);
            Save(viewer, prefix + "license");
            viewer.Close();
        }

        main.Close();
    }

    static async Task Settle(Window window, int milliseconds)
    {
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(milliseconds);
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Layout is the same off screen, and the person at the PC keeps a free screen.</summary>
    static void KeepOffScreen(Window window)
    {
        if (!_offScreen)
            return;
        window.Topmost = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
    }

    static void Save(Window window, string path)
    {
        if (_offScreen)
            OffScreenShot.Save(window, path + ".png");
        else
            ScreenCapture.SaveWindow(new WindowInteropHelper(window).Handle, path + ".png");
        CutText.Report(window, path);
    }

    /// <summary>
    /// Shows a modal dialog, saves it once it is drawn, then closes it as if the user pressed Esc.
    /// The dialog is looked up among this thread's own windows: the foreground window can belong to another app.
    /// </summary>
    static void CaptureModal(string path, Action show)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        var attempts = 0;
        timer.Tick += (_, _) =>
        {
            var dialog = OwnDialog();
            if (dialog == IntPtr.Zero)
            {
                if (++attempts >= 10)
                    timer.Stop();
                return;
            }
            timer.Stop();
            var box = HwndSource.FromHwnd(dialog)?.RootVisual as Window;
            if (!_offScreen)
                ScreenCapture.SaveWindow(dialog, path + ".png");
            else if (box is not null)
                OffScreenShot.Save(box, path + ".png");
            if (box is not null)
                CutText.Report(box, path);
            PostMessage(dialog, WmClose, IntPtr.Zero, IntPtr.Zero);
        };
        timer.Start();
        show();
    }

    static IntPtr OwnDialog()
    {
        var box = Application.Current.Windows.OfType<FmMessageBox>().LastOrDefault(w => w.IsVisible);
        if (box is not null)
            return new WindowInteropHelper(box).Handle;

        var found = IntPtr.Zero;
        var className = new StringBuilder(16);
        EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetClassName(hwnd, className, className.Capacity) == 0 || className.ToString() != "#32770")
                return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    static bool ScrollToEnd(DependencyObject root)
    {
        var viewer = Descendants(root).OfType<ScrollViewer>()
            .Where(v => v.IsVisible && v.ScrollableHeight > 1)
            .OrderByDescending(v => v.ViewportHeight)
            .FirstOrDefault();
        if (viewer is null)
            return false;
        viewer.ScrollToEnd();
        return true;
    }

    static bool Press(DependencyObject root, string content)
    {
        var button = Descendants(root).OfType<Button>().FirstOrDefault(b => b.IsVisible && Equals(b.Content, content));
        if (button is null)
            return false;
        (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
        return true;
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    static string? ValueAfter(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    const uint WmClose = 0x0010;

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
}
#endif
