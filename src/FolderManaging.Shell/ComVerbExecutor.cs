// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.QuickAccess;
using FolderManaging.Core.Storage;

namespace FolderManaging.Shell;

/// <summary>
/// Runs folder actions in-process from the COM host. Spawning <c>FolderManaging.Cli.exe</c> from the MSIX
/// surrogate often fails (wrong working directory, missing sibling exe), which looks like "nothing happens".
/// Every action takes the whole Explorer selection.
/// </summary>
internal static class ComVerbExecutor
{
    /// <summary>Color, reset or tags for all selected folders; protected ones together with one UAC prompt.</summary>
    internal static FolderBatchResult RunBatch(IReadOnlyList<FolderAction> actions)
    {
        ClickTrace.Step($"in-proc batch {actions.FirstOrDefault()?.Verb} x{actions.Count}");
        TraceLog.Append("shell-menu.log", $"[COM] [PROCESS] batch {actions.FirstOrDefault()?.Verb} x{actions.Count}");
        var result = FolderBatch.Run(actions, UserFeedback.ConfirmElevation);
        UserFeedback.ReportBatch(result);
        if (result.Done.Count > 0 && actions.Any(a => a.Kind is FolderActionKind.Color or FolderActionKind.Rgb))
            UserFeedback.MaybeShowColorSortTip();
        RefreshPinsAfterChange(result.Done);
        return result;
    }

    /// <summary>
    /// A pinned folder that just got (or lost) its color keeps its old picture in Quick access until it is pinned again.
    /// The CLI does that in the background so the menu closes at once.
    /// </summary>
    static void RefreshPinsAfterChange(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        try
        {
            if (paths.Any(QuickAccessService.HasStalePin))
                ShellCliLauncher.StartDetached("repair-pins", "--auto");
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[COM] pin check failed: {ex.Message}");
        }
    }

    /// <summary>Adds the tag to every selected folder, or removes it when all of them already have it.</summary>
    internal static bool TagShouldAdd(IReadOnlyList<string> paths, string tagId)
    {
        var doc = JsonFolderStateStore.LoadSynchronously();
        return !paths.All(p => FolderStateTagSync.FolderHasTag(doc, p, tagId));
    }

    internal static void Pin(IReadOnlyList<string> paths)
    {
        ClickTrace.Step($"in-proc Pin x{paths.Count}");
        var editor = new FolderStateEditor(new JsonFolderStateStore());
        foreach (var path in paths)
        {
            try
            {
                PinManager.PinToQuickAccess(path);
                editor.SetPinnedAsync(path, pinned: true).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                TraceLog.Append("shell-menu.log", $"[COM] pin failed '{path}': {ex.Message}");
                UserFeedback.ReportFailure(path, ex);
            }
        }

        PinnedStateCache.Invalidate();
    }

    internal static void Unpin(IReadOnlyList<string> paths)
    {
        ClickTrace.Step($"in-proc Unpin x{paths.Count}");
        var editor = new FolderStateEditor(new JsonFolderStateStore());
        foreach (var path in paths)
        {
            try
            {
                PinManager.UnpinFromQuickAccess(path);
                editor.SetPinnedAsync(path, pinned: false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                TraceLog.Append("shell-menu.log", $"[COM] unpin failed '{path}': {ex.Message}");
                UserFeedback.ReportFailure(path, ex);
            }
        }

        PinnedStateCache.Invalidate();
    }

    internal static void OpenSettings()
    {
        ClickTrace.Step("in-proc OpenSettings");
        TraceLog.Append("shell-menu.log", "[COM] [PROCESS] CompanionLauncher.TryLaunch");
        if (!CompanionLauncher.TryLaunch(out var err))
            throw new InvalidOperationException($"Could not start Companion: {err}");
    }

    internal static void OpenQuickColor(IReadOnlyList<string> paths)
    {
        ClickTrace.Step($"in-proc OpenQuickColor x{paths.Count}");
        var args = new List<string> { "--quick-color" };
        foreach (var path in paths)
        {
            args.Add("--path");
            args.Add(path);
        }

        if (!CompanionLauncher.TryLaunch(args.ToArray(), out var err))
            throw new InvalidOperationException($"Could not start quick color: {err}");
    }
}
