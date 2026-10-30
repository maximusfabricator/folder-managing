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
using FolderManaging.Core.Updates;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace FolderManaging.Companion.Diagnostics;

/// <summary>
/// Developer builds only. <c>--ui-tour DIR [--theme light|dark] [--off-screen] [--only-colors]</c> opens every page and
/// dialog, saves a PNG of each into DIR and exits; FM_UI_LANGUAGE picks the language. <c>--only-colors</c> saves just
/// the Folder colors page. Texts that do not fit go to DIR\cut-text.txt.
/// <c>--off-screen</c> keeps the windows off screen and has WPF draw the PNGs, without the system backdrop and without
/// the native message box. The tour only looks: it presses nothing that saves.
/// </summary>
internal static class UiTour
{
    static bool _offScreen;
    static bool _colorsOnly;

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
        if (ValueAfter(args, "--update-rehearsal") is { } rehearsalDir)
        {
            _offScreen = true;
            var host = Application.Current;
            host.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            host.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await RehearseUpdateAsync(rehearsalDir);
                }
                catch (Exception ex)
                {
                    File.AppendAllText(Path.Combine(rehearsalDir, "rehearsal.txt"), "FAILED: " + ex + Environment.NewLine);
                }
                finally
                {
                    host.Shutdown();
                }
            });
            return true;
        }

        var dir = ValueAfter(args, "--ui-tour");
        if (dir is null)
            return false;

        _offScreen = args.Contains("--off-screen", StringComparer.OrdinalIgnoreCase);
        _colorsOnly = args.Contains("--only-colors", StringComparer.OrdinalIgnoreCase);
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
        var updates = UpdateService.ForTour();
        UpdateService.BeforeShowForTour = KeepOffScreen;

        var main = new MainWindow { Topmost = true, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        KeepOffScreen(main);
        main.Show();
        await Settle(main, 1500);

        if (_colorsOnly)
        {
            await SaveColors(main, prefix);
            main.Close();
            return;
        }

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

        var sample = SampleManifest();
        var update = updates.OpenWindowForTour();
        await Settle(update, 900);
        update.ShowAvailable(sample);
        await Settle(update, 700);
        Save(update, prefix + "update-available");
        update.ShowDownloadingForTour(sample, sample.SetupSize * 2 / 5);
        await Settle(update, 600);
        Save(update, prefix + "update-downloading");
        update.ShowStartingForTour(sample);
        await Settle(update, 600);
        Save(update, prefix + "update-starting");
        update.ShowError(UiText.Get("UpdateCheckFailedTitle"), UiText.Get("UpdateErrorNetwork"),
            "github.com did not answer in time.", () => Task.CompletedTask);
        await Settle(update, 600);
        Save(update, prefix + "update-error");
        update.ShowError(UiText.Get("UpdateFailedTitle"), UiText.Get("UpdateCancelled"), null, () => Task.CompletedTask);
        await Settle(update, 600);
        Save(update, prefix + "update-cancelled");
        update.ShowError(UiText.Get("UpdateFailedTitle"), UiText.Get("UpdateErrorDownload"), "The downloaded setup does not match update.json (size or SHA-256).", () => Task.CompletedTask);
        await Settle(update, 600);
        Save(update, prefix + "update-bad-download");
        update.ShowChecking();
        await Settle(update, 600);
        Save(update, prefix + "update-checking");
        update.ShowUpToDate();
        await Settle(update, 600);
        Save(update, prefix + "update-uptodate");
        update.Close();

        main.Close();
    }

    /// <summary>A release as the window would get it; the notes are long enough to scroll.</summary>
    static UpdateManifest SampleManifest()
    {
        var notes = Environment.GetEnvironmentVariable("FM_TOUR_NOTES") is { Length: > 0 } file && File.Exists(file)
            ? File.ReadAllText(file)
            : "## 2.2.0\n\n### New\n\n- **Update notifications.** The app tells you when a new version is out.\n- Settings → Check for updates automatically.";
        return new UpdateManifest(new UpdateVersion(2, 2, 0), "v2.2.0", "FolderManaging-Setup-2.2.0.exe", 136_000_000,
            new string('0', 64), UpdateEndpoint.ReleasesPageUrl + "/tag/v2.2.0", DateTimeOffset.UtcNow, notes);
    }

    /// <summary>
    /// <c>--update-rehearsal DIR</c>: the real in-app path against the server in <c>FM_UPDATE_BASE_URL</c> — check,
    /// signature, window, download, size and SHA-256 — stopping before the setup would start (needs
    /// <c>FM_UPDATE_DRY_RUN=1</c>). Windows stay off screen; DIR gets PNGs and rehearsal.txt.
    /// </summary>
    static async Task RehearseUpdateAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var report = Path.Combine(dir, "rehearsal.txt");
        void Log(string line) => File.AppendAllText(report, line + Environment.NewLine);
        if (Environment.GetEnvironmentVariable("FM_UPDATE_DRY_RUN") != "1")
        {
            Log("FAILED: set FM_UPDATE_DRY_RUN=1");
            return;
        }

        var service = UpdateService.ForTour();
        UpdateService.BeforeShowForTour = KeepOffScreen;
        Log($"current (pretended): {UpdateVersion.Current}; source: {UpdateEndpoint.Default.ManifestUri}");
        var manifest = await service.CheckForRehearsalAsync();
        if (manifest is null)
        {
            Log("FAILED: no newer version offered");
            return;
        }

        Log($"signature: valid; offered: {manifest.Version}, {manifest.SetupFile}, {manifest.SetupSize} bytes, sha256 {manifest.SetupSha256}");
        var window = Application.Current.Windows.OfType<UpdateWindow>().Single();
        await Settle(window, 900);
        Save(window, Path.Combine(dir, $"{UiText.Current.Code}-rehearsal-available"));

        var install = window.PressUpdateForRehearsal();
        await Task.Delay(150);
        Save(window, Path.Combine(dir, $"{UiText.Current.Code}-rehearsal-downloading"));
        await install;
        await Settle(window, 600);
        Save(window, Path.Combine(dir, $"{UiText.Current.Code}-rehearsal-dry-run"));

        var path = UpdateService.DryRunSetup;
        if (path is null || !File.Exists(path))
        {
            Log($"FAILED: no verified setup (window state {window.State})");
            return;
        }

        await using (var stream = File.OpenRead(path))
        {
            var hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(stream));
            Log($"downloaded: {path}, {stream.Length} bytes, sha256 {hash}");
            Log(hash == manifest.SetupSha256 && stream.Length == manifest.SetupSize
                ? "RESULT: OK — verified setup ready; the installer was not started (dry run)"
                : "FAILED: downloaded file differs from the manifest");
        }

        window.Close();
    }

    static async Task SaveColors(MainWindow main, string prefix)
    {
        main.NavigateTo(typeof(Pages.ColorsPage));
        await Settle(main, 900);
        Save(main, prefix + "colors");
        if (ScrollToEnd(main))
        {
            await Settle(main, 500);
            Save(main, prefix + "colors-end");
        }

        main.Width = main.MinWidth;
        main.Height = main.MinHeight;
        main.NavigateTo(typeof(Pages.ColorsPage));
        await Settle(main, 900);
        Save(main, prefix + "colors-small");
        if (ScrollToEnd(main))
        {
            await Settle(main, 400);
            Save(main, prefix + "colors-small-end");
        }
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
