// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.QuickAccess;

namespace FolderManaging.Shell;

/// <summary>
/// Which folders Quick access really has pinned (also those pinned with Windows' own command), so the menu offers
/// "Pin" or "Unpin" correctly. Cached briefly: Explorer asks for every row while it builds the menu.
/// </summary>
internal static class PinnedStateCache
{
    static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(2);
    static readonly object Gate = new();
    static DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    static HashSet<string> _pinned = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsPinned(string folderPath)
    {
        var normalized = PinRefresher.NormalizePath(folderPath);
        lock (Gate)
        {
            if (DateTimeOffset.UtcNow - _loadedAt > MaxAge)
            {
                _pinned = QuickAccessService.GetPinned()
                    .Select(p => PinRefresher.NormalizePath(p.Path))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _loadedAt = DateTimeOffset.UtcNow;
            }

            return _pinned.Contains(normalized);
        }
    }

    internal static void Invalidate()
    {
        lock (Gate)
            _loadedAt = DateTimeOffset.MinValue;
    }
}
