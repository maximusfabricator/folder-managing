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

    /// <summary>Shows the failure; for protected folders elevates (auto or after confirm). Returns <c>true</c> if elevated retry succeeded.</summary>
    internal static bool ReportFolderActionFailure(string action, string path, string? value, Exception ex)
    {
        try
        {
            if (IsAccessDenied(ex))
            {
                var autoElevate = false;
                try
                {
                    autoElevate = MenuStateReader.GetMerged().AutoElevateProtectedFolders;
                }
                catch (Exception readEx)
                {
                    ClickTrace.Step($"feedback: could not read AutoElevate setting: {readEx.Message}");
                    autoElevate = true;
                }

                if (!autoElevate)
                {
                    ClickTrace.Step("feedback: access denied dialog shown (auto-elevate off)");
                    var answer = NativeMessageBox.Show(
                        UiText.Format("UacProtected", UiText.LeftToRight(path)),
                        Caption,
                        NativeMessageBox.YesNo | NativeMessageBox.IconWarning | OnTop);
                    ClickTrace.Step($"feedback: answer={(answer == NativeMessageBox.IdYes ? "yes" : "no")}");
                    if (answer != NativeMessageBox.IdYes)
                        return false;
                }
                else
                {
                    ClickTrace.Step("feedback: access denied → auto-elevate (setting on)");
                }

                var ok = ElevatedCliInvoker.TryInvoke(action, path, value, out var elevErr);
                if (!ok)
                {
                    var tip = string.Equals(elevErr, "User cancelled UAC.", StringComparison.Ordinal)
                        ? UiText.Get("UacOnce")
                        : elevErr;
                    NativeMessageBox.Show(
                        UiText.Format("UacFail", tip),
                        Caption,
                        NativeMessageBox.Ok | NativeMessageBox.IconError | OnTop);
                }

                return ok;
            }

            ClickTrace.Step("feedback: error dialog shown");
            NativeMessageBox.Show(
                UiText.Format("ChangeFolderFail", UiText.LeftToRight(path), ex.Message),
                Caption,
                NativeMessageBox.Ok | NativeMessageBox.IconError | OnTop);
        }
        catch (Exception inner)
        {
            ClickTrace.Step($"feedback dialog failed: {inner.Message}");
        }

        return false;
    }

    /// <summary>
    /// Once per machine/user state: explain that Explorer sorts colors via Tags/Title (not a «Color» menu item).
    /// </summary>
    internal static void MaybeShowColorSortTip() => ColorSortTip.TryShowOnce();

    static bool IsAccessDenied(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException)
                return true;
        }

        return false;
    }
}
