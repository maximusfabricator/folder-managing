// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Drawing;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;

namespace FolderManaging.Core.Storage;

/// <summary>Caches merged <see cref="AppStateDocument"/> (invalidated when <c>state.json</c> changes on disk).</summary>
public static class MenuStateReader
{
    static long _stateTicks = long.MinValue;
    static AppStateDocument? _doc;

    public static AppStateDocument GetMerged()
    {
        var path = AppPaths.StateFilePath;
        var ticks = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L;
        if (_doc is not null && ticks == _stateTicks)
            return _doc;

        _doc = JsonFolderStateStore.LoadSynchronously();
        _stateTicks = ticks;
        // The Explorer menu host lives for hours; it picks up a language changed in Settings on the next right-click.
        UiText.Use(_doc.Language);
        return _doc;
    }

    /// <summary>Drops the in-memory cache so the next read picks up Companion edits immediately.</summary>
    public static void Invalidate()
    {
        _doc = null;
        _stateTicks = long.MinValue;
    }

    public static bool TryGetMenuTint(string colorId, out Color tint)
    {
        tint = default;
        if (string.IsNullOrWhiteSpace(colorId))
            return false;
        // Scheme colors resolve whether or not the scheme is on: a folder keeps its color when the scheme is turned off.
        if (ColorSchemes.TryGetColor(colorId, out tint))
            return true;

        var doc = GetMerged();
        var row = doc.MenuColors.FirstOrDefault(c =>
            string.Equals(c.Id, colorId, StringComparison.OrdinalIgnoreCase));
        if (row is not null && MenuColorHex.TryParse(row.HexRgb, out tint))
            return true;

        return FolderColors.TryGetColor(colorId, out tint);
    }
}
