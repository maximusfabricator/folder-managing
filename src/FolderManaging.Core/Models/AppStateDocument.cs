// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Core.Models;

public sealed class AppStateDocument
{
    /// <summary>2 since 2.1.0: folder ids and recent colors.</summary>
    public const int CurrentVersion = 2;

    /// <summary>How many colors the "Recent colors" rows keep.</summary>
    public const int RecentColorsLimit = 5;

    public int Version { get; set; } = CurrentVersion;

    public List<FolderStateEntry> Folders { get; set; } = new();

    /// <summary>Last used folder colors, newest first: a preset id or <c>rgb-rrggbb</c>.</summary>
    public List<string> RecentColors { get; set; } = new();

    public List<MenuColorPreset> MenuColors { get; set; } = new();

    public List<MenuTagPreset> MenuTags { get; set; } = new();

    /// <summary>
    /// <c>true</c> after the four default tags have been offered once.
    /// A missing value on an already saved file means the user already had a tag list, so defaults must not return.
    /// </summary>
    public bool? DefaultTagsSeeded { get; set; }

    /// <summary>
    /// When true, color/reset on protected folders (Program Files, etc.) immediately retries via elevated CLI
    /// without asking each time. Windows may still show one UAC prompt per elevated launch. Default: on.
    /// </summary>
    public bool AutoElevateProtectedFolders { get; set; } = true;

    /// <summary>Companion light/dark theme. Default: follow Windows.</summary>
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;

    /// <summary>
    /// UI language code (<see cref="Localization.UiLanguage.Code"/>) for the app, the installer and the Explorer menu.
    /// <c>null</c> follows the Windows display language.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>User layout of the "Folder Manager" context menu; <c>null</c> means the built-in default.</summary>
    public ContextMenuLayout? ContextMenu { get; set; }

    /// <summary>
    /// When true, we already showed the one-time tip that Explorer sorts colored folders via Tags/Title.
    /// </summary>
    public bool ColorSortTipShown { get; set; }

    public ContextMenuLayout GetContextMenuLayout() => ContextMenuLayout.Normalize(ContextMenu);

    /// <summary>Moves <paramref name="colorToken"/> to the front of <see cref="RecentColors"/>.</summary>
    public void RememberRecentColor(string colorToken)
    {
        if (string.IsNullOrWhiteSpace(colorToken))
            return;
        RecentColors ??= new List<string>();
        RecentColors.RemoveAll(c => string.Equals(c, colorToken, StringComparison.OrdinalIgnoreCase));
        RecentColors.Insert(0, colorToken.ToLowerInvariant());
        if (RecentColors.Count > RecentColorsLimit)
            RecentColors.RemoveRange(RecentColorsLimit, RecentColors.Count - RecentColorsLimit);
    }

    /// <summary>Restores app settings to defaults. Folder data, color presets and tags are kept.</summary>
    public void ResetSettingsToDefaults()
    {
        AutoElevateProtectedFolders = true;
        Theme = AppThemePreference.System;
        Language = null;
        ContextMenu = null;
    }
}
