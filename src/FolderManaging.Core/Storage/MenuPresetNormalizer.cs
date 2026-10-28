// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Models;

namespace FolderManaging.Core.Storage;

/// <summary>
/// Ensures <see cref="AppStateDocument"/> contains the built-in color ids, default tag rows when empty,
/// and migrates legacy branded color ids (<c>coral</c>/<c>ocean</c>/<c>mint</c>/<c>plum</c>) onto the new generic ones.
/// </summary>
public static class MenuPresetNormalizer
{
    public static void Merge(AppStateDocument doc)
    {
        doc.MenuColors ??= new List<MenuColorPreset>();
        doc.MenuTags ??= new List<MenuTagPreset>();
        doc.EnabledColorSchemes ??= new List<string>();

        MigrateLegacyColorIds(doc);

        var byColorId = new Dictionary<string, MenuColorPreset>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in doc.MenuColors)
        {
            if (string.IsNullOrWhiteSpace(c.Id))
                continue;
            if (!byColorId.ContainsKey(c.Id))
                byColorId[c.Id] = c;
        }

        foreach (var (id, rgb) in FolderColors.PresetRgb)
        {
            if (byColorId.TryGetValue(id, out var row))
            {
                if (!MenuColorHex.TryParse(row.HexRgb, out _))
                    row.HexRgb = MenuColorHex.ToHexRgb(rgb);
                continue;
            }

            doc.MenuColors.Add(new MenuColorPreset
            {
                Id = id,
                Label = "",
                HexRgb = MenuColorHex.ToHexRgb(rgb),
            });
        }

        if (doc.MenuTags.Count == 0)
        {
            if (doc.DefaultTagsSeeded != true)
            {
                foreach (var (id, label, hex) in DefaultTags())
                    doc.MenuTags.Add(new MenuTagPreset { Id = id, Label = label, HexRgb = hex });
            }
        }
        else
        {
            foreach (var t in doc.MenuTags)
            {
                if (string.IsNullOrWhiteSpace(t.Id))
                    continue;
                if (string.IsNullOrWhiteSpace(t.Label))
                    t.Label = t.Id;
                if (!MenuColorHex.TryParse(t.HexRgb, out _))
                    t.HexRgb = MenuTagPresetDefaults.HexRgb;
            }
        }

        doc.DefaultTagsSeeded = true;
    }

    static void MigrateLegacyColorIds(AppStateDocument doc)
    {
        for (var i = 0; i < doc.MenuColors.Count; i++)
        {
            var row = doc.MenuColors[i];
            if (row is null || string.IsNullOrWhiteSpace(row.Id))
                continue;
            if (FolderColors.LegacyIdMigration.TryGetValue(row.Id, out var newId))
                row.Id = newId;
        }

        // Drop duplicates that the migration may have introduced (e.g. legacy "plum" + already-present "purple").
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<MenuColorPreset>(doc.MenuColors.Count);
        foreach (var row in doc.MenuColors)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Id))
                continue;
            if (seen.Add(row.Id))
                deduped.Add(row);
        }

        doc.MenuColors.Clear();
        doc.MenuColors.AddRange(deduped);

        foreach (var f in doc.Folders)
        {
            if (string.IsNullOrWhiteSpace(f.ColorToken))
                continue;
            if (FolderColors.LegacyIdMigration.TryGetValue(f.ColorToken, out var newId))
                f.ColorToken = newId;
        }
    }

    static IEnumerable<(string Id, string Label, string HexRgb)> DefaultTags()
    {
        yield return ("work", "Work", "0078D4");
        yield return ("personal", "Personal", "E3008C");
        yield return ("later", "Later", "FFB900");
        yield return ("archive", "Archive", "8A8886");
    }
}
