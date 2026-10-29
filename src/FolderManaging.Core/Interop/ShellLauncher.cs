// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using System.Runtime.InteropServices;
using FolderManaging.Core.Diagnostics;

namespace FolderManaging.Core.Interop;

/// <summary>
/// Starts a program through the running Explorer (the desktop's <c>IShellDispatch2.ShellExecute</c>), so it runs
/// as a normal app of the signed-in user: not elevated even when the caller is, outside the caller's package, and
/// with the user's own environment.
/// </summary>
public static class ShellLauncher
{
    /// <summary>Through Explorer; when there is no Explorer shell, a normal start.</summary>
    public static bool Start(string file, string arguments)
    {
        if (TryStartThroughExplorer(file, arguments, out var error))
            return true;
        TraceLog.Append("shell-menu.log", $"[LAUNCH] through Explorer failed ({error}), starting directly: {file}");
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[LAUNCH] start failed '{file}': {ex.Message}");
            return false;
        }
    }

    public static bool TryStartThroughExplorer(string file, string arguments, out string error)
    {
        string? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                ExecuteInExplorer(file, arguments);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
        })
        {
            IsBackground = true,
            Name = "Shell launch",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20)))
            failure = "Explorer did not answer.";
        error = failure ?? "";
        return failure is null;
    }

    static void ExecuteInExplorer(string file, string arguments)
    {
        var type = Type.GetTypeFromCLSID(ShellWindowsClsid, throwOnError: true)!;
        var windows = (IShellWindows)Activator.CreateInstance(type)!;
        object? view = null;
        try
        {
            object location = CsidlDesktop;
            object root = null!;
            var desktop = windows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch)
                          ?? throw new InvalidOperationException("No desktop window.");
            var provider = (IServiceProvider)desktop;
            var sid = SidTopLevelBrowser;
            var iid = typeof(IShellBrowser).GUID;
            Marshal.ThrowExceptionForHR(provider.QueryService(ref sid, ref iid, out var browserPtr));
            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(browserPtr);
            Marshal.Release(browserPtr);
            Marshal.ThrowExceptionForHR(browser.QueryActiveShellView(out var shellView));
            var dispatch = IidDispatch;
            Marshal.ThrowExceptionForHR(shellView.GetItemObject(SvgioBackground, ref dispatch, out var viewPtr));
            view = Marshal.GetObjectForIUnknown(viewPtr);
            Marshal.Release(viewPtr);

            dynamic folderView = view;
            dynamic shell = folderView.Application;
            shell.ShellExecute(file, arguments, Path.GetDirectoryName(file) ?? "", "open", 1);
            Marshal.FinalReleaseComObject(shell);
            Marshal.ReleaseComObject(browser);
            Marshal.ReleaseComObject(shellView);
        }
        finally
        {
            if (view is not null)
                Marshal.FinalReleaseComObject(view);
            Marshal.FinalReleaseComObject(windows);
        }
    }

    const int CsidlDesktop = 0;
    const int SwcDesktop = 8;
    const int SwfoNeedDispatch = 1;
    const uint SvgioBackground = 0;
    static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    static readonly Guid IidDispatch = new("00020400-0000-0000-C000-000000000046");

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    interface IShellWindows
    {
        int Count { get; }
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? FindWindowSW(
            [MarshalAs(UnmanagedType.Struct)] ref object pvarLoc,
            [MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot,
            int swClass,
            out int phwnd,
            int swfwOptions);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();

        [PreserveSig]
        int QueryActiveShellView(out IShellView ppshv);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellView
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
        void CreateViewWindow();
        void DestroyViewWindow();
        void GetCurrentInfo();
        void AddPropertySheetPages();
        void SaveViewState();
        void SelectItem();

        [PreserveSig]
        int GetItemObject(uint uItem, ref Guid riid, out IntPtr ppv);
    }
}
