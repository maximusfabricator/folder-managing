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
    /// <returns><c>null</c> when the folder now matches the saved state, otherwise the reason for the user.</returns>
    internal static async Task<string?> SyncAsync(Window? owner, string folderPath)
    {
        try
        {
            await Task.Run(() => FolderIconManager.SyncFolderTags(folderPath));
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

        if (!CompanionState.Document.AutoElevateProtectedFolders && !AskElevation(owner, folderPath))
            return UiText.Get("UacOnce");

        var (ok, error) = await Task.Run(() =>
        {
            var done = ElevatedCliInvoker.TryInvoke("sync", folderPath, value: null, out var message);
            return (done, message);
        });
        if (ok)
            return null;
        return string.Equals(error, "User cancelled UAC.", StringComparison.Ordinal) ? UiText.Get("UacOnce") : error;
    }

    static bool AskElevation(Window? owner, string folderPath)
    {
        var answer = FmMessageBox.Show(
            owner,
            UiText.Format("UacProtected", UiText.LeftToRight(folderPath)),
            "Folder Managing",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }
}
