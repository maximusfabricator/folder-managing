// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Core.Models;

/// <summary>Tag preset shown in the shell context menu (and as a colored badge on the folder .ico).</summary>
public sealed class MenuTagPreset
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>Badge color on the folder icon (RRGGBB). Empty = default blue.</summary>
    public string HexRgb { get; set; } = "0078D4";
}
