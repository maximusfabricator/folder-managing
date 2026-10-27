// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

#if FM_DEV
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FolderManaging.Companion.Diagnostics;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;

namespace FolderManaging.Setup;

/// <summary>
/// Developer builds only. <c>--ui-tour DIR [--off-screen]</c> shows every installer page, saves a PNG of each into DIR
/// and exits without installing anything; FM_UI_LANGUAGE picks the language. Texts that do not fit go to
/// DIR\cut-text.txt. <c>--off-screen</c> keeps the window off screen and has WPF draw the PNGs.
/// </summary>
internal static class SetupUiTour
{
    static bool _offScreen;

    internal static bool TryStart(Application app, string[] args)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, "--ui-tour", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length)
            return false;

        var dir = args[i + 1];
        _offScreen = args.Contains("--off-screen", StringComparer.OrdinalIgnoreCase);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                Directory.CreateDirectory(dir);
                var prefix = Path.Combine(dir, $"{UiText.Current.Code}-setup-");

                var install = await OpenAsync(SetupMode.Install);
                Save(install, prefix + "install");
                install.ShowProgressForTour(UiText.Get("PrepareDotnet"));
                await SettleAsync(install, 600);
                Save(install, prefix + "progress");
                install.ShowResultForTour(true, UiText.Get("Done"), UiText.Get("InstalledBody"),
                    [UiText.Format("AdminLeftColumn", "Access is denied.")]);
                await SettleAsync(install, 600);
                Save(install, prefix + "done");
                install.ShowResultForTour(false, UiText.Get("NotFinished"), UiText.Get("NeedAdminInstall"), []);
                await SettleAsync(install, 600);
                Save(install, prefix + "failed");
                install.Close();

                var uninstall = await OpenAsync(SetupMode.Uninstall);
                Save(uninstall, prefix + "uninstall");
                uninstall.ShowResultForTour(true, UiText.Get("Removed"), UiText.Get("RemovedBody"),
                    [UiText.Format("FolderNotReset", UiText.LeftToRight(@"C:\Program Files (x86)\Contoso Software"))]);
                await SettleAsync(uninstall, 600);
                Save(uninstall, prefix + "removed");
                uninstall.Close();
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(dir, "setup-tour-error.txt"), ex.ToString());
            }
            finally
            {
                app.Shutdown();
            }
        });
        return true;
    }

    static async Task<SetupWindow> OpenAsync(SetupMode mode)
    {
        var window = new SetupWindow(mode) { Topmost = true, ShowActivated = false };
        if (_offScreen)
        {
            window.Topmost = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
        }
        window.Show();
        await SettleAsync(window, 1200);
        return window;
    }

    static async Task SettleAsync(Window window, int milliseconds)
    {
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(milliseconds);
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
    }

    static void Save(Window window, string path)
    {
        if (_offScreen)
            OffScreenShot.Save(window, path + ".png");
        else
            ScreenCapture.SaveWindow(new WindowInteropHelper(window).Handle, path + ".png");
        CutText.Report(window, path);
    }
}
#endif
