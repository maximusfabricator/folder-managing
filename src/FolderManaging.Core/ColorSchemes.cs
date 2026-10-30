// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Drawing;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>
/// One color of a built-in scheme. <see cref="Id"/> is what folders store in <c>state.json</c> and must never change;
/// <see cref="NameKey"/> is a string-table key (the palette names).
/// </summary>
public sealed record SchemeColor(string Id, string NameKey, string HexRgb)
{
    public Color Color => MenuColorHex.TryParse(HexRgb, out var c) ? c : Color.Gray;
}

/// <summary>A built-in, read-only set of folder colors; <see cref="NameKey"/> is a string-table key.</summary>
public sealed record ColorScheme(string Id, string NameKey, IReadOnlyList<SchemeColor> Colors)
{
    public string Name => UiText.Get(NameKey);
}

/// <summary>
/// The built-in color schemes. The user turns them on or off (<see cref="AppStateDocument.EnabledColorSchemes"/>);
/// they are never copied into "My colors". A folder keeps its scheme color when the scheme is turned off.
/// </summary>
public static class ColorSchemes
{
    /// <summary>Every scheme color id starts with this; user colors never do.</summary>
    public const string IdPrefix = "scheme-";

    public static readonly IReadOnlyList<ColorScheme> All =
    [
        Scheme("vivid", "SchemeVivid",
            ("red", "PalRed", "E81123"), ("orange", "PalOrange", "FF8C00"), ("yellow", "PalYellow", "FFB900"),
            ("green", "PalGreen", "107C10"), ("blue", "PalBlue", "0078D4"), ("purple", "PalPurple", "881798")),
        Scheme("pastel", "SchemePastel",
            ("pink", "PalPink", "F4B6C2"), ("orange", "PalOrange", "FFD3A5"), ("yellow", "PalYellow", "FDF2A6"),
            ("green", "PalGreen", "B9E4C9"), ("blue", "PalBlue", "AED9F4"), ("purple", "PalPurple", "D5C3F0")),
        Scheme("earth", "SchemeEarth",
            ("brown", "PalBrown", "8B5A2B"), ("orange", "PalOrange", "C8743A"), ("yellow", "PalYellow", "C2A35A"),
            ("green", "PalGreen", "6B8E23"), ("teal", "PalTeal", "4F7F72"), ("gray", "PalGray", "7D7461")),
        Scheme("ocean", "SchemeOcean",
            ("indigo", "PalIndigo", "1B3A6B"), ("blue", "PalBlue", "0F6CBD"), ("cyan", "PalCyan", "0099BC"),
            ("teal", "PalTeal", "00A6A0"), ("green", "PalGreen", "4CB89A"), ("lime", "PalLime", "9BD770")),
        Scheme("status", "SchemeStatus",
            ("red", "PalRed", "D13438"), ("orange", "PalOrange", "F7630C"), ("yellow", "PalYellow", "FFB900"),
            ("green", "PalGreen", "107C10"), ("gray", "PalGray", "8A8886")),
    ];

    static ColorScheme Scheme(string id, string nameKey, params (string Color, string NameKey, string Hex)[] colors) =>
        new(id, nameKey, colors.Select(c => new SchemeColor($"{IdPrefix}{id}-{c.Color}", c.NameKey, c.Hex)).ToList());

    public static ColorScheme? Find(string? schemeId) =>
        All.FirstOrDefault(s => string.Equals(s.Id, schemeId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a scheme color by its id, whether or not the scheme is turned on.</summary>
    public static bool TryFindColor(string? colorId, out ColorScheme scheme, out SchemeColor color)
    {
        if (!string.IsNullOrWhiteSpace(colorId) && colorId.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var s in All)
            {
                foreach (var c in s.Colors)
                {
                    if (!string.Equals(c.Id, colorId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    scheme = s;
                    color = c;
                    return true;
                }
            }
        }

        scheme = null!;
        color = null!;
        return false;
    }

    public static bool TryGetColor(string? colorId, out Color color)
    {
        var found = TryFindColor(colorId, out _, out var c);
        color = found ? c.Color : default;
        return found;
    }

    /// <summary>"Blue (Ocean)": how a scheme color is named outside its scheme (recent colors, folder list).</summary>
    public static string DisplayName(ColorScheme scheme, SchemeColor color) =>
        UiText.Format("SchemeColorLabel", scheme.Name, UiText.Get(color.NameKey));

    public static bool IsEnabled(AppStateDocument doc, string schemeId) =>
        doc.EnabledColorSchemes?.Any(id => string.Equals(id, schemeId, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>The schemes that are turned on, in the built-in order.</summary>
    public static IReadOnlyList<ColorScheme> Enabled(AppStateDocument doc) =>
        All.Where(s => IsEnabled(doc, s.Id)).ToList();

    /// <summary>
    /// Turns a scheme on or off. Ids this version does not know (from a newer one) are kept.
    /// Does not change <see cref="AppStateDocument.HiddenColorIds"/>: a hidden color of this scheme stays hidden
    /// when the scheme is turned on again.
    /// </summary>
    public static void SetEnabled(AppStateDocument doc, string schemeId, bool enabled)
    {
        doc.EnabledColorSchemes ??= new List<string>();
        doc.EnabledColorSchemes.RemoveAll(id => string.Equals(id, schemeId, StringComparison.OrdinalIgnoreCase));
        if (enabled)
            doc.EnabledColorSchemes.Add(schemeId.ToLowerInvariant());
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
