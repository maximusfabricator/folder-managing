using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Companion;

internal static class CompanionState
{
    static AppStateDocument? _document;

    internal static AppStateDocument Document => _document ??= JsonFolderStateStore.LoadSynchronously();

    internal static void Save()
    {
        MenuPresetNormalizer.Merge(Document);
        JsonFolderStateStore.SaveSynchronously(Document);
        MenuStateReader.Invalidate();
        ClickTrace.Step("CompanionState.Save → state.json written + MenuStateReader invalidated");
    }
}
