// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>Keeps <c>state.json</c> pointing at folders the user renamed or moved.</summary>
public static class FolderRelocator
{
    /// <summary>Folders looked at per missing entry before giving up.</summary>
    const int ScanLimit = 2000;

    /// <summary>
    /// <paramref name="oldPath"/> is now <paramref name="newPath"/>: moves its entry and the entries of folders inside it.
    /// </summary>
    /// <returns>New paths of the entries that moved.</returns>
    public static IReadOnlyList<string> Relocate(AppStateDocument doc, string oldPath, string newPath)
    {
        var from = Normalize(oldPath);
        var to = Normalize(newPath);
        var moved = new List<string>();
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return moved;

        foreach (var entry in doc.Folders)
        {
            var path = Normalize(entry.Path);
            string? updated = null;
            if (string.Equals(path, from, StringComparison.OrdinalIgnoreCase))
                updated = to;
            else if (path.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                updated = to + path[from.Length..];
            if (updated is null)
                continue;
            // Another entry already has the new path (the user colored the new folder too): keep both as they are.
            if (doc.Folders.Any(f => !ReferenceEquals(f, entry) &&
                                     string.Equals(Normalize(f.Path), updated, StringComparison.OrdinalIgnoreCase)))
                continue;
            entry.Path = updated;
            moved.Add(updated);
        }

        return moved;
    }

    /// <summary>
    /// Finds colored or tagged folders that are gone from their path but whose <c>desktop.ini</c> id shows up next to it
    /// (renamed) or one level down in a neighbor (moved into a sibling folder). Missing drives are left alone.
    /// </summary>
    /// <param name="readFolderId">Our <c>FolderId</c> in a folder's <c>desktop.ini</c>, or <c>null</c>.</param>
    /// <returns>(old path, new path) pairs, already applied to <paramref name="doc"/>.</returns>
    public static IReadOnlyList<(string From, string To)> FindMoved(
        AppStateDocument doc,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> listSubdirectories,
        Func<string, string?> readFolderId)
    {
        var result = new List<(string, string)>();
        foreach (var entry in doc.Folders.Where(f => f.Id is not null && StateMigration.HasCustomization(f)).ToList())
        {
            var path = Normalize(entry.Path);
            var parent = Path.GetDirectoryName(path);
            var root = Path.GetPathRoot(path);
            if (parent is null || string.IsNullOrEmpty(root) || !directoryExists(root) || directoryExists(path) ||
                !directoryExists(parent))
                continue;

            var found = Search(parent, entry.Id!, listSubdirectories, readFolderId);
            if (found is null || doc.Folders.Any(f => string.Equals(Normalize(f.Path), found, StringComparison.OrdinalIgnoreCase)))
                continue;
            Relocate(doc, path, found);
            result.Add((path, found));
        }

        return result;
    }

    static string? Search(string parent, string id, Func<string, IEnumerable<string>> listSubdirectories, Func<string, string?> readFolderId)
    {
        var looked = 0;
        var siblings = SafeList(parent, listSubdirectories);
        foreach (var dir in siblings)
        {
            if (++looked > ScanLimit)
                return null;
            if (string.Equals(SafeRead(dir, readFolderId), id, StringComparison.OrdinalIgnoreCase))
                return Normalize(dir);
        }

        foreach (var sibling in siblings)
        {
            foreach (var dir in SafeList(sibling, listSubdirectories))
            {
                if (++looked > ScanLimit)
                    return null;
                if (string.Equals(SafeRead(dir, readFolderId), id, StringComparison.OrdinalIgnoreCase))
                    return Normalize(dir);
            }
        }

        return null;
    }

    static List<string> SafeList(string dir, Func<string, IEnumerable<string>> list)
    {
        try
        {
            return list(dir).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    static string? SafeRead(string dir, Func<string, string?> read)
    {
        try
        {
            return read(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Real file system version of <see cref="FindMoved(AppStateDocument, Func{string, bool}, Func{string, IEnumerable{string}}, Func{string, string?})"/>.</summary>
    public static IReadOnlyList<(string From, string To)> FindMoved(AppStateDocument doc) =>
        FindMoved(doc, Directory.Exists, Directory.EnumerateDirectories, FolderIconManager.TryReadFolderId);

    static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
