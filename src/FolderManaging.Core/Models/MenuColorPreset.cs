// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Core.Models;

/// <summary>Folder color shown in the shell context menu and edited in Companion.</summary>
public sealed class MenuColorPreset
{
    /// <summary>Stable token (e.g. coral, ocean) used by shell verbs and <see cref="FolderStateEntry.ColorToken"/>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable caption in the menu.</summary>
    public string Label { get; set; } = "";

    /// <summary>RGB as 6 hex digits without # (e.g. FF7256).</summary>
    public string HexRgb { get; set; } = "000000";
}
