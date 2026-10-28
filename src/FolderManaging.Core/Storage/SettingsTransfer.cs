// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Text.Json;
using FolderManaging.Core.Models;

namespace FolderManaging.Core.Storage;

/// <summary>What an exported settings file holds. Quick access pins stay out: they belong to Windows.</summary>
public sealed class SettingsExport
{
    public const string FormatName = "FolderManaging.Settings";

    /// <summary><see cref="FormatName"/> in our files; a file without it is something else.</summary>
    public string? Format { get; set; }

    public int Version { get; set; } = 1;

    public string? AppVersion { get; set; }

    public List<MenuColorPreset> MenuColors { get; set; } = new();

    public List<MenuTagPreset> MenuTags { get; set; } = new();

    /// <summary>Built-in color schemes that are on; <c>null</c> in files from 2.1.0, which leave the current choice alone.</summary>
    public List<string>? EnabledColorSchemes { get; set; }

    public ContextMenuLayout? ContextMenu { get; set; }

    public AppThemePreference? Theme { get; set; }

    public string? Language { get; set; }

    public bool? AutoElevateProtectedFolders { get; set; }

    public List<ExportedFolder> Folders { get; set; } = new();
}

public sealed class ExportedFolder
{
    public string Path { get; set; } = "";

    public string? ColorToken { get; set; }

    public List<string> Tags { get; set; } = new();
}

/// <param name="Folders">Folders whose color or tags changed and need their <c>desktop.ini</c> written.</param>
/// <param name="MissingFolders">Folders in the file that do not exist on this PC (skipped).</param>
public sealed record SettingsImportResult(int Colors, int Tags, IReadOnlyList<string> Folders, IReadOnlyList<string> MissingFolders);

/// <summary>Export and import of colors, tags, the menu layout, options and the folder list.</summary>
public static class SettingsTransfer
{
    public static string Export(AppStateDocument doc, string? appVersion = null)
    {
        var export = new SettingsExport
        {
            Format = SettingsExport.FormatName,
            AppVersion = appVersion,
            MenuColors = doc.MenuColors.Select(c => new MenuColorPreset { Id = c.Id, Label = c.Label, HexRgb = c.HexRgb }).ToList(),
            MenuTags = doc.MenuTags.Select(t => new MenuTagPreset { Id = t.Id, Label = t.Label, HexRgb = t.HexRgb }).ToList(),
            EnabledColorSchemes = (doc.EnabledColorSchemes ?? []).ToList(),
            ContextMenu = doc.ContextMenu,
            Theme = doc.Theme,
            Language = doc.Language,
            AutoElevateProtectedFolders = doc.AutoElevateProtectedFolders,
            Folders = doc.Folders
                .Where(StateMigration.HasCustomization)
                .Select(f => new ExportedFolder { Path = f.Path, ColorToken = f.ColorToken, Tags = f.Tags.ToList() })
                .ToList(),
        };
        return JsonSerializer.Serialize(export, JsonFolderStateStore.SerializerOptions);
    }

    /// <exception cref="InvalidDataException">Not a Folder Managing settings file.</exception>
    public static SettingsExport Parse(string json)
    {
        SettingsExport? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SettingsExport>(json, JsonFolderStateStore.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        if (parsed is null || !string.Equals(parsed.Format, SettingsExport.FormatName, StringComparison.Ordinal))
            throw new InvalidDataException("Not a Folder Managing settings file.");
        parsed.MenuColors ??= new();
        parsed.MenuTags ??= new();
        parsed.Folders ??= new();
        return parsed;
    }

    /// <summary>
    /// Merges <paramref name="import"/> into <paramref name="doc"/>: colors and tags by id (same id → the file wins),
    /// menu layout and options replaced, folders that exist here take the file's color and tags. Nothing is removed.
    /// </summary>
    public static SettingsImportResult Apply(AppStateDocument doc, SettingsExport import, Func<string, bool> folderExists)
    {
        var colors = 0;
        foreach (var color in import.MenuColors.Where(c => !string.IsNullOrWhiteSpace(c.Id) && MenuColorHex.TryParse(c.HexRgb, out _)))
        {
            var existing = doc.MenuColors.FirstOrDefault(c => string.Equals(c.Id, color.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                doc.MenuColors.Add(new MenuColorPreset { Id = color.Id, Label = color.Label ?? "", HexRgb = color.HexRgb });
            else
            {
                existing.Label = color.Label ?? "";
                existing.HexRgb = color.HexRgb;
            }

            colors++;
        }

        var tags = 0;
        foreach (var tag in import.MenuTags.Where(t => !string.IsNullOrWhiteSpace(t.Id)))
        {
            var existing = doc.MenuTags.FirstOrDefault(t => string.Equals(t.Id, tag.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                doc.MenuTags.Add(new MenuTagPreset { Id = tag.Id, Label = tag.Label ?? tag.Id, HexRgb = tag.HexRgb });
            else
            {
                existing.Label = tag.Label ?? existing.Label;
                existing.HexRgb = tag.HexRgb;
            }

            tags++;
        }

        if (import.EnabledColorSchemes is not null)
            doc.EnabledColorSchemes = import.EnabledColorSchemes.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (import.ContextMenu is not null)
            doc.ContextMenu = import.ContextMenu;
        if (import.Theme is { } theme)
            doc.Theme = theme;
        doc.Language = import.Language;
        if (import.AutoElevateProtectedFolders is { } elevate)
            doc.AutoElevateProtectedFolders = elevate;

        var changed = new List<string>();
        var missing = new List<string>();
        foreach (var folder in import.Folders.Where(f => !string.IsNullOrWhiteSpace(f.Path)))
        {
            string full;
            try
            {
                full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                missing.Add(folder.Path);
                continue;
            }

            if (!folderExists(full))
            {
                missing.Add(full);
                continue;
            }

            var knownTags = (folder.Tags ?? new()).Where(t => doc.MenuTags.Any(m => string.Equals(m.Id, t, StringComparison.OrdinalIgnoreCase))).ToList();
            var entry = doc.Folders.FirstOrDefault(f => SamePath(f.Path, full));
            if (entry is null)
            {
                entry = new FolderStateEntry { Path = full };
                doc.Folders.Add(entry);
            }
            else if (string.Equals(entry.ColorToken, folder.ColorToken, StringComparison.OrdinalIgnoreCase) &&
                     entry.Tags.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(knownTags.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.ColorToken = string.IsNullOrWhiteSpace(folder.ColorToken) ? null : folder.ColorToken;
            entry.Tags = knownTags;
            changed.Add(full);
        }

        return new SettingsImportResult(colors, tags, changed, missing);
    }

    static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), b, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
