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
        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
