using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Interop;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>
/// Applies a folder tint by writing <c>desktop.ini</c> with <c>IconResource</c> pointing to a central .ico
/// under <see cref="AppPaths.GeneratedIconsDirectory"/> (never copies icons into the target folder).
/// </summary>
public static class FolderIconManager
{
    const string DesktopIniFileName = "desktop.ini";

    /// <summary>UTF-16 LE with BOM — required for Explorer to reliably parse <c>desktop.ini</c>.</summary>
    static readonly Encoding DesktopIniEncoding = Encoding.Unicode;

    /// <summary>Marker inside <c>desktop.ini</c> so we only delete our own customizations.</summary>
    public const string ManagedDesktopIniMarker = "FolderManaging-managed=1";

    /// <summary>Comment carrying the pre-existing (system/user) <c>desktop.ini</c> as Base64 so reset can restore it verbatim.</summary>
    const string OriginalIniPrefix = "; FolderManaging-original-b64=";

    /// <summary>Keys we own in <c>[.ShellClassInfo]</c>; every other line of a pre-existing ini is carried over.</summary>
    static readonly string[] OwnedShellClassInfoKeys = ["IconResource", "IconFile", "IconIndex", "ConfirmFileOp", "NoSharing"];

    /// <summary>Creates built-in preset .ico files under the user icon cache when missing, tiny, or style marker mismatch.</summary>
    public static void EnsurePresetIcons() =>
        FolderTintedPresetIconWriter.WriteAllPresets(AppPaths.GeneratedIconsDirectory);

    /// <summary>Generates an ICO for every menu color in <c>state.json</c> (built-in + user-defined) using the system folder glyph.</summary>
    public static void EnsureMenuIconsForAllPresets()
    {
        var doc = MenuStateReader.GetMerged();
        var pairs = new List<KeyValuePair<string, Color>>(doc.MenuColors.Count);
        foreach (var p in doc.MenuColors)
        {
            if (string.IsNullOrWhiteSpace(p.Id))
                continue;
            if (!MenuColorHex.TryParse(p.HexRgb, out var c))
                continue;
            pairs.Add(new KeyValuePair<string, Color>(p.Id, c));
        }

        FolderTintedPresetIconWriter.WritePresetSet(pairs, AppPaths.GeneratedIconsDirectory);
    }

    /// <summary>(Re)writes a single preset ICO immediately (used by the Companion when the user edits a color).</summary>
    public static string WritePresetIcon(string presetId, Color color) =>
        FolderTintedPresetIconWriter.WriteSinglePreset(presetId, color, AppPaths.GeneratedIconsDirectory);

    /// <summary>Shell menu icon string (<c>path,0</c>) when a cached preset ICO exists; otherwise <c>null</c> (caller uses a stock glyph).</summary>
    public static string? TryGetPresetIconMenuReference(string colorId)
    {
        if (string.IsNullOrWhiteSpace(colorId))
            return null;

        if (!MenuStateReader.TryGetMenuTint(colorId, out var tint))
            return null;

        var hex = MenuColorHex.ToHexRgb(tint);
        var path = FolderTintedPresetIconWriter.GetPresetIconPath(colorId, hex);
        if (!FolderTintedPresetIconWriter.ShouldReuseCachedIco(path, styleMarkerMatches: true))
            path = Path.GetFullPath(WritePresetIcon(colorId, tint));

        ClickTrace.Step($"menu-icon id={colorId} hex={hex} path='{path}' exists={File.Exists(path)} bytes={SafeFileLen(path)}");

        if (!File.Exists(path))
            return null;

        try
        {
            if (new FileInfo(path).Length < FolderTintedPresetIconWriter.MinCachedIcoBytes)
                return null;
        }
        catch
        {
            return null;
        }

        return FormatShellMenuIconReference(path);
    }

    /// <summary>Menu icon for an arbitrary RGB tint (quick palette); cached as <c>rgb-xxxxxx.ico</c>.</summary>
    public static string? TryGetRgbIconMenuReference(Color tint)
    {
        try
        {
            var path = Path.GetFullPath(FolderTintedPresetIconWriter.WriteTintedRgbIcon(tint));
            if (!File.Exists(path) || new FileInfo(path).Length < FolderTintedPresetIconWriter.MinCachedIcoBytes)
                return null;
            return FormatShellMenuIconReference(path);
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"rgb menu-icon failed hex={tint.R:X2}{tint.G:X2}{tint.B:X2}: {ex.Message}");
            return null;
        }
    }

    static long SafeFileLen(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return -1; }
    }

    static string FormatShellMenuIconReference(string iconFullPath)
    {
        if (iconFullPath.Contains('"', StringComparison.Ordinal))
            iconFullPath = iconFullPath.Replace("\"", "\\\"", StringComparison.Ordinal);
        if (iconFullPath.Contains(' ', StringComparison.Ordinal) || iconFullPath.Contains(',', StringComparison.Ordinal))
            return $"\"{iconFullPath}\",0";
        return $"{iconFullPath},0";
    }

    /// <summary>
    /// Writes <c>desktop.ini</c>, marks the folder read-only (required for Explorer to honor the ini),
    /// marks <c>desktop.ini</c> hidden+system, and notifies the shell.
    /// </summary>
    /// <exception cref="ArgumentException">Unknown color id.</exception>
    public static void Apply(string folderPath, string colorId)
    {
        ClickTrace.Step($"Apply enter folder='{folderPath}' colorId='{colorId}'");
        TraceLog.Append("shell-menu.log", $"[CORE] [STEP 0] Apply enter folder='{folderPath}' colorId='{colorId}'");

        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Folder path is required.", nameof(folderPath));
        if (!MenuStateReader.TryGetMenuTint(colorId, out var resolvedTint))
        {
            ClickTrace.Step($"unknown colorId='{colorId}' (not in menuColors / presets)");
            throw new ArgumentException($"Unknown color '{colorId}'.", nameof(colorId));
        }

        var hex = $"{resolvedTint.R:X2}{resolvedTint.G:X2}{resolvedTint.B:X2}";
        ClickTrace.Step($"resolved tint=#{hex}");

        var fullFolder = Path.GetFullPath(folderPath);
        TraceLog.Append("shell-menu.log", $"[CORE] [STEP 1] resolved fullFolder='{fullFolder}'");
        if (!Directory.Exists(fullFolder))
            throw new DirectoryNotFoundException(fullFolder);

        var probe = Path.Combine(fullFolder, DesktopIniFileName + ".probe");
        TraceLog.Append("shell-menu.log", $"[CORE] [STEP 2] write-probe '{probe}'");
        try
        {
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            ClickTrace.Step("write-probe OK (folder is writable)");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            TraceLog.Append("shell-menu.log", $"[CORE] [FAIL STEP 2] write-probe failed: {ex.Message}");
            ClickTrace.Step($"write-probe FAILED: {ex.Message}");
            throw new UnauthorizedAccessException($"Cannot write to folder '{fullFolder}'. Try moving it out of protected locations or run elevated.", ex);
        }

        TraceLog.Append("shell-menu.log", $"[CORE] [STEP 3] WritePresetIcon id='{colorId}'");
        var sourceIconPath = Path.GetFullPath(WritePresetIcon(colorId, resolvedTint));
        TraceLog.Append("shell-menu.log", $"[CORE] [STEP 4] sourceIconPath='{sourceIconPath}' exists={File.Exists(sourceIconPath)}");
        ClickTrace.Step($"preset ico='{sourceIconPath}' exists={File.Exists(sourceIconPath)}");
        if (!File.Exists(sourceIconPath))
            throw new FileNotFoundException("Preset icon missing after ensure.", sourceIconPath);

        // Versioned per-folder copy: Explorer caches by file path, so each Apply must use a fresh path.
        // Tag includes HEX so a color edit of the same id also busts the path.
        var iconFullPath = PerFolderIconCache.WriteVersionedCopy(
            fullFolder, sourceIconPath, PerFolderIconCache.TagForPresetId(colorId, hex));

        var iniPath = Path.Combine(fullFolder, DesktopIniFileName);
        var iconResourceLine = BuildIconResourceLine(iconFullPath);
        TraceLog.Append("shell-menu.log", $"[CORE] [STEP 5] iniPath='{iniPath}' iconResourceLine='{iconResourceLine}' versioned='{iconFullPath}'");
        ClickTrace.Step($"IconResource={iconResourceLine}");
        try
        {
            if (File.Exists(iniPath))
            {
                var previous = File.ReadAllLines(iniPath)
                    .FirstOrDefault(l => l.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase))?
                    .Substring("IconResource=".Length);
                var same = string.Equals(previous, iconResourceLine, StringComparison.OrdinalIgnoreCase);
                ClickTrace.Step($"previous IconResource={previous ?? "(none)"} samePath={same}" +
                                (same ? " WARN: Explorer will reuse its cached glyph for this path" : ""));
            }
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"could not read previous IconResource: {ex.Message}");
        }

        try
        {
            var iniExistsBefore = File.Exists(iniPath);
            var attrsBefore = iniExistsBefore ? File.GetAttributes(iniPath).ToString() : "(file missing)";
            var folderAttrsBefore = File.GetAttributes(fullFolder).ToString();
            TraceLog.Append("shell-menu.log",
                $"[CORE] [STEP 6 BEFORE-CLEAR] folderAttrs={folderAttrsBefore} iniExists={iniExistsBefore} iniAttrs={attrsBefore}");
            ClickTrace.Step($"before-clear folderAttrs={folderAttrsBefore} iniExists={iniExistsBefore}");
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[CORE] [WARN STEP 6] could not snapshot attrs: {ex.Message}");
        }

        ClearFolderReadOnly(fullFolder);

        try
        {
            var iniExistsAfter = File.Exists(iniPath);
            var attrsAfter = iniExistsAfter ? File.GetAttributes(iniPath).ToString() : "(file missing)";
            var folderAttrsAfter = File.GetAttributes(fullFolder).ToString();
            TraceLog.Append("shell-menu.log",
                $"[CORE] [STEP 7 PRE-WRITE] folderAttrs={folderAttrsAfter} iniExists={iniExistsAfter} iniAttrs={attrsAfter}");
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[CORE] [WARN STEP 7] could not snapshot attrs: {ex.Message}");
        }

        var iniBody = ComposeManagedIniBody(iniPath, iconResourceLine);

        TraceLog.Append("shell-menu.log", $"[CORE] [STEP 8 WRITE] {iniBody.Length} chars UTF-16 LE -> '{iniPath}'");
        WriteManagedDesktopIni(iniPath, iniBody);
        TraceLog.Append("shell-menu.log", "[CORE] [STEP 9] desktop.ini written + Hidden|System applied");
        ClickTrace.Step("desktop.ini written (UTF-16 LE, Hidden|System)");
        try
        {
            var written = File.ReadAllText(iniPath);
            var preview = written.Length > 240 ? written.Substring(0, 240).Replace("\r", "\\r").Replace("\n", "\\n") : written.Replace("\r", "\\r").Replace("\n", "\\n");
            ClickTrace.Step($"desktop.ini verify readback chars={written.Length} preview='{preview}'");
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"desktop.ini readback failed: {ex.Message}");
        }

        SetFolderReadOnly(fullFolder);
        TraceLog.Append("shell-menu.log", "[CORE] [STEP 10] set ReadOnly on folder");

        CommitIconViaShellApi(fullFolder, iniPath, iniBody, iconFullPath);

        NotifyShellAfterDesktopIniChange(fullFolder);
        TraceLog.Append("shell-menu.log", "[CORE] [STEP 11] nudged Explorer / SHChangeNotify done");
        ClickTrace.Step("SHChangeNotify sent (UPDATEITEM+UPDATEDIR+ATTRIBUTES)");

        PerFolderIconCache.DeleteOldVersions(fullFolder, iconFullPath);

        try
        {
            var folderAttrsFinal = File.GetAttributes(fullFolder).ToString();
            var iniOk = File.Exists(iniPath);
            ClickTrace.Step($"verify iniExists={iniOk} folderAttrs={folderAttrsFinal} versionedIcoExists={File.Exists(iconFullPath)}");
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"verify failed: {ex.Message}");
        }
    }

    /// <summary>Like <see cref="RemoveCustomization"/> but does not throw when <c>desktop.ini</c> is missing or not ours.</summary>
    /// <returns><c>true</c> if there was nothing to do, or our <c>desktop.ini</c> was removed.</returns>
    public static bool TryRemoveCustomization(string folderPath, out string? message)
    {
        message = null;
        try
        {
            RemoveCustomization(folderPath);
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    /// <summary>Removes our <c>desktop.ini</c> customization and folder read-only flag so Explorer shows the default folder icon.</summary>
    public static void RemoveCustomization(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Folder path is required.", nameof(folderPath));

        var fullFolder = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullFolder))
            throw new DirectoryNotFoundException(fullFolder);

        var iniPath = Path.Combine(fullFolder, DesktopIniFileName);
        if (!File.Exists(iniPath))
            return;

        if (!IsManagedDesktopIni(iniPath))
            throw new InvalidOperationException(
                $"The existing '{DesktopIniFileName}' in this folder was not created by Folder Managing (missing marker). It was not deleted.");

        RestoreOrDeleteManagedIni(fullFolder, iniPath);
        PerFolderIconCache.DeleteOldVersions(fullFolder, keepPath: null);
        NotifyShellAfterDesktopIniChange(fullFolder);
    }

    /// <summary>Restores the pre-existing <c>desktop.ini</c> saved by <see cref="ComposeManagedIniBody"/>, or deletes ours.</summary>
    static void RestoreOrDeleteManagedIni(string folder, string iniPath)
    {
        ClearFolderReadOnly(folder);
        var original = TryReadOriginalIni(iniPath);
        if (original is not null)
        {
            ClickTrace.Step($"reset: restoring original desktop.ini ({original.Length} chars)");
            WriteManagedDesktopIni(iniPath, original);
            SetFolderReadOnly(folder);
            return;
        }

        PrepareDesktopIniForWrite(iniPath);
        File.Delete(iniPath);
        NormalizeFolderAttributesAfterClear(folder);
    }

    /// <summary>
    /// Builds our <c>desktop.ini</c>. A pre-existing foreign ini (e.g. <c>LocalizedResourceName</c> on system folders)
    /// keeps all its non-icon lines and is embedded as Base64 so reset can put it back byte-for-byte.
    /// </summary>
    static string ComposeManagedIniBody(string iniPath, string iconResourceLine)
    {
        string? original = null;
        if (File.Exists(iniPath))
        {
            original = IsManagedDesktopIni(iniPath) ? TryReadOriginalIni(iniPath) : File.ReadAllText(iniPath);
            if (original is not null)
                ClickTrace.Step($"pre-existing desktop.ini preserved ({original.Length} chars)");
        }

        var nl = Environment.NewLine;
        var shellClassInfo = new StringBuilder();
        var otherSections = new StringBuilder();
        if (original is not null)
        {
            var section = "";
            foreach (var raw in original.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    section = trimmed;
                    if (!section.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase))
                        otherSections.Append(line).Append(nl);
                    continue;
                }

                if (trimmed.Length == 0)
                    continue;

                if (section.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase))
                {
                    var eq = trimmed.IndexOf('=');
                    var key = eq > 0 ? trimmed[..eq].Trim() : trimmed;
                    if (!OwnedShellClassInfoKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                        shellClassInfo.Append(line).Append(nl);
                }
                else if (section.Length > 0)
                {
                    otherSections.Append(line).Append(nl);
                }
            }
        }

        var body = new StringBuilder()
            .Append("[.ShellClassInfo]").Append(nl)
            .Append("ConfirmFileOp=0").Append(nl)
            .Append("NoSharing=1").Append(nl)
            .Append("IconResource=").Append(iconResourceLine).Append(nl)
            .Append(shellClassInfo)
            .Append("; ").Append(ManagedDesktopIniMarker).Append(nl);
        if (original is not null)
            body.Append(OriginalIniPrefix).Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(original))).Append(nl);
        body.Append(otherSections);
        return body.ToString();
    }

    static string? TryReadOriginalIni(string iniPath)
    {
        try
        {
            foreach (var line in File.ReadAllLines(iniPath))
            {
                if (line.StartsWith(OriginalIniPrefix, StringComparison.Ordinal))
                    return Encoding.UTF8.GetString(Convert.FromBase64String(line[OriginalIniPrefix.Length..].Trim()));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            ClickTrace.Step($"original desktop.ini unreadable: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Touches the folder and sends <c>SHChangeNotify</c> so Explorer repaints icons/metadata without changing <c>desktop.ini</c>.
    /// Use after Companion edits <c>state.json</c> (tags / pin) where the folder icon path is unchanged.
    /// </summary>
    public static void RequestExplorerRefreshForFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        try
        {
            var full = Path.GetFullPath(folderPath);
            if (!Directory.Exists(full))
                return;

            NotifyShellAfterDesktopIniChange(full);
        }
        catch
        {
            // best-effort UI nudge
        }
    }

    /// <summary>Deletes managed <c>desktop.ini</c> files under <paramref name="rootDirectory"/>.</summary>
    /// <returns>Number of <c>desktop.ini</c> files removed.</returns>
    public static int CleanupManagedDesktopInis(string rootDirectory, int maxDepth, bool recursive)
    {
        var fullRoot = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(fullRoot))
            throw new DirectoryNotFoundException(fullRoot);

        var removed = 0;
        if (!recursive)
        {
            TryRemoveManagedIniInFolder(fullRoot, ref removed);
            return removed;
        }

        void Walk(string dir, int depth)
        {
            TryRemoveManagedIniInFolder(dir, ref removed);
            if (depth >= maxDepth)
                return;

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                try
                {
                    Walk(sub, depth + 1);
                }
                catch (UnauthorizedAccessException)
                {
                    // skip protected directories
                }
                catch (IOException)
                {
                    // skip
                }
            }
        }

        Walk(fullRoot, 0);
        return removed;
    }

    static void TryRemoveManagedIniInFolder(string folder, ref int removed)
    {
        var iniPath = Path.Combine(folder, DesktopIniFileName);
        if (!File.Exists(iniPath) || !IsManagedDesktopIni(iniPath))
            return;

        try
        {
            RestoreOrDeleteManagedIni(folder, iniPath);
            PerFolderIconCache.DeleteOldVersions(folder, keepPath: null);
            NotifyShellAfterDesktopIniChange(folder);
            removed++;
        }
        catch
        {
            // best-effort bulk cleanup
        }
    }

    static bool IsManagedDesktopIni(string iniPath)
    {
        try
        {
            var text = File.ReadAllText(iniPath);
            if (text.Contains(ManagedDesktopIniMarker, StringComparison.Ordinal))
                return true;

            var gen = AppPaths.GeneratedIconsDirectory;
            if (text.Contains(gen, StringComparison.OrdinalIgnoreCase))
                return true;

            // Legacy: require "FolderManaging" in file so we never treat unrelated coral.ico icons as ours.
            if (!text.Contains("IconResource=", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!text.Contains("FolderManaging", StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var id in FolderColors.PresetRgb.Keys)
            {
                if (text.Contains($"{id}.ico", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (text.Contains("rgb-", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    static void NormalizeFolderAttributesAfterClear(string folder)
    {
        try
        {
            var attrs = File.GetAttributes(folder);
            File.SetAttributes(folder, attrs & ~(FileAttributes.ReadOnly | FileAttributes.System));
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Applies a custom RGB tint by generating <c>rgb-RRGGBB.ico</c> in the user icon cache (same shading pipeline as presets).
    /// </summary>
    public static void ApplyRgb(string folderPath, Color tint)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Folder path is required.", nameof(folderPath));

        var fullFolder = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullFolder))
            throw new DirectoryNotFoundException(fullFolder);

        var probe = Path.Combine(fullFolder, DesktopIniFileName + ".probe");
        try
        {
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException($"Cannot write to folder '{fullFolder}'.", ex);
        }

        var sourceIconPath = Path.GetFullPath(FolderTintedPresetIconWriter.WriteTintedRgbIcon(tint));
        if (!File.Exists(sourceIconPath))
            throw new FileNotFoundException("Tinted icon missing after generation.", sourceIconPath);

        var iconFullPath = PerFolderIconCache.WriteVersionedCopy(
            fullFolder, sourceIconPath, PerFolderIconCache.TagForRgb(tint));

        ClearFolderReadOnly(fullFolder);
        var iniPath = Path.Combine(fullFolder, DesktopIniFileName);

        var iconResourceLine = BuildIconResourceLine(iconFullPath);
        var iniBody = ComposeManagedIniBody(iniPath, iconResourceLine);

        WriteManagedDesktopIni(iniPath, iniBody);
        SetFolderReadOnly(fullFolder);
        CommitIconViaShellApi(fullFolder, iniPath, iniBody, iconFullPath);
        NotifyShellAfterDesktopIniChange(fullFolder);
        PerFolderIconCache.DeleteOldVersions(fullFolder, iconFullPath);
    }

    /// <summary>
    /// Re-commits the icon through <c>SHGetSetFolderCustomSettings</c>: a plain file write + SHChangeNotify does not
    /// reliably invalidate Explorer's cached desktop.ini, which then keeps rendering the previous IconResource.
    /// </summary>
    static void CommitIconViaShellApi(string fullFolder, string iniPath, string iniBody, string iconFullPath)
    {
        try
        {
            var fcs = new Win32Shell.SHFOLDERCUSTOMSETTINGS
            {
                dwSize = (uint)Marshal.SizeOf<Win32Shell.SHFOLDERCUSTOMSETTINGS>(),
                dwMask = Win32Shell.FCSM_ICONFILE,
                pszIconFile = iconFullPath,
                cchIconFile = 0,
                iIconIndex = 0,
            };
            var hr = Win32Shell.SHGetSetFolderCustomSettings(ref fcs, fullFolder, Win32Shell.FCS_FORCEWRITE);
            ClickTrace.Step($"SHGetSetFolderCustomSettings hr=0x{hr:X8}");
            TraceLog.Append("shell-menu.log", $"[CORE] [STEP 10b] SHGetSetFolderCustomSettings hr=0x{hr:X8} icon='{iconFullPath}'");
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"SHGetSetFolderCustomSettings threw: {ex.Message}");
        }

        try
        {
            var current = File.ReadAllText(iniPath);
            var lostOriginal = iniBody.Contains(OriginalIniPrefix, StringComparison.Ordinal) &&
                               !current.Contains(OriginalIniPrefix, StringComparison.Ordinal);
            if (lostOriginal || !IsManagedDesktopIni(iniPath) ||
                !current.Contains(Path.GetFileName(iconFullPath), StringComparison.OrdinalIgnoreCase))
            {
                ClickTrace.Step("desktop.ini changed by shell API (marker/icon missing) -> rewriting our body");
                ClearFolderReadOnly(fullFolder);
                WriteManagedDesktopIni(iniPath, iniBody);
                SetFolderReadOnly(fullFolder);
            }

            var preview = File.ReadAllText(iniPath).Replace("\r", "\\r").Replace("\n", "\\n");
            ClickTrace.Step($"desktop.ini after shell API: '{(preview.Length > 300 ? preview[..300] : preview)}'");
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"desktop.ini post-check failed: {ex.Message}");
        }
    }

    static string GetIconPath(string colorId)
    {
        if (!MenuStateReader.TryGetMenuTint(colorId, out var tint))
            return Path.Combine(AppPaths.GeneratedIconsDirectory, FolderTintedPresetIconWriter.GetPresetIconFileName(colorId, "000000"));
        return FolderTintedPresetIconWriter.GetPresetIconPath(colorId, MenuColorHex.ToHexRgb(tint));
    }

    /// <summary>Builds the value for IconResource= (path + ,0).</summary>
    /// <remarks>
    /// The file name must stay long: 8.3 short names (<c>AE806F~1.ICO</c>) are reused after old versions are deleted,
    /// so Explorer's per-path icon cache would serve a previous color. Only the directory may be shortened (stable).
    /// </remarks>
    static string BuildIconResourceLine(string iconFullPath)
    {
        var dir = Path.GetDirectoryName(iconFullPath) ?? string.Empty;
        var fileName = Path.GetFileName(iconFullPath);
        if (dir.Contains(' ', StringComparison.Ordinal) || dir.Contains(',', StringComparison.Ordinal))
            dir = TryGetShortPath(dir) ?? dir;
        var pathForIni = Path.Combine(dir, fileName);
        if (pathForIni.Contains('"', StringComparison.Ordinal))
            pathForIni = pathForIni.Replace("\"", "\\\"", StringComparison.Ordinal);
        if (pathForIni.Contains(' ', StringComparison.Ordinal) || pathForIni.Contains(',', StringComparison.Ordinal))
            return $"\"{pathForIni}\",0";
        return $"{pathForIni},0";
    }

    static string? TryGetShortPath(string longPath)
    {
        var capacity = 260;
        for (var attempt = 0; attempt < 4; attempt++, capacity *= 2)
        {
            var sb = new StringBuilder(capacity);
            var n = Win32Kernel.GetShortPathNameW(longPath, sb, sb.Capacity);
            if (n == 0)
                return null;
            if (n >= sb.Capacity)
                continue;
            return sb.ToString(0, n);
        }

        return null;
    }

    static void ClearFolderReadOnly(string folder)
    {
        var attrs = File.GetAttributes(folder);
        // Strip ReadOnly and System so we can update desktop.ini (older builds set System on the folder).
        var cleared = attrs & ~(FileAttributes.ReadOnly | FileAttributes.System);
        if (cleared != attrs)
            File.SetAttributes(folder, cleared);
    }

    /// <summary>
    /// Strips attribute flags on an existing <c>desktop.ini</c> so it can be deleted or replaced.
    /// <see cref="FileAttributes.Normal"/> clears Hidden, System, ReadOnly, and Archive bits.
    /// </summary>
    static void PrepareDesktopIniForWrite(string iniPath)
    {
        if (!File.Exists(iniPath))
            return;

        try
        {
            File.SetAttributes(iniPath, FileAttributes.Normal);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException(
                $"Cannot reset attributes on existing '{iniPath}' (clear Hidden/System/ReadOnly before overwrite).",
                ex);
        }
    }

    /// <summary>
    /// If <c>desktop.ini</c> exists, clears all file attribute flags, writes UTF-16 LE (with BOM), then applies Hidden+System
    /// only after a successful write — Explorer is picky about encoding and locked attributes.
    /// </summary>
    static void WriteManagedDesktopIni(string iniPath, string iniBody)
    {
        if (File.Exists(iniPath))
        {
            try
            {
                File.SetAttributes(iniPath, FileAttributes.Normal);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                throw new UnauthorizedAccessException(
                    $"Cannot strip attributes on '{iniPath}' before overwrite (try clearing folder read-only on parent).",
                    ex);
            }
        }

        try
        {
            File.WriteAllText(iniPath, iniBody, DesktopIniEncoding);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException($"Cannot write '{iniPath}'.", ex);
        }

        File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);
    }

    static void SetFolderReadOnly(string folder)
    {
        var attrs = File.GetAttributes(folder);
        // Explorer honors folder customization with ReadOnly on the folder; do not set System (breaks attrib / UX).
        File.SetAttributes(folder, (attrs & ~FileAttributes.System) | FileAttributes.ReadOnly);
    }

    /// <summary>
    /// Win11-friendly refresh after the caller has already finished mutating <c>desktop.ini</c> and folder attributes.
    /// Bumps mtime, then sends <c>SHCNE_UPDATEITEM</c> + <c>SHCNE_UPDATEDIR</c> on the folder, and finally fires
    /// <c>SHCNE_ATTRIBUTES</c> so Explorer reconsiders the folder customization.
    /// </summary>
    static void NotifyShellAfterDesktopIniChange(string folderPath)
    {
        try
        {
            Directory.SetLastWriteTime(folderPath, DateTime.Now);
        }
        catch
        {
            // ignore
        }

        ClickTrace.Step($"SHChangeNotify begin folder='{folderPath}'");
        SendShellChangeNotify(Win32Shell.SHCNE_UPDATEITEM, folderPath);
        SendShellChangeNotify(Win32Shell.SHCNE_UPDATEDIR, folderPath);
        SendShellChangeNotify(Win32Shell.SHCNE_ATTRIBUTES, folderPath);

        var parent = TryGetParentDirectory(folderPath);
        if (parent is not null)
        {
            SendShellChangeNotify(Win32Shell.SHCNE_UPDATEITEM, parent);
            SendShellChangeNotify(Win32Shell.SHCNE_UPDATEDIR, parent);
        }

        // Global association/icon cache nudge — helps when Explorer keeps the previous glyph until restart.
        try
        {
            Win32Shell.SHChangeNotify(Win32Shell.SHCNE_ASSOCCHANGED, Win32Shell.SHCNF_IDLIST, 0, 0);
            ClickTrace.Step("SHChangeNotify SHCNE_ASSOCCHANGED sent");
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"SHCNE_ASSOCCHANGED failed: {ex.Message}");
        }
    }

    static void SendShellChangeNotify(uint eventId, string path)
    {
        var p = Marshal.StringToHGlobalUni(path);
        try
        {
            // FLUSHNOWAIT: SHCNF_FLUSH blocks until Explorer processes the event and can hang the caller indefinitely.
            Win32Shell.SHChangeNotify(
                eventId,
                Win32Shell.SHCNF_PATHW | Win32Shell.SHCNF_FLUSHNOWAIT,
                p,
                nint.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    static string? TryGetParentDirectory(string folderPath)
    {
        try
        {
            return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folderPath));
        }
        catch
        {
            return null;
        }
    }
}
