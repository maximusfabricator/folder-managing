// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;
using System.Windows.Interop;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Interop;

namespace FolderManaging.Companion;

/// <summary>
/// Listens to Explorer's "folder renamed or moved" notices while the tray app runs, so colors and tags follow the
/// folder. Changes made outside Explorer (a script, another file manager) are caught at the next start instead.
/// </summary>
internal sealed class FolderRenameWatcher : IDisposable
{
    const int WmApp = 0x8000;
    const uint WmShellNotify = WmApp + 0x31;

    readonly HwndSource? _window;
    readonly uint _registration;

    internal FolderRenameWatcher()
    {
        try
        {
            _window = new HwndSource(new HwndSourceParameters("FolderManaging.RenameWatcher")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE: no visible window
            });
            _window.AddHook(WndProc);

            if (SHGetFolderLocation(0, 0 /* CSIDL_DESKTOP */, 0, 0, out var desktop) != 0)
                return;
            try
            {
                var entry = new Win32Shell.SHChangeNotifyEntry { pidl = desktop, fRecursive = true };
                _registration = Win32Shell.SHChangeNotifyRegister(
                    _window.Handle,
                    Win32Shell.SHCNRF_ShellLevel | Win32Shell.SHCNRF_InterruptLevel | Win32Shell.SHCNRF_NewDelivery,
                    Win32Shell.SHCNE_RENAMEFOLDER,
                    WmShellNotify,
                    1,
                    ref entry);
            }
            finally
            {
                Marshal.FreeCoTaskMem(desktop);
            }
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[RENAME] watcher not started: {ex.Message}");
        }
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg != WmShellNotify)
            return 0;
        handled = true;
        var lockHandle = Win32Shell.SHChangeNotification_Lock(wParam, (uint)lParam, out var pidls, out var eventId);
        if (lockHandle == 0)
            return 0;
        string? from = null;
        string? to = null;
        try
        {
            if (((uint)eventId & Win32Shell.SHCNE_RENAMEFOLDER) != 0)
            {
                from = Win32Shell.TryGetPathFromIdList(Marshal.ReadIntPtr(pidls));
                to = Win32Shell.TryGetPathFromIdList(Marshal.ReadIntPtr(pidls, IntPtr.Size));
            }
        }
        finally
        {
            Win32Shell.SHChangeNotification_Unlock(lockHandle);
        }

        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
            OnRenamed(from, to);
        return 0;
    }

    static void OnRenamed(string from, string to)
    {
        try
        {
            var moved = FolderRelocator.Relocate(CompanionState.Document, from, to);
            if (moved.Count == 0)
                return;
            TraceLog.Append("shell-menu.log", $"[RENAME] '{from}' -> '{to}' ({moved.Count} entr{(moved.Count == 1 ? "y" : "ies")})");
            CompanionState.Save();
            App.SyncMovedInBackground(moved);
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[RENAME] update failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_registration != 0)
            Win32Shell.SHChangeNotifyDeregister(_registration);
        _window?.Dispose();
    }

    [DllImport("shell32.dll")]
    static extern int SHGetFolderLocation(nint hwndOwner, int nFolder, nint hToken, uint dwReserved, out nint ppidl);
}
