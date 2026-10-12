using System.Runtime.InteropServices;
using System.Text;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using Microsoft.Win32;

namespace FolderManaging.Core.ShellProperties;

/// <summary>
/// Registers DesktopIniPropertyMap so Explorer maps <c>[FolderManaging] ColorHue</c> to
/// <c>System.Rating</c> (numeric rainbow sort). Optionally registers <c>.propdesc</c> for the
/// custom name (often ignored for folders on Win11).
/// Requires elevation (HKLM).
/// </summary>
public static class PropertySchemaRegistration
{
    const string PropdescFileName = "FolderManaging.propdesc";

    public static string ProgramDataSchemaDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FolderManaging");

    public static string ProgramDataSchemaPath =>
        Path.Combine(ProgramDataSchemaDirectory, PropdescFileName);

    static string RatingMapKeyPath =>
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DesktopIniPropertyMap\PropertySetStorage\" +
        FolderColorHueProperty.RatingFormatId.ToString("B").ToUpperInvariant() + @"\" +
        FolderColorHueProperty.RatingPropId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    static string ColorHueMapKeyPath =>
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DesktopIniPropertyMap\PropertySetStorage\" +
        FolderColorHueProperty.FormatId.ToString("B").ToUpperInvariant() + @"\" +
        FolderColorHueProperty.PropId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Folder types whose Details column chooser should list «Цвет папки».</summary>
    static readonly string[] FolderTypeGuids =
    [
        "{5c4f28b5-f869-4e84-8e60-f11db97c5cc7}", // Generic
        "{7d49d726-3c21-4f05-99aa-fdc2c9474656}", // Documents
        "{885a186e-a440-4ada-812b-db871b942259}", // Downloads
        "{CD0FC69B-71E2-46e5-9690-5BCD9F57AAB3}", // UserFiles
    ];

    const string ColumnToken = "FolderManaging.ColorHue";

    /// <summary>
    /// Writes Rating←ColorHue DesktopIniPropertyMap. If <paramref name="sourcePropdescPath"/>
    /// exists, also copies/registers the optional custom schema (best-effort).
    /// </summary>
    public static void Register(string? sourcePropdescPath = null)
    {
        WriteDesktopIniMap(ColorHueMapKeyPath);
        ClickTrace.Step("DesktopIniPropertyMap: FolderManaging.ColorHue ← [FolderManaging] ColorHue");
        WriteDesktopIniMap(RatingMapKeyPath);
        ClickTrace.Step("DesktopIniPropertyMap: System.Rating ← [FolderManaging] ColorHue");

        var patched = EnsureColumnOnFolderTypes();
        ClickTrace.Step($"ColumnList patched on {patched} folder type(s) (column «Цвет папки»)");

        if (string.IsNullOrWhiteSpace(sourcePropdescPath) || !File.Exists(sourcePropdescPath))
            throw new FileNotFoundException("FolderManaging.propdesc is required to register the «Цвет папки» column.", sourcePropdescPath);

        Directory.CreateDirectory(ProgramDataSchemaDirectory);
        var dest = ProgramDataSchemaPath;
        if (File.Exists(dest))
        {
            var unreg = PSUnregisterPropertySchema(dest);
            ClickTrace.Step($"PSUnregisterPropertySchema (refresh) hr=0x{unreg:X8}");
        }

        var xml = File.ReadAllText(sourcePropdescPath);
        // One column title for the whole PC. It is the Windows display language at
        // registration: ru / en / nb, otherwise English. The elevated worker refreshes
        // it when that language changes.
        var label = System.Security.SecurityElement.Escape(UiText.Get("ColumnName"));
        xml = xml.Replace("__FM_COLUMN_LABEL__", label, StringComparison.Ordinal);
        File.WriteAllText(dest, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        ClickTrace.Step($"propdesc written -> '{dest}' label='{label}'");

        var hr = PSRegisterPropertySchema(dest);
        ClickTrace.Step($"PSRegisterPropertySchema hr=0x{hr:X8} path='{dest}'");
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    /// <summary>
    /// Rewrites the registered column title when the Windows display language changed.
    /// No-op when the schema is already in that language, or when this process is not allowed to write HKLM.
    /// </summary>
    public static void SyncColumnLabelForCurrentLanguage()
    {
        var source = TryFindBundledPropdesc();
        if (source is null)
            return;

        var wanted = System.Security.SecurityElement.Escape(UiText.Get("ColumnName"));
        if (File.Exists(ProgramDataSchemaPath))
        {
            var current = File.ReadAllText(ProgramDataSchemaPath);
            if (current.Contains("label=\"" + wanted + "\"", StringComparison.Ordinal)
                && !current.Contains("__FM_COLUMN_LABEL__", StringComparison.Ordinal))
                return;
        }

        Register(source);
        ClickTrace.Step($"column label synced to '{wanted}'");
    }

    /// <summary>Removes Rating map and unregisters schema (best-effort).</summary>
    public static void Unregister()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DesktopIniPropertyMap\PropertySetStorage",
                writable: true);
            baseKey?.DeleteSubKeyTree(
                FolderColorHueProperty.RatingFormatId.ToString("B").ToUpperInvariant(),
                throwOnMissingSubKey: false);
            baseKey?.DeleteSubKeyTree(
                FolderColorHueProperty.FormatId.ToString("B").ToUpperInvariant(),
                throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"DesktopIniPropertyMap remove failed: {ex.Message}");
        }

        var path = ProgramDataSchemaPath;
        if (File.Exists(path))
        {
            var hr = PSUnregisterPropertySchema(path);
            ClickTrace.Step($"PSUnregisterPropertySchema hr=0x{hr:X8}");
        }
    }

    static void WriteDesktopIniMap(string keyPath)
    {
        using var key = Registry.LocalMachine.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException("Cannot create DesktopIniPropertyMap key (need Administrator).");

        key.SetValue("Section", FolderColorHueProperty.IniSectionName, RegistryValueKind.String);
        key.SetValue("Key", FolderColorHueProperty.IniHueKey, RegistryValueKind.String);
        key.SetValue("VarType", (int)FolderColorHueProperty.VarTypeUInt32, RegistryValueKind.DWord);
    }

    /// <summary>
    /// FolderTypes under HKLM are TrustedInstaller-owned, so the column is added in HKCU
    /// (same relative path). Explorer uses that list for the Details column chooser.
    /// </summary>
    static int EnsureColumnOnFolderTypes()
    {
        var n = 0;
        foreach (var guid in FolderTypeGuids)
        {
            var rel =
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FolderTypes\{guid}\TopViews\{{00000000-0000-0000-0000-000000000000}}";
            string? list;
            object? mode;
            object? name;
            object? order;
            using (var hklm = Registry.LocalMachine.OpenSubKey(rel, writable: false))
            {
                if (hklm?.GetValue("ColumnList") is not string current || current.Length == 0)
                    continue;
                list = current.Contains(ColumnToken, StringComparison.OrdinalIgnoreCase)
                    ? current
                    : current.TrimEnd(';') + ";0(14)" + ColumnToken;
                mode = hklm.GetValue("LogicalViewMode");
                name = hklm.GetValue("Name");
                order = hklm.GetValue("Order");
            }

            using var hkcu = Registry.CurrentUser.CreateSubKey(rel, writable: true)
                ?? throw new InvalidOperationException("Cannot write HKCU FolderTypes.");
            hkcu.SetValue("ColumnList", list, RegistryValueKind.String);
            if (mode is not null)
                hkcu.SetValue("LogicalViewMode", mode);
            if (name is not null)
                hkcu.SetValue("Name", name);
            if (order is not null)
                hkcu.SetValue("Order", order);
            n++;
        }

        return n;
    }

    public static string? TryFindBundledPropdesc(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
            return Path.GetFullPath(explicitPath);

        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[]
                 {
                     Path.Combine(baseDir, PropdescFileName),
                     Path.Combine(baseDir, "sparse", PropdescFileName),
                     Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "sparse", PropdescFileName)),
                 })
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    [DllImport("propsys.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int PSRegisterPropertySchema(string pszPath);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int PSUnregisterPropertySchema(string pszPath);
}
