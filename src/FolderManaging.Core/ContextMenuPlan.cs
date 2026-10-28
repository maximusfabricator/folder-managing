// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Drawing;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

public enum MenuLineIcon
{
    /// <summary>The stock folder glyph.</summary>
    Default,
    /// <summary>No icon (separators and headings).</summary>
    None,
    /// <summary>The menu icon of the color id in <see cref="MenuLine.Token"/>.</summary>
    Preset,
    /// <summary>The menu icon of <see cref="MenuLine.IconColor"/>.</summary>
    Rgb,
    /// <summary>The tag badge of the tag id in <see cref="MenuLine.Token"/>, colored <see cref="MenuLine.IconColor"/>.</summary>
    Tag,
}

/// <summary>
/// One row of the "Folder Manager" submenu. <see cref="Kind"/> is the action the shell runs
/// (<c>color</c>, <c>rgb</c>, <c>recent</c>, <c>tag</c>, <c>pin</c>, <c>unpin</c>, <c>quick-color</c>, <c>reset</c>,
/// <c>settings</c>) or <c>noop</c> for a disabled separator or heading.
/// </summary>
public sealed record MenuLine(
    string VerbId,
    string Title,
    string Kind,
    string? Token = null,
    MenuLineIcon Icon = MenuLineIcon.Default,
    Color? IconColor = null,
    string? CheckedTitle = null)
{
    public bool Disabled => Kind == ContextMenuPlan.NoopKind;
}

/// <summary>
/// The rows of the "Folder Manager" submenu for a state document, used by the Explorer menu and by the preview on the
/// Context menu page. Explorer's modern menu has one submenu level, so blocks (recent colors, color schemes) are flat
/// rows; a scheme starts with a disabled heading row.
/// </summary>
public static class ContextMenuPlan
{
    public const string NoopKind = "noop";

    /// <summary>Win11 ignores ECF_ISSEPARATOR in nested modern menus. A disabled line-item is the visible stand-in.</summary>
    public const string SeparatorTitle = "──────────────";

    /// <summary>Rows shown for recently used colors (the app keeps a few more for the color wheel window).</summary>
    public const int RecentRows = 3;

    static readonly (string NameKey, string Hex)[] SystemPalette =
    {
        ("PalRed", "E53935"),
        ("PalOrange", "FB8C00"),
        ("PalYellow", "FDD835"),
        ("PalLime", "7CB342"),
        ("PalGreen", "43A047"),
        ("PalTeal", "00897B"),
        ("PalCyan", "00ACC1"),
        ("PalBlue", "1E88E5"),
        ("PalIndigo", "3949AB"),
        ("PalPurple", "8E24AA"),
        ("PalPink", "D81B60"),
        ("PalBrown", "6D4C41"),
        ("PalGray", "757575"),
    };

    /// <param name="layout">The layout to show; <c>null</c> uses the document's own.</param>
    public static IReadOnlyList<MenuLine> Build(AppStateDocument doc, ContextMenuLayout? layout = null)
    {
        layout ??= doc.GetContextMenuLayout();
        var list = new List<MenuLine>();
        var separatorIndex = 0;
        var pendingSeparator = false;

        foreach (var entry in layout.Entries)
        {
            if (!entry.Visible)
                continue;

            if (entry.Kind == ContextMenuItemKinds.Separator)
            {
                pendingSeparator = true;
                continue;
            }

            var beforeCount = list.Count;
            var label = string.IsNullOrWhiteSpace(entry.Label)
                ? ContextMenuItemKinds.DefaultLabel(entry.Kind)
                : entry.Label;

            switch (entry.Kind)
            {
                case ContextMenuItemKinds.Pin:
                    list.Add(new MenuLine("pin", UiText.Get("Pin"), "pin"));
                    list.Add(new MenuLine("unpin", UiText.Get("Unpin"), "unpin"));
                    break;

                case ContextMenuItemKinds.QuickColor:
                    list.Add(new MenuLine("quick-color", label, "quick-color"));
                    break;

                case ContextMenuItemKinds.ColorPresets:
                    AppendPresets(list, doc);
                    break;

                case ContextMenuItemKinds.ColorSchemes:
                    AppendSchemes(list, doc);
                    break;

                case ContextMenuItemKinds.RecentColors:
                    AppendRecent(list, doc);
                    break;

                case ContextMenuItemKinds.SystemPalette:
                    foreach (var (nameKey, hex) in SystemPalette)
                    {
                        MenuColorHex.TryParse(hex, out var c);
                        list.Add(new MenuLine($"palette-{hex}", $"🎨 {UiText.Get(nameKey)}", "rgb", hex, MenuLineIcon.Rgb, c));
                    }
                    break;

                case ContextMenuItemKinds.Reset:
                    list.Add(new MenuLine("reset-color", label, "reset"));
                    break;

                case ContextMenuItemKinds.Tags:
                    AppendTags(list, doc);
                    break;

                case ContextMenuItemKinds.Settings:
                    list.Add(new MenuLine("settings", label, "settings"));
                    break;
            }

            // A scheme heading already divides the menu; a separator right above it would be a second line.
            if (pendingSeparator && list.Count > beforeCount)
            {
                if (!list[beforeCount].Disabled)
                    list.Insert(beforeCount, new MenuLine($"sep-{separatorIndex++}", SeparatorTitle, NoopKind, Icon: MenuLineIcon.None));
                pendingSeparator = false;
            }
        }

        return list;
    }

    static void AppendPresets(List<MenuLine> list, AppStateDocument doc)
    {
        foreach (var row in doc.MenuColors.Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Id)))
            list.Add(new MenuLine($"color-{row.Id}", ColorTitle(row), "color", row.Id, MenuLineIcon.Preset));
    }

    static void AppendSchemes(List<MenuLine> list, AppStateDocument doc)
    {
        foreach (var scheme in ColorSchemes.Enabled(doc))
        {
            list.Add(new MenuLine($"scheme-{scheme.Id}", $"── {scheme.Name} ──", NoopKind, Icon: MenuLineIcon.None));
            foreach (var color in scheme.Colors)
            {
                list.Add(new MenuLine($"color-{color.Id}", UiText.Format("ColorLine", UiText.Get(color.NameKey)),
                    "color", color.Id, MenuLineIcon.Preset));
            }
        }
    }

    static void AppendRecent(List<MenuLine> list, AppStateDocument doc)
    {
        foreach (var token in (doc.RecentColors ?? []).Take(RecentRows))
        {
            if (token.StartsWith("rgb-", StringComparison.OrdinalIgnoreCase))
            {
                if (!MenuColorHex.TryParse(token[4..], out var rgb))
                    continue;
                list.Add(new MenuLine($"recent-{token}", UiText.Format("RecentLine", "#" + MenuColorHex.ToHexRgb(rgb)),
                    "recent", token, MenuLineIcon.Rgb, rgb));
                continue;
            }

            var name = ColorName(doc, token);
            if (name is null)
                continue;
            list.Add(new MenuLine($"recent-{token}", UiText.Format("RecentLine", name), "recent", token, MenuLineIcon.Preset));
        }
    }

    static void AppendTags(List<MenuLine> list, AppStateDocument doc)
    {
        foreach (var t in doc.MenuTags.OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(t.Id))
                continue;
            Color? badge = MenuColorHex.TryParse(t.HexRgb, out var c) ? c : null;
            list.Add(new MenuLine($"tag-{t.Id}", UiText.Format("TagLine", t.Label), "tag", t.Id, MenuLineIcon.Tag, badge,
                CheckedTitle: UiText.Format("TagLineOn", t.Label)));
        }
    }

    /// <summary>
    /// The name of a color id outside its own block: the user's name or hex code for "My colors", "Blue (Ocean)" for
    /// a scheme color. <c>null</c> for an id that no longer exists.
    /// </summary>
    public static string? ColorName(AppStateDocument doc, string colorId)
    {
        if (ColorSchemes.TryFindColor(colorId, out var scheme, out var schemeColor))
            return ColorSchemes.DisplayName(scheme, schemeColor);
        var preset = doc.MenuColors.FirstOrDefault(c => string.Equals(c.Id, colorId, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
            return null;
        return string.IsNullOrWhiteSpace(preset.Label) ? "#" + HexForDisplay(preset.HexRgb) : preset.Label;
    }

    static string ColorTitle(MenuColorPreset row)
    {
        if (!string.IsNullOrWhiteSpace(row.Label))
            return UiText.Format("ColorLine", row.Label);
        var hex = HexForDisplay(row.HexRgb);
        return hex.Length > 0 ? UiText.Format("ColorHex", hex) : UiText.Get("ColorPlain");
    }

    static string HexForDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var s = raw.Trim().TrimStart('#');
        return s.Length == 6 ? s.ToUpperInvariant() : string.Empty;
    }
}
