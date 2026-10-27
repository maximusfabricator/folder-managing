// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

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

    /// <summary>True when <paramref name="folderPath"/> carries <paramref name="tagId"/> (case-insensitive).</summary>
    public static bool FolderHasTag(AppStateDocument doc, string folderPath, string tagId)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(tagId))
            return false;

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        foreach (var folder in doc.Folders)
        {
            try
            {
                if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Path)), full, StringComparison.OrdinalIgnoreCase))
                    return folder.Tags.Contains(tagId, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                // a broken path stored in state.json is not this folder
            }
        }

        return false;
    }
}
