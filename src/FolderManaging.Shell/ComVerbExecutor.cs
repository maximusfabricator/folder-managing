using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Storage;

namespace FolderManaging.Shell;

/// <summary>
/// Runs folder actions in-process from the COM host. Spawning <c>FolderManaging.Cli.exe</c> from the MSIX
/// surrogate often fails (wrong working directory, missing sibling exe), which looks like "nothing happens".
/// </summary>
internal static class ComVerbExecutor
{
    internal static void ApplyColor(string folderPath, string colorId)
    {
        ClickTrace.Step($"in-proc ApplyColor color={colorId}");
        TraceLog.Append("shell-menu.log", $"[COM] [PROCESS] in-proc FolderIconManager.Apply color={colorId}");
        FolderIconManager.Apply(folderPath, colorId);
        ClickTrace.Step("Apply done; writing colorToken to state.json");
        var editor = new FolderStateEditor(new JsonFolderStateStore());
        editor.SetColorTokenAsync(folderPath, colorId, CancellationToken.None).GetAwaiter().GetResult();
        ClickTrace.Step("state.json colorToken saved");
    }

    internal static void ResetColor(string folderPath)
    {
        ClickTrace.Step("in-proc ResetColor");
        TraceLog.Append("shell-menu.log", "[COM] [PROCESS] in-proc RemoveCustomization + state");
        // Must throw UnauthorizedAccessException so UserFeedback can offer / auto-run elevated CLI
        // (TryRemoveCustomization used to swallow access-denied and report SUCCESS while doing nothing).
        FolderIconManager.RemoveCustomization(folderPath);

        var editor = new FolderStateEditor(new JsonFolderStateStore());
        editor.SetColorTokenAsync(folderPath, null, CancellationToken.None).GetAwaiter().GetResult();
        ClickTrace.Step("state.json colorToken cleared");
    }

    internal static void Pin(string folderPath)
    {
        ClickTrace.Step("in-proc Pin");
        TraceLog.Append("shell-menu.log", "[COM] [PROCESS] in-proc PinManager.PinToQuickAccess");
        PinManager.PinToQuickAccess(folderPath);
        var editor = new FolderStateEditor(new JsonFolderStateStore());
        editor.SetPinnedAsync(folderPath, pinned: true, CancellationToken.None).GetAwaiter().GetResult();
    }

    internal static void Unpin(string folderPath)
    {
        ClickTrace.Step("in-proc Unpin");
        TraceLog.Append("shell-menu.log", "[COM] [PROCESS] in-proc PinManager.UnpinFromQuickAccess");
        PinManager.UnpinFromQuickAccess(folderPath);
        var editor = new FolderStateEditor(new JsonFolderStateStore());
        editor.SetPinnedAsync(folderPath, pinned: false, CancellationToken.None).GetAwaiter().GetResult();
    }

    internal static void Tag(string folderPath, string tagId)
    {
        ClickTrace.Step($"in-proc Tag tag={tagId}");
        TraceLog.Append("shell-menu.log", $"[COM] [PROCESS] in-proc UpsertTag tag={tagId}");
        var editor = new FolderStateEditor(new JsonFolderStateStore());
        editor.UpsertTagAsync(folderPath, tagId, CancellationToken.None).GetAwaiter().GetResult();
    }

    internal static void OpenSettings()
    {
        ClickTrace.Step("in-proc OpenSettings");
        TraceLog.Append("shell-menu.log", "[COM] [PROCESS] CompanionLauncher.TryLaunch");
        if (!CompanionLauncher.TryLaunch(out var err))
            throw new InvalidOperationException($"Could not start Companion: {err}");
    }

    internal static void OpenQuickColor(string folderPath)
    {
        ClickTrace.Step($"in-proc OpenQuickColor path='{folderPath}'");
        if (!CompanionLauncher.TryLaunch(new[] { "--quick-color", "--path", folderPath }, out var err))
            throw new InvalidOperationException($"Could not start quick color: {err}");
    }

    internal static void ApplyRgb(string folderPath, string hexRgb)
    {
        ClickTrace.Step($"in-proc ApplyRgb hex={hexRgb}");
        TraceLog.Append("shell-menu.log", $"[COM] [PROCESS] in-proc FolderIconManager.ApplyRgb hex={hexRgb}");
        var tint = System.Drawing.ColorTranslator.FromHtml("#" + hexRgb);
        FolderIconManager.ApplyRgb(folderPath, tint);
        var editor = new FolderStateEditor(new JsonFolderStateStore());
        editor.SetColorTokenAsync(folderPath, $"rgb-{hexRgb.ToLowerInvariant()}", CancellationToken.None).GetAwaiter().GetResult();
        ClickTrace.Step("state.json colorToken saved");
    }
}
