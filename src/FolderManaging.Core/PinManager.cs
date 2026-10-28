// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.QuickAccess;

namespace FolderManaging.Core;

/// <summary>
/// Pin / unpin a folder in Quick access. Both check the real Quick access list first: Windows' <c>pintohome</c> verb
/// unpins a folder that is already pinned, and <c>unpinfromhome</c> on the folder itself silently does nothing.
/// </summary>
public static class PinManager
{
    public static void PinToQuickAccess(string folderPath)
    {
        var full = Path.GetFullPath(folderPath);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException(full);
        QuickAccessService.Pin(full);
    }

    /// <summary>Also unpins a folder that no longer exists.</summary>
    public static void UnpinFromQuickAccess(string folderPath) =>
        QuickAccessService.Unpin(Path.GetFullPath(folderPath));
}
