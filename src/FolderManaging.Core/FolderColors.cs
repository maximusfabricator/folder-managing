// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Drawing;

namespace FolderManaging.Core;

/// <summary>
/// Built-in preset ids and RGB values shipped as defaults. The user owns the names in <c>state.json</c>;
/// these are intentionally generic (no quirky branded names) so the UI can display the hex code instead.
/// </summary>
public static class FolderColors
{
    public const string Red = "red";
    public const string Blue = "blue";
    public const string Green = "green";
    public const string Purple = "purple";

    public static readonly IReadOnlyDictionary<string, Color> PresetRgb = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
    {
        [Red] = Color.FromArgb(255, 114, 86),
        [Blue] = Color.FromArgb(0, 122, 204),
        [Green] = Color.FromArgb(72, 199, 142),
        [Purple] = Color.FromArgb(142, 68, 173),
    };

    /// <summary>Old branded ids that may still appear in older <c>state.json</c> documents and folder customizations.</summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyIdMigration = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["coral"] = Red,
        ["ocean"] = Blue,
        ["mint"] = Green,
        ["plum"] = Purple,
    };

    public static bool TryGetColor(string colorId, out Color color) =>
        PresetRgb.TryGetValue(colorId, out color);
}
