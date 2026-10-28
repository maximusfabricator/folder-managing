// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Interop;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Storage;

namespace FolderManaging.Shell;

/// <summary>Tells the user why a context-menu action did nothing (otherwise failures are only visible in the log).</summary>
internal static class UserFeedback
{
    const string Caption = "Folder Manager";
    const uint OnTop = NativeMessageBox.SetForeground | NativeMessageBox.Topmost;

    /// <summary>The paths listed in a message; the rest is "…".</summary>
    const int ListedPaths = 8;

    [ThreadStatic] static bool _answeredNo;

    /// <summary>
    /// Asked once for all protected folders of a batch. With automatic elevation on (the default) it does not ask.
    /// </summary>
    internal static bool ConfirmElevation(IReadOnlyList<string> paths)
    {
        _answeredNo = false;
        var autoElevate = true;
        try
        {
            autoElevate = MenuStateReader.GetMerged().AutoElevateProtectedFolders;
        }
        catch (Exception readEx)
        {
            ClickTrace.Step($"feedback: could not read AutoElevate setting: {readEx.Message}");
        }

        if (autoElevate)
        {
            ClickTrace.Step($"feedback: access denied on {paths.Count} folder(s) → auto-elevate (setting on)");
            return true;
        }

        var text = paths.Count == 1
            ? UiText.Format("UacProtected", UiText.LeftToRight(paths[0]))
            : UiText.Format("UacProtectedMany", paths.Count, ListOf(paths));
        var answer = NativeMessageBox.Show(text, Caption, NativeMessageBox.YesNo | NativeMessageBox.IconWarning | OnTop);
        ClickTrace.Step($"feedback: answer={(answer == NativeMessageBox.IdYes ? "yes" : "no")}");
        _answeredNo = answer != NativeMessageBox.IdYes;
        return !_answeredNo;
    }

    /// <summary>One message for everything that went wrong in a batch (nothing when all went well).</summary>
    internal static void ReportBatch(FolderBatchResult result)
    {
        try
        {
            if (result.ElevationDeclined)
            {
                if (!_answeredNo)
                    NativeMessageBox.Show(UiText.Format("UacFail", UiText.Get("UacOnce")), Caption,
                        NativeMessageBox.Ok | NativeMessageBox.IconError | OnTop);
                return;
            }

            var failed = result.Failed;
            if (failed.Count == 0)
                return;

            var text = failed.Count == 1
                ? UiText.Format("ChangeFolderFail", UiText.LeftToRight(failed[0].Path), failed[0].Message)
                : UiText.Format("ChangeFoldersFail", failed.Count,
                    string.Join("\n", failed.Take(ListedPaths).Select(f => UiText.LeftToRight(f.Path) + " — " + f.Message)) +
                    (failed.Count > ListedPaths ? "\n…" : ""));
            ClickTrace.Step($"feedback: error dialog shown ({failed.Count} failed)");
            NativeMessageBox.Show(text, Caption, NativeMessageBox.Ok | NativeMessageBox.IconError | OnTop);
        }
        catch (Exception inner)
        {
            ClickTrace.Step($"feedback dialog failed: {inner.Message}");
        }
    }

    /// <summary>Shows a failure that is not about one folder's rights (pin, opening a window).</summary>
    internal static void ReportFailure(string path, Exception ex)
    {
        try
        {
            NativeMessageBox.Show(UiText.Format("ChangeFolderFail", UiText.LeftToRight(path), ex.Message), Caption,
                NativeMessageBox.Ok | NativeMessageBox.IconError | OnTop);
        }
        catch (Exception inner)
        {
            ClickTrace.Step($"feedback dialog failed: {inner.Message}");
        }
    }

    static string ListOf(IReadOnlyList<string> paths) =>
        string.Join("\n", paths.Take(ListedPaths).Select(UiText.LeftToRight)) + (paths.Count > ListedPaths ? "\n…" : "");

    /// <summary>
    /// Once per machine/user state: explain that Explorer sorts colors via Tags/Title (not a «Color» menu item).
    /// </summary>
    internal static void MaybeShowColorSortTip() => ColorSortTip.TryShowOnce();
}
