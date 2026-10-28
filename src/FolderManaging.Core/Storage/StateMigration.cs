// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Text.Json;
using FolderManaging.Core.Models;

namespace FolderManaging.Core.Storage;

/// <summary>Upgrades an older <c>state.json</c> in memory. Entries are never dropped, empty ones included.</summary>
public static class StateMigration
{
    public const string BackupFileName = "state.v1.backup.json";

    /// <returns><c>true</c> when the document was older and has been upgraded.</returns>
    public static bool Upgrade(AppStateDocument doc, Func<string, string?>? readFolderId = null)
    {
        if (doc.Version >= AppStateDocument.CurrentVersion)
            return false;

        doc.RecentColors ??= new List<string>();
        foreach (var entry in doc.Folders)
            entry.Tags ??= new List<string>();
        AssignMissingIds(doc, readFolderId ?? (_ => null), IdFromPath);
        doc.Version = AppStateDocument.CurrentVersion;
        return true;
    }

    /// <summary>
    /// Same id in every process that upgrades the same file before it is saved again (the shell, the tray app and
    /// the elevated worker may all read the old file).
    /// </summary>
    public static string IdFromPath(string path)
    {
        var normalized = Path.TrimEndingDirectorySeparator(path.Trim()).ToLowerInvariant();
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        return new Guid(hash.AsSpan(0, 16)).ToString("N");
    }

    /// <summary>
    /// Gives every colored or tagged entry an id: the one in its folder's <c>desktop.ini</c> when there is one
    /// (<paramref name="readFolderId"/>), else a new one.
    /// </summary>
    public static void AssignMissingIds(AppStateDocument doc, Func<string, string?> readFolderId, Func<string, string>? newId = null)
    {
        foreach (var entry in doc.Folders)
        {
            if (entry.Id is not null || !HasCustomization(entry))
                continue;
            string? existing = null;
            try
            {
                existing = readFolderId(entry.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }

            entry.Id = existing ?? newId?.Invoke(entry.Path) ?? NewId();
        }
    }

    public static bool HasCustomization(FolderStateEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.ColorToken) || entry.Tags is { Count: > 0 };

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Before the first save in the new format: copies the old file byte for byte next to it, once.
    /// </summary>
    public static void BackupIfOlder(string statePath)
    {
        try
        {
            var backup = Path.Combine(Path.GetDirectoryName(statePath)!, BackupFileName);
            if (File.Exists(backup) || !File.Exists(statePath) || ReadVersion(statePath) >= AppStateDocument.CurrentVersion)
                return;
            File.Copy(statePath, backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Diagnostics.TraceLog.Append("shell-menu.log", $"[STATE] backup before upgrade failed: {ex.Message}");
        }
    }

    public static int ReadVersion(string statePath)
    {
        using var stream = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.TryGetProperty("version", out var v) && v.TryGetInt32(out var version) ? version : 1;
    }
}
