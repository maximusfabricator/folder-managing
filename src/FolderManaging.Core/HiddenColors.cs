// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>
/// Colors the user hid (<see cref="AppStateDocument.HiddenColorIds"/>).
/// Hiding only changes what is offered. A folder that already uses the color keeps it,
/// and turning a color scheme off and on does not clear or refill the list.
/// </summary>
public static class HiddenColors
{
    /// <summary>Drops blanks and duplicate ids. A missing list becomes an empty one.</summary>
    public static void Normalize(AppStateDocument doc)
    {
        var source = doc.HiddenColorIds;
        var kept = new List<string>();
        if (source is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in source)
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var trimmed = id.Trim();
                if (seen.Add(trimmed))
                    kept.Add(trimmed);
            }
        }

        doc.HiddenColorIds = kept;
    }

    public static bool IsHidden(AppStateDocument doc, string? id) =>
        !string.IsNullOrWhiteSpace(id) &&
        doc.HiddenColorIds?.Any(h => string.Equals(h, id, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// My colors, including the built-in red, blue, green and purple, can be deleted.
    /// A scheme color cannot: the Colors page shows Hide for it, and Restore brings it back.
    /// </summary>
    public static bool CanDelete(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        if (id.StartsWith(ColorSchemes.IdPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public static bool CanHide(string? id) => !string.IsNullOrWhiteSpace(id) && !CanDelete(id);

    public static void Hide(AppStateDocument doc, string id)
    {
        if (!CanHide(id))
            return;
        Normalize(doc);
        if (IsHidden(doc, id))
            return;
        doc.HiddenColorIds.Add(id.Trim().ToLowerInvariant());
    }

    public static void Restore(AppStateDocument doc, string id)
    {
        if (doc.HiddenColorIds is null || string.IsNullOrWhiteSpace(id))
            return;
        doc.HiddenColorIds.RemoveAll(h => string.Equals(h, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>My colors, without the ones the user hid. The rows stay in <see cref="AppStateDocument.MenuColors"/>.</summary>
    public static IEnumerable<MenuColorPreset> OfferedPresets(AppStateDocument doc) =>
        (doc.MenuColors ?? []).Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Id) && !IsHidden(doc, c.Id));

    /// <summary>Colors of one scheme that are still offered. Empty when every color of the scheme is hidden.</summary>
    public static IEnumerable<SchemeColor> OfferedSchemeColors(AppStateDocument doc, ColorScheme scheme) =>
        scheme.Colors.Where(c => !IsHidden(doc, c.Id));

    /// <summary>Name shown next to a hidden color: the user's name, "#RRGGBB", or "Blue (Ocean)".</summary>
    public static string Title(AppStateDocument doc, string id)
    {
        if (ColorSchemes.TryFindColor(id, out var scheme, out var color))
            return ColorSchemes.DisplayName(scheme, color);
        var preset = doc.MenuColors?.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (preset is not null)
        {
            var hex = Hex(doc, id);
            return string.IsNullOrWhiteSpace(preset.Label) ? "#" + hex : preset.Label;
        }

        return id;
    }

    /// <summary>6 hex digits, or gray when the id is not a color this version knows.</summary>
    public static string Hex(AppStateDocument doc, string id)
    {
        if (ColorSchemes.TryFindColor(id, out _, out var schemeColor))
            return schemeColor.HexRgb;
        var preset = doc.MenuColors?.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (preset is not null && MenuColorHex.TryParse(preset.HexRgb, out var parsed))
            return MenuColorHex.ToHexRgb(parsed);
        if (FolderColors.TryGetColor(id, out var rgb))
            return MenuColorHex.ToHexRgb(rgb);
        return "808080";
    }
}
