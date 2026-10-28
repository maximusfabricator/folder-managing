// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.IO;
using System.Windows;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;

namespace FolderManaging.Companion;

/// <summary>
/// Writes one folder's <c>desktop.ini</c> from <c>state.json</c>. Protected folders (Program Files, Windows)
/// go through the elevated worker, the same way Quick color does.
/// </summary>
internal static class FolderWriteGuard
{
    /// <summary>The paths listed in a message; the rest is "…".</summary>
    const int ListedPaths = 8;

    /// <returns><c>null</c> when the folder now matches the saved state, otherwise the reason for the user.</returns>
    internal static async Task<string?> SyncAsync(Window? owner, string folderPath)
    {
        try
        {
            await Task.Run(() => FolderIconManager.SyncFolderTags(folderPath));
            PinRefreshScheduler.Request();
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            ClickTrace.Step($"sync access denied, elevating: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            ClickTrace.Step($"sync failed: {ex.Message}");
            return ex.Message;
        }

        if (!CompanionState.Document.AutoElevateProtectedFolders && !AskElevation(owner, [folderPath]))
            return UiText.Get("UacOnce");

        var (ok, error) = await Task.Run(() =>
        {
            var done = ElevatedCliInvoker.TryInvoke("sync", folderPath, value: null, out var message);
            return (done, message);
        });
        if (ok)
        {
            PinRefreshScheduler.Request();
            return null;
        }

        return string.Equals(error, ElevatedCliInvoker.UacCancelledMessage, StringComparison.Ordinal) ? UiText.Get("UacOnce") : error;
    }

    /// <summary>
    /// Runs the actions off the UI thread; protected folders together through the elevated worker (one UAC prompt).
    /// The caller reloads <see cref="CompanionState"/> afterwards: the actions write <c>state.json</c> themselves.
    /// </summary>
    internal static async Task<FolderBatchResult> RunAsync(Window? owner, IReadOnlyList<FolderAction> actions)
    {
        var autoElevate = CompanionState.Document.AutoElevateProtectedFolders;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var result = await Task.Run(() => FolderBatch.Run(actions, paths =>
            autoElevate || dispatcher.Invoke(() => AskElevation(owner, paths))));
        CompanionState.ReloadFromDiskIfStale();
        PinRefreshScheduler.Request();
        return result;
    }

    /// <summary><see cref="FolderDiagnostics.Repair"/> off the UI thread, asking like <see cref="RunAsync"/>.</summary>
    internal static async Task<FolderBatchResult> RepairAsync(Window? owner, IReadOnlyList<FolderCheck> checks)
    {
        var autoElevate = CompanionState.Document.AutoElevateProtectedFolders;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var result = await Task.Run(() => FolderDiagnostics.Repair(checks, paths =>
            autoElevate || dispatcher.Invoke(() => AskElevation(owner, paths))));
        CompanionState.ReloadFromDiskIfStale();
        return result;
    }

    /// <summary>One message for the folders that could not be changed; nothing when all went well.</summary>
    internal static void Report(Window? owner, FolderBatchResult result)
    {
        var failed = result.Failed.Where(f => f.Message.Length > 0).ToList();
        string? text = null;
        if (failed.Count == 1)
            text = UiText.Format("ChangeFolderFail", UiText.LeftToRight(failed[0].Path), failed[0].Message);
        else if (failed.Count > 1)
            text = UiText.Format("ChangeFoldersFail", failed.Count,
                string.Join("\n", failed.Take(ListedPaths).Select(f => UiText.LeftToRight(f.Path) + " — " + f.Message)) +
                (failed.Count > ListedPaths ? "\n…" : ""));
        else if (result.ElevationDeclined)
            text = UiText.Get("UacOnce");
        if (text is not null)
            FmMessageBox.Show(owner, text, "Folder Managing", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    static bool AskElevation(Window? owner, IReadOnlyList<string> paths)
    {
        var text = paths.Count == 1
            ? UiText.Format("UacProtected", UiText.LeftToRight(paths[0]))
            : UiText.Format("UacProtectedMany", paths.Count,
                string.Join("\n", paths.Take(ListedPaths).Select(UiText.LeftToRight)) + (paths.Count > ListedPaths ? "\n…" : ""));
        var answer = FmMessageBox.Show(owner, text, "Folder Managing", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }
}
