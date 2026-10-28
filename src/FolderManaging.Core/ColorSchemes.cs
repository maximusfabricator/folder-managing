// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>One color of a ready-made scheme; <see cref="NameKey"/> is a string-table key (the palette names).</summary>
public sealed record SchemeColor(string NameKey, string HexRgb);

/// <summary>A ready-made set of folder colors; <see cref="NameKey"/> is a string-table key.</summary>
public sealed record ColorScheme(string Id, string NameKey, IReadOnlyList<SchemeColor> Colors);

public static class ColorSchemes
{
    public static readonly IReadOnlyList<ColorScheme> All =
    [
        new("vivid", "SchemeVivid",
        [
            new("PalRed", "E81123"), new("PalOrange", "FF8C00"), new("PalYellow", "FFB900"),
            new("PalGreen", "107C10"), new("PalBlue", "0078D4"), new("PalPurple", "881798"),
        ]),
        new("pastel", "SchemePastel",
        [
            new("PalPink", "F4B6C2"), new("PalOrange", "FFD3A5"), new("PalYellow", "FDF2A6"),
            new("PalGreen", "B9E4C9"), new("PalBlue", "AED9F4"), new("PalPurple", "D5C3F0"),
        ]),
        new("earth", "SchemeEarth",
        [
            new("PalBrown", "8B5A2B"), new("PalOrange", "C8743A"), new("PalYellow", "C2A35A"),
            new("PalGreen", "6B8E23"), new("PalTeal", "4F7F72"), new("PalGray", "7D7461"),
        ]),
        new("ocean", "SchemeOcean",
        [
            new("PalIndigo", "1B3A6B"), new("PalBlue", "0F6CBD"), new("PalCyan", "0099BC"),
            new("PalTeal", "00A6A0"), new("PalGreen", "4CB89A"), new("PalLime", "9BD770"),
        ]),
        new("status", "SchemeStatus",
        [
            new("PalRed", "D13438"), new("PalOrange", "F7630C"), new("PalYellow", "FFB900"),
            new("PalGreen", "107C10"), new("PalGray", "8A8886"),
        ]),
    ];

    /// <summary>
    /// Adds the scheme's colors that are not in "My colors" yet (same RGB counts as present).
    /// </summary>
    /// <param name="label">Menu name for a color: (scheme name key, color name key) → text.</param>
    /// <returns>How many colors were added.</returns>
    public static int AddTo(AppStateDocument doc, ColorScheme scheme, Func<string, string, string> label)
    {
        var added = 0;
        foreach (var color in scheme.Colors)
        {
            if (doc.MenuColors.Any(c => string.Equals(c.HexRgb, color.HexRgb, StringComparison.OrdinalIgnoreCase)))
                continue;
            doc.MenuColors.Add(new MenuColorPreset
            {
                Id = NextCustomId(doc),
                Label = label(scheme.NameKey, color.NameKey),
                HexRgb = color.HexRgb,
            });
            added++;
        }

        return added;
    }

    /// <summary>First free <c>custom-NN</c> id, the form the Colors page uses.</summary>
    public static string NextCustomId(AppStateDocument doc)
    {
        for (var n = 1; ; n++)
        {
            var id = $"custom-{n:00}";
            if (!doc.MenuColors.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
                return id;
        }
    }

    /// <summary>Adds an RGB color to "My colors" unless it is there already. Returns the color's id.</summary>
    public static string SaveToMyColors(AppStateDocument doc, string hexRgb, string label = "")
    {
        if (!MenuColorHex.TryParse(hexRgb, out var color))
            throw new ArgumentException($"Bad color '{hexRgb}'.", nameof(hexRgb));
        var hex = MenuColorHex.ToHexRgb(color);
        var existing = doc.MenuColors.FirstOrDefault(c => string.Equals(c.HexRgb, hex, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing.Id;
        var id = NextCustomId(doc);
        doc.MenuColors.Add(new MenuColorPreset { Id = id, Label = label, HexRgb = hex });
        return id;
    }
}
