// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Core.Models;

using FolderManaging.Core.Localization;

/// <summary>Building blocks the user can arrange inside the "Folder Manager" context-menu entry.</summary>
public static class ContextMenuItemKinds
{
    public const string Pin = "pin";
    public const string QuickColor = "quick-color";
    public const string ColorPresets = "color-presets";
    public const string ColorSchemes = "color-schemes";
    public const string RecentColors = "recent-colors";
    public const string SystemPalette = "system-palette";
    public const string Reset = "reset";
    public const string Tags = "tags";
    public const string Settings = "settings";
    public const string Separator = "separator";

    public static readonly IReadOnlyList<string> Singletons =
        new[] { Pin, QuickColor, RecentColors, ColorPresets, ColorSchemes, SystemPalette, Reset, Tags, Settings };

    /// <summary>Kinds that render one menu line and therefore accept a custom label.</summary>
    public static bool SupportsLabel(string kind) => kind is QuickColor or Reset or Settings;

    public static string DefaultLabel(string kind) => kind switch
    {
        QuickColor => UiText.Get("LabelQuick"),
        Reset => UiText.Get("LabelReset"),
        Settings => UiText.Get("LabelSettings"),
        _ => string.Empty,
    };

    public static string DisplayName(string kind) => kind switch
    {
        Pin => UiText.Get("KindPin"),
        QuickColor => UiText.Get("KindQuick"),
        RecentColors => UiText.Get("KindRecent"),
        ColorPresets => UiText.Get("KindPresets"),
        ColorSchemes => UiText.Get("KindSchemes"),
        SystemPalette => UiText.Get("KindPalette"),
        Reset => UiText.Get("KindReset"),
        Tags => UiText.Get("KindTags"),
        Settings => UiText.Get("KindSettings"),
        Separator => UiText.Get("KindSeparator"),
        _ => kind,
    };
}

public sealed class ContextMenuEntry
{
    public string Kind { get; set; } = ContextMenuItemKinds.Separator;

    public bool Visible { get; set; } = true;

    /// <summary>Optional custom text (only for <see cref="ContextMenuItemKinds.SupportsLabel"/> kinds).</summary>
    public string? Label { get; set; }

    public ContextMenuEntry Clone() => new() { Kind = Kind, Visible = Visible, Label = Label };
}

/// <summary>User-editable layout of the single "Folder Manager" context-menu entry.</summary>
public sealed class ContextMenuLayout
{
    public const string DefaultRootTitle = "Folder Manager";

    public string RootTitle { get; set; } = DefaultRootTitle;

    public List<ContextMenuEntry> Entries { get; set; } = new();

    public static ContextMenuLayout CreateDefault() => new()
    {
        RootTitle = DefaultRootTitle,
        Entries =
        {
            new() { Kind = ContextMenuItemKinds.Pin },
            new() { Kind = ContextMenuItemKinds.Separator },
            new() { Kind = ContextMenuItemKinds.QuickColor },
            new() { Kind = ContextMenuItemKinds.RecentColors },
            new() { Kind = ContextMenuItemKinds.ColorPresets },
            new() { Kind = ContextMenuItemKinds.ColorSchemes },
            new() { Kind = ContextMenuItemKinds.SystemPalette, Visible = false },
            new() { Kind = ContextMenuItemKinds.Separator },
            new() { Kind = ContextMenuItemKinds.Reset },
            new() { Kind = ContextMenuItemKinds.Separator },
            new() { Kind = ContextMenuItemKinds.Tags },
            new() { Kind = ContextMenuItemKinds.Separator },
            new() { Kind = ContextMenuItemKinds.Settings },
        },
    };

    public ContextMenuLayout Clone() => new()
    {
        RootTitle = RootTitle,
        Entries = Entries.Select(e => e.Clone()).ToList(),
    };

    /// <summary>
    /// Returns a usable layout: unknown kinds dropped, duplicate singletons removed, missing singletons appended
    /// with their default visibility (so new features show up after an update without wiping the user's order).
    /// </summary>
    public static ContextMenuLayout Normalize(ContextMenuLayout? source)
    {
        if (source is null)
            return CreateDefault();

        var defaults = CreateDefault();
        var result = new ContextMenuLayout
        {
            RootTitle = string.IsNullOrWhiteSpace(source.RootTitle) ? DefaultRootTitle : source.RootTitle.Trim(),
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in source.Entries ?? new List<ContextMenuEntry>())
        {
            if (e is null || string.IsNullOrWhiteSpace(e.Kind))
                continue;
            var kind = e.Kind.Trim().ToLowerInvariant();
            if (kind == ContextMenuItemKinds.Separator)
            {
                // Keep at most one separator in a row. Leading/trailing are OK (shell ignores orphan edges).
                if (result.Entries.Count > 0 && result.Entries[^1].Kind == ContextMenuItemKinds.Separator)
                    continue;
                result.Entries.Add(new ContextMenuEntry { Kind = ContextMenuItemKinds.Separator, Visible = true });
                continue;
            }

            if (!ContextMenuItemKinds.Singletons.Contains(kind) || !seen.Add(kind))
                continue;

            result.Entries.Add(new ContextMenuEntry
            {
                Kind = kind,
                Visible = e.Visible,
                Label = ContextMenuItemKinds.SupportsLabel(kind) && !string.IsNullOrWhiteSpace(e.Label) ? e.Label.Trim() : null,
            });
        }

        // Do NOT strip trailing separators here — "Add separator" appends at the end; removing them
        // made the button look broken. MenuBuilder already ignores a trailing pending separator.

        // A block added in a newer version goes right after its neighbor from the default layout.
        for (var i = 0; i < defaults.Entries.Count; i++)
        {
            var d = defaults.Entries[i];
            if (d.Kind == ContextMenuItemKinds.Separator || seen.Contains(d.Kind))
                continue;
            var at = result.Entries.Count;
            for (var j = i - 1; j >= 0; j--)
            {
                var before = result.Entries.FindIndex(e => e.Kind == defaults.Entries[j].Kind && e.Kind != ContextMenuItemKinds.Separator);
                if (before < 0)
                    continue;
                at = before + 1;
                break;
            }

            result.Entries.Insert(at, d.Clone());
            seen.Add(d.Kind);
        }

        return result;
    }
}
