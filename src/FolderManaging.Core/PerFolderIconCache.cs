// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>
/// Writes a per-folder, per-apply versioned copy of a tinted folder icon. Explorer caches folder icons
/// <b>by file path</b>, so reusing <c>coral.ico</c> across changes leaves the previous render visible.
/// Each successful <see cref="Apply"/> writes a fresh path under
/// <c>%LocalAppData%\FolderManaging\Icons\Folders\&lt;hash&gt;-&lt;tag&gt;-&lt;ticks&gt;.ico</c>
/// and best-effort deletes earlier versioned files for the same folder hash.
/// </summary>
internal static class PerFolderIconCache
{
    internal const string FoldersSubdirectory = "Folders";

    /// <summary>Writes a fresh copy of <paramref name="sourceCachedIcoPath"/> under a unique per-folder path and returns the new path.</summary>
    internal static string WriteVersionedCopy(string folderFullPath, string sourceCachedIcoPath, string colorTag)
    {
        if (string.IsNullOrWhiteSpace(folderFullPath))
            throw new ArgumentException("Folder path is required.", nameof(folderFullPath));
        if (string.IsNullOrWhiteSpace(sourceCachedIcoPath))
            throw new ArgumentException("Source icon path is required.", nameof(sourceCachedIcoPath));
        if (!File.Exists(sourceCachedIcoPath))
            throw new FileNotFoundException("Source cached icon missing.", sourceCachedIcoPath);

        var dest = AllocateVersionedPath(folderFullPath, colorTag);
        File.Copy(sourceCachedIcoPath, dest, overwrite: true);
        File.SetLastWriteTimeUtc(dest, DateTime.UtcNow);
        return dest;
    }

    /// <summary>Unique per-folder .ico path under the icon cache (caller writes the file).</summary>
    internal static string AllocateVersionedPath(string folderFullPath, string colorTag)
    {
        if (string.IsNullOrWhiteSpace(folderFullPath))
            throw new ArgumentException("Folder path is required.", nameof(folderFullPath));

        var dir = EnsureFolderIconCacheDir();
        var hash = ComputeFolderHash(folderFullPath);
        var safeTag = SanitizeTag(colorTag);
        var ticks = DateTime.UtcNow.Ticks;
        return Path.GetFullPath(Path.Combine(dir, $"{hash}-{safeTag}-{ticks:x16}.ico"));
    }

    /// <summary>Previous versions kept on disk: Explorer may still resolve the old IconResource for a moment after a change.</summary>
    const int KeepPreviousVersions = 2;

    /// <summary>
    /// Best-effort delete of older <c>&lt;hash&gt;-*.ico</c> for <paramref name="folderFullPath"/>.
    /// With a <paramref name="keepPath"/> the newest <see cref="KeepPreviousVersions"/> other files survive; without it, all are removed.
    /// </summary>
    internal static void DeleteOldVersions(string folderFullPath, string? keepPath)
    {
        var dir = EnsureFolderIconCacheDir();
        var hash = ComputeFolderHash(folderFullPath);

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, $"{hash}-*.ico");
        }
        catch
        {
            return;
        }

        var keep = string.IsNullOrEmpty(keepPath) ? null : Path.GetFullPath(keepPath);
        var candidates = files
            .Where(f => keep is null || !string.Equals(Path.GetFullPath(f), keep, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(keep is null ? 0 : KeepPreviousVersions)
            .ToList();
        foreach (var f in candidates)
        {
            try
            {
                File.Delete(f);
            }
            catch
            {
                // Explorer / icon cache may still hold a handle — ignore.
            }
        }
    }

    /// <summary>Color tag (preset id + hex, e.g. <c>red-ffb356</c>).</summary>
    internal static string TagForPresetId(string colorId, string? hexRgb = null)
    {
        var id = SanitizeTag(string.IsNullOrWhiteSpace(colorId) ? "preset" : colorId.ToLowerInvariant());
        if (string.IsNullOrWhiteSpace(hexRgb))
            return id;
        var hex = hexRgb.Trim().TrimStart('#').ToLowerInvariant();
        if (hex.Length != 6)
            return id;
        return SanitizeTag($"{id}-{hex}");
    }

    /// <summary>Color tag for an arbitrary RGB tint.</summary>
    internal static string TagForRgb(Color tint) =>
        $"rgb{tint.R:x2}{tint.G:x2}{tint.B:x2}";

    /// <summary>True when <paramref name="iconPath"/> exists and was written for this folder with <paramref name="colorTag"/>.</summary>
    internal static bool IsCurrentIcon(string iconPath, string folderFullPath, string colorTag)
    {
        var prefix = $"{ComputeFolderHash(folderFullPath)}-{SanitizeTag(colorTag)}-";
        return Path.GetFileName(iconPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(iconPath);
    }

    static string EnsureFolderIconCacheDir()
    {
        var dir = Path.Combine(AppPaths.GeneratedIconsDirectory, FoldersSubdirectory);
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string ComputeFolderHash(string folderFullPath)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderFullPath))
            .ToLowerInvariant();
        Span<byte> hash = stackalloc byte[32];
        var bytes = Encoding.UTF8.GetBytes(normalized);
        SHA256.HashData(bytes, hash);

        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
            sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    static string SanitizeTag(string tag)
    {
        var chars = tag.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray();
        var s = chars.Length == 0 ? "x" : new string(chars);
        return s.Length > 24 ? s.Substring(0, 24) : s;
    }
}
