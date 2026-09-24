using FolderManaging.Core.Models;

namespace FolderManaging.Core.Storage;

/// <summary>Keeps per-folder <see cref="FolderStateEntry.Tags"/> aligned when menu tag presets are removed.</summary>
public static class FolderStateTagSync
{
    /// <summary>Removes <paramref name="tagId"/> from every folder's tag list (case-insensitive).</summary>
    public static void RemoveTagIdFromAllFolders(AppStateDocument doc, string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
            return;

        foreach (var folder in doc.Folders)
            folder.Tags.RemoveAll(t => string.Equals(t, tagId, StringComparison.OrdinalIgnoreCase));
    }
}
