// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Core.Models;

/// <summary>Per-folder metadata persisted for tagging, pinning, and (later) color tokens.</summary>
public sealed class FolderStateEntry
{
    public string Path { get; set; } = "";

    public List<string> Tags { get; set; } = new();

    public bool IsPinned { get; set; }

    /// <summary>Optional logical color name (Phase 3).</summary>
    public string? ColorToken { get; set; }

    /// <summary>
    /// Also written into the folder's <c>desktop.ini</c> (<c>FolderId=</c>), so a renamed or moved folder can be found
    /// again. Set when the folder first gets a color or tags.
    /// </summary>
    public string? Id { get; set; }
}
