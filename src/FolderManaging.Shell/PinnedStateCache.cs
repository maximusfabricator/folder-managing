// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Text.Json;
using FolderManaging.Core.Storage;

namespace FolderManaging.Shell;

/// <summary>
/// Fast, low-allocation check for whether a folder is pinned (based on app state.json).
/// Cached briefly to avoid UI lag when Explorer queries command states repeatedly.
/// </summary>
internal static class PinnedStateCache
{
    private static readonly object Gate = new();
    private static DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private static Dictionary<string, bool> _pinnedByPath = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsPinned(string folderPath)
    {
        var normalized = Normalize(folderPath);

        EnsureLoaded();

        lock (Gate)
        {
            return _pinnedByPath.TryGetValue(normalized, out var pinned) && pinned;
        }
    }

    private static void EnsureLoaded()
    {
        // Explorer can call GetState many times while building menus; keep the cache short but non-zero.
        var now = DateTimeOffset.UtcNow;
        if ((now - _loadedAt) < TimeSpan.FromMilliseconds(750))
            return;

        lock (Gate)
        {
            if ((now - _loadedAt) < TimeSpan.FromMilliseconds(750))
                return;

            _loadedAt = now;
            _pinnedByPath = LoadPinnedMap();
        }
    }

    private static Dictionary<string, bool> LoadPinnedMap()
    {
        try
        {
            var path = AppPaths.StateFilePath;
            if (!File.Exists(path))
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty("folders", out var folders) || folders.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in folders.EnumerateArray())
            {
                if (!f.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String)
                    continue;
                var raw = p.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var isPinned = false;
                if (f.TryGetProperty("isPinned", out var pin) && pin.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    isPinned = pin.GetBoolean();

                map[Normalize(raw)] = isPinned;
            }

            return map;
        }
        catch (Exception ex)
        {
            ShellActionLog.Write($"PinnedStateCache failed to load state.json: {ex}");
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return path.Trim();
        }
    }
}

