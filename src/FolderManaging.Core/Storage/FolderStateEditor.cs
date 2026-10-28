// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Models;

namespace FolderManaging.Core.Storage;

/// <summary>Minimal in-memory edits on top of <see cref="IFolderStateStore"/> for CLI / future services.</summary>
public sealed class FolderStateEditor
{
    private readonly IFolderStateStore _store;

    public FolderStateEditor(IFolderStateStore store) => _store = store;

    public async Task<FolderStateEntry> GetOrCreateAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var full = NormalizePath(folderPath);
        var entry = state.Folders.FirstOrDefault(f => string.Equals(NormalizePath(f.Path), full, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            return entry;
        }

        entry = new FolderStateEntry { Path = full };
        state.Folders.Add(entry);
        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public async Task UpsertTagAsync(string folderPath, string tag, CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var full = NormalizePath(folderPath);
        var entry = state.Folders.FirstOrDefault(f => string.Equals(NormalizePath(f.Path), full, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new FolderStateEntry { Path = full };
            state.Folders.Add(entry);
        }

        if (!entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            entry.Tags.Add(tag);
        }

        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes one tag; an entry left with nothing is dropped.</summary>
    public async Task RemoveTagAsync(string folderPath, string tag, CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var full = NormalizePath(folderPath);
        var entry = state.Folders.FirstOrDefault(f => string.Equals(NormalizePath(f.Path), full, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return;

        entry.Tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        PruneIfEmpty(state, entry);
        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forgets the folder's color and tags. A Quick access pin stays.</summary>
    public async Task ResetFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var full = NormalizePath(folderPath);
        var entry = state.Folders.FirstOrDefault(f => string.Equals(NormalizePath(f.Path), full, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return;

        entry.ColorToken = null;
        entry.Tags.Clear();
        PruneIfEmpty(state, entry);
        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops an entry that no longer carries a pin, a color or tags.</summary>
    private static void PruneIfEmpty(AppStateDocument state, FolderStateEntry entry)
    {
        if (!entry.IsPinned && entry.Tags.Count == 0 && string.IsNullOrWhiteSpace(entry.ColorToken))
            state.Folders.Remove(entry);
    }

    public async Task SetPinnedAsync(string folderPath, bool pinned, CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var full = NormalizePath(folderPath);
        var entry = state.Folders.FirstOrDefault(f => string.Equals(NormalizePath(f.Path), full, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new FolderStateEntry { Path = full };
            state.Folders.Add(entry);
        }

        entry.IsPinned = pinned;
        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetColorTokenAsync(string folderPath, string? colorToken, CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var full = NormalizePath(folderPath);
        var entry = state.Folders.FirstOrDefault(f => string.Equals(NormalizePath(f.Path), full, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new FolderStateEntry { Path = full };
            state.Folders.Add(entry);
        }

        entry.ColorToken = colorToken;
        if (colorToken is not null)
            state.RememberRecentColor(colorToken);
        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
