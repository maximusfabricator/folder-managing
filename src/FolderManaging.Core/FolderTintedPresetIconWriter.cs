// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Interop;
using FolderManaging.Core.Storage;
using FolderManaging.Core.Tinting;

namespace FolderManaging.Core;

/// <summary>
/// Builds tinted folder .ico files from the <b>system stock folder icon</b> (<c>SHGetStockIconInfo</c> / SIID_FOLDER),
/// applies grayscale + RGB tint via <see cref="ColorMatrix"/> (alpha preserved), and writes a multi-resolution PNG-based ICO.
/// Filenames include the HEX so Explorer (and our menu) cannot show a stale tint after the user edits a color.
/// </summary>
public static class FolderTintedPresetIconWriter
{
    const string StyleMarkerFileName = ".folder-managing-icon-style";
    /// <summary>v4: stronger tint + hex in filename for reactive menu icons.</summary>
    internal const string CurrentIconStyle = "siid-folder-grayscale-pngico-v5-tagbadge";

    /// <summary>Minimum plausible size for our multi-frame PNG ICO (avoids treating corrupt/tiny files as cache hits).</summary>
    internal const int MinCachedIcoBytes = 4000;

    /// <summary>Writes built-in preset icons for their current built-in RGB values.</summary>
    public static void WriteAllPresets(string? outputDirectory = null, bool forceRegenerate = false)
    {
        var presets = FolderColors.PresetRgb
            .Select(p => new KeyValuePair<string, Color>(p.Key, p.Value));
        WritePresetSet(presets, outputDirectory, forceRegenerate);
    }

    /// <summary>Writes one ICO per <paramref name="presets"/> entry keyed as <c>{id}-{hex}.ico</c>.</summary>
    public static void WritePresetSet(
        IEnumerable<KeyValuePair<string, Color>> presets,
        string? outputDirectory = null,
        bool forceRegenerate = false)
    {
        var dir = ResolveOutputDirectory(outputDirectory);
        Directory.CreateDirectory(dir);

        Bitmap? stock = null;
        try
        {
            foreach (var (id, color) in presets)
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var hex = MenuColorHex.ToHexRgb(color);
                var path = Path.Combine(dir, GetPresetIconFileName(id, hex));
                if (!forceRegenerate && ShouldReuseCachedIco(path, styleMarkerMatches: true))
                {
                    ClickTrace.Step($"icon-cache HIT id={id} hex={hex} path='{path}'");
                    continue;
                }

                stock ??= CaptureFolderStockBitmapMaster(256);
                WriteTintedIco(path, stock, color);
                ClickTrace.Step($"icon-cache WRITE id={id} hex={hex} path='{path}' bytes={SafeLen(path)}");
            }

            WriteStyleMarker(dir);
            TryPruneStalePresetIcons(dir, presets);
        }
        finally
        {
            stock?.Dispose();
        }
    }

    /// <summary>(Re)writes a single preset's ICO for the exact HEX; returns the absolute path.</summary>
    public static string WriteSinglePreset(string presetId, Color color, string? outputDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            throw new ArgumentException("Preset id is required.", nameof(presetId));

        var dir = ResolveOutputDirectory(outputDirectory);
        Directory.CreateDirectory(dir);

        var hex = MenuColorHex.ToHexRgb(color);
        var path = Path.Combine(dir, GetPresetIconFileName(presetId, hex));
        using var stock = CaptureFolderStockBitmapMaster(256);
        WriteTintedIco(path, stock, color);
        WriteStyleMarker(dir);
        ClickTrace.Step($"WriteSinglePreset id={presetId} hex={hex} path='{path}' bytes={SafeLen(path)}");

        // Drop older hex variants for the same id so the Icons folder stays small.
        TryDeleteOtherHexVariants(dir, presetId, keepPath: path);
        return Path.GetFullPath(path);
    }

    /// <summary>Absolute path for a preset at a specific HEX (does not create the file).</summary>
    public static string GetPresetIconPath(string presetId, string hexRgb, string? outputDirectory = null)
    {
        var dir = ResolveOutputDirectory(outputDirectory);
        return Path.GetFullPath(Path.Combine(dir, GetPresetIconFileName(presetId, hexRgb)));
    }

    /// <summary>Sanitizes a preset id + hex into a filesystem-safe ICO filename.</summary>
    public static string GetPresetIconFileName(string presetId, string hexRgb)
    {
        var sanitized = SanitizePresetIdForFilename(presetId);
        var hex = NormalizeHex(hexRgb);
        return $"{sanitized}-{hex}.ico";
    }

    /// <summary>Legacy overload — without hex; prefers looking up current menu tint when possible.</summary>
    [Obsolete("Use GetPresetIconFileName(presetId, hexRgb) so Explorer cannot cache a stale tint.")]
    public static string GetPresetIconFileName(string presetId) =>
        GetPresetIconFileName(presetId, "000000");

    static string NormalizeHex(string? hexRgb)
    {
        if (string.IsNullOrWhiteSpace(hexRgb))
            return "000000";
        var s = hexRgb.Trim().TrimStart('#').ToLowerInvariant();
        return s.Length == 6 ? s : "000000";
    }

    static string SanitizePresetIdForFilename(string presetId)
    {
        var s = presetId.Trim().ToLowerInvariant();
        var invalid = Path.GetInvalidFileNameChars();
        var chars = s.Where(c => Array.IndexOf(invalid, c) < 0).ToArray();
        var safe = chars.Length == 0 ? "preset" : new string(chars);
        return safe.Length > 48 ? safe.Substring(0, 48) : safe;
    }

    static string ResolveOutputDirectory(string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            return Path.GetFullPath(outputDirectory!);

        var env = Environment.GetEnvironmentVariable("FOLDERMANAGING_ICONS_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(
                env.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return AppPaths.GeneratedIconsDirectory;
    }

    static Bitmap CaptureFolderStockBitmapMaster(int targetSize)
    {
        try
        {
            return StockShellIcon.CaptureFolderStockBitmap(targetSize);
        }
        catch
        {
            return CaptureFolderBitmapViaTempShGetFileInfo(targetSize);
        }
    }

    static Bitmap CaptureFolderBitmapViaTempShGetFileInfo(int targetSize)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "FolderManaging-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var shfi = new ShellFileInfo.SHFILEINFO();
            var flags = ShellFileInfo.ShgfiIcon | ShellFileInfo.ShgfiShelliconsize | ShellFileInfo.ShgfiAddoverlays;
            var r = ShellFileInfo.SHGetFileInfoW(tmp, 0, ref shfi, (uint)Marshal.SizeOf<ShellFileInfo.SHFILEINFO>(), flags);
            if (r == 0 || shfi.hIcon == 0)
                throw new InvalidOperationException("SHGetFileInfo could not obtain a shell folder icon.");

            try
            {
                using var ico = Icon.FromHandle(shfi.hIcon);
                using var src = ico.ToBitmap();
                var bmp = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, targetSize, targetSize));
                }

                return bmp;
            }
            finally
            {
                Win32Shell.DestroyIcon(shfi.hIcon);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tmp, recursive: true);
            }
            catch
            {
                // best-effort
            }
        }
    }

    public static string WriteTintedRgbIcon(Color tint, string? outputDirectory = null, bool forceRegenerate = false)
    {
        var dir = ResolveOutputDirectory(outputDirectory);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"rgb-{tint.R:x2}{tint.G:x2}{tint.B:x2}.ico");

        if (!forceRegenerate && ShouldReuseCachedIco(path, styleMarkerMatches: true))
            return path;

        using var stock = CaptureFolderStockBitmapMaster(256);
        WriteTintedIco(path, stock, tint);
        WriteStyleMarker(dir);
        return path;
    }

    /// <summary>
    /// Writes a multi-size folder ICO: optional RGB tint, optional tag badge in the corner.
    /// Used for per-folder versioned icons (color and/or tags).
    /// </summary>
    public static void WriteCompositeIcon(string outputPath, Color? folderTint, Color? tagBadge)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var stock = CaptureFolderStockBitmapMaster(256);
        using var base256 = folderTint is { } tint
            ? FolderIconTint.ApplyRgbTint(stock, tint)
            : (Bitmap)stock.Clone();

        // Draw the badge per size so 16/32px stay crisp (not a blurry downscale of a 256 badge).
        var owned = new List<Bitmap>();
        try
        {
            var frames = new List<(int LogicalSize, Bitmap Bitmap)>();
            foreach (var s in new[] { 256, 64, 48, 32, 16 })
            {
                var frame = s == 256
                    ? (Bitmap)base256.Clone()
                    : FolderIconTint.ResizeSquare(base256, s);
                owned.Add(frame);
                if (tagBadge is { } badge)
                    FolderIconTint.DrawTagBadge(frame, badge);
                frames.Add((s, frame));
            }

            PngIcoEncoder.Save(outputPath, frames);
        }
        finally
        {
            foreach (var b in owned)
                b.Dispose();
        }

        WriteStyleMarker(ResolveOutputDirectory(null));
        ClickTrace.Step(
            $"WriteCompositeIcon tint={(folderTint is { } t ? MenuColorHex.ToHexRgb(t) : "stock")} " +
            $"badge={(tagBadge is { } b2 ? MenuColorHex.ToHexRgb(b2) : "none")} path='{outputPath}'");
    }

    internal static bool ShouldReuseCachedIco(string icoPath, bool styleMarkerMatches)
    {
        if (!styleMarkerMatches || !File.Exists(icoPath))
            return false;
        try
        {
            return new FileInfo(icoPath).Length >= MinCachedIcoBytes;
        }
        catch
        {
            return false;
        }
    }

    static void WriteStyleMarker(string dir)
    {
        try
        {
            File.WriteAllText(Path.Combine(dir, StyleMarkerFileName), CurrentIconStyle);
        }
        catch
        {
            // ignore
        }
    }

    static void WriteTintedIco(string outputPath, Bitmap stock256, Color tint)
    {
        using var tinted256 = FolderIconTint.ApplyRgbTint(stock256, tint);
        var extras = new List<Bitmap>();
        try
        {
            var frames = new List<(int LogicalSize, Bitmap Bitmap)> { (256, tinted256) };
            foreach (var s in new[] { 64, 48, 32, 16 })
            {
                var b = FolderIconTint.ResizeSquare(tinted256, s);
                extras.Add(b);
                frames.Add((s, b));
            }

            PngIcoEncoder.Save(outputPath, frames);
        }
        finally
        {
            foreach (var b in extras)
                b.Dispose();
        }
    }

    static void TryPruneStalePresetIcons(string dir, IEnumerable<KeyValuePair<string, Color>> keep)
    {
        try
        {
            var keepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, color) in keep)
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                keepNames.Add(GetPresetIconFileName(id, MenuColorHex.ToHexRgb(color)));
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.ico"))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("rgb-", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (keepNames.Contains(name))
                    continue;
                // Only prune id-hex style names (contain a dash before .ico)
                if (!name.Contains('-', StringComparison.Ordinal))
                {
                    // legacy id-only .ico from v3 — delete
                    try { File.Delete(file); } catch { /* ignore */ }
                    continue;
                }

                var idPart = name.Split('-')[0];
                if (keep.Any(p => string.Equals(SanitizePresetIdForFilename(p.Key), idPart, StringComparison.OrdinalIgnoreCase)))
                {
                    try { File.Delete(file); } catch { /* ignore */ }
                }
            }
        }
        catch
        {
            // best-effort
        }
    }

    static void TryDeleteOtherHexVariants(string dir, string presetId, string keepPath)
    {
        try
        {
            var prefix = SanitizePresetIdForFilename(presetId) + "-";
            var keep = Path.GetFullPath(keepPath);
            foreach (var file in Directory.EnumerateFiles(dir, prefix + "*.ico"))
            {
                if (string.Equals(Path.GetFullPath(file), keep, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { File.Delete(file); } catch { /* ignore */ }
            }

            // legacy id-only
            var legacy = Path.Combine(dir, SanitizePresetIdForFilename(presetId) + ".ico");
            if (File.Exists(legacy))
            {
                try { File.Delete(legacy); } catch { /* ignore */ }
            }
        }
        catch
        {
            // ignore
        }
    }

    static long SafeLen(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return -1; }
    }

    internal static bool IsCurrentStyleMarkerPresent(string iconsDirectory)
    {
        try
        {
            var p = Path.Combine(iconsDirectory, StyleMarkerFileName);
            if (!File.Exists(p))
                return false;
            return string.Equals(File.ReadAllText(p).Trim(), CurrentIconStyle, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
