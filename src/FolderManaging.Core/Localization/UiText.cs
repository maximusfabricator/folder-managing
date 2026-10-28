// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core.Localization;

/// <summary>A UI language the app ships strings for.</summary>
/// <param name="Code">Culture name; also the name of the embedded <c>Localization\Strings\{Code}.json</c> table.</param>
/// <param name="NativeName">How speakers of the language write its name (shown in the language picker).</param>
public sealed record UiLanguage(string Code, string NativeName, bool IsRightToLeft = false);

/// <summary>
/// UI strings. The language is the one picked in Settings (<c>language</c> in <c>state.json</c>), otherwise the first
/// Windows display language we have strings for, otherwise English. <c>en.json</c> has every key; a key missing from
/// another table falls back to English.
/// </summary>
public static class UiText
{
    public const string English = "en";

    public static IReadOnlyList<UiLanguage> Languages { get; } =
    [
        new("en", "English"),
        new("zh-Hans", "中文（简体）"),
        new("zh-Hant", "中文（繁體）"),
        new("hi", "हिन्दी"),
        new("es", "Español"),
        new("fr", "Français"),
        new("ar", "العربية", IsRightToLeft: true),
        new("bn", "বাংলা"),
        new("pt-BR", "Português (Brasil)"),
        new("ru", "Русский"),
        new("ur", "اردو", IsRightToLeft: true),
        new("id", "Bahasa Indonesia"),
        new("de", "Deutsch"),
        new("ja", "日本語"),
        new("tr", "Türkçe"),
        new("ko", "한국어"),
        new("vi", "Tiếng Việt"),
        new("it", "Italiano"),
        new("fa", "فارسی", IsRightToLeft: true),
        new("pl", "Polski"),
        new("uk", "Українська"),
        new("th", "ไทย"),
        new("nl", "Nederlands"),
        new("nb", "Norsk bokmål"),
    ];

    static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase);

    static volatile UiLanguage _current = Languages[0];

    public static UiLanguage Current => _current;

    public static bool IsRightToLeft => _current.IsRightToLeft;

    /// <summary>Culture of <see cref="Current"/>, for number and date formatting.</summary>
    public static CultureInfo Culture => GetCulture(_current.Code);

    /// <summary>The language "Match Windows" resolves to right now.</summary>
    public static UiLanguage WindowsLanguage => Find(ResolveFromWindows());

    /// <summary>Uses the language saved in <c>state.json</c>, otherwise the Windows display language.</summary>
    public static void ApplyToProcess() => Use(ReadSavedPreference());

    /// <summary>
    /// Switches the process to <paramref name="preference"/> (a <see cref="UiLanguage.Code"/>);
    /// null or empty follows Windows.
    /// </summary>
    public static void Use(string? preference)
    {
#if FM_DEV
        // Developer builds: FM_UI_LANGUAGE shows a language without touching state.json (UI tours, screenshots).
        if (Environment.GetEnvironmentVariable("FM_UI_LANGUAGE") is { Length: > 0 } forced)
            preference = forced;
#endif
        var language = Find(string.IsNullOrWhiteSpace(preference) ? ResolveFromWindows() : ResolveLanguage(preference));
        _current = language;
        var culture = GetCulture(language.Code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    public static UiLanguage Find(string code) =>
        Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];

    /// <summary>Maps any culture name (<c>pt-PT</c>, <c>zh-TW</c>, <c>nn-NO</c>…) to a <see cref="UiLanguage.Code"/>.</summary>
    public static string ResolveLanguage(string? name) => TryResolve(name) ?? English;

    public static string ResolveLanguage(CultureInfo ui) => TryResolve(ui) ?? English;

    public static string Get(string key)
    {
        if (Strings(_current.Code).TryGetValue(key, out var value))
            return value;
        return Strings(English).TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(Culture, Get(key), args);

    /// <summary>
    /// A path for a message, kept in one left-to-right piece. Right-to-left text otherwise moves its ends:
    /// "C:\Program Files (x86)" would show as "(C:\Program Files (x86". The marks inside keep digits at the ends
    /// ("Project 4", "D:\Photos\2026") on their side when the path stands alone on a line.
    /// </summary>
    public static string LeftToRight(string value) => IsRightToLeft ? "\u202A\u200E" + value + "\u200E\u202C" : value;

    /// <summary>The embedded table for <paramref name="code"/> as shipped (empty when there is none).</summary>
    public static IReadOnlyDictionary<string, string> Strings(string code) => Tables.GetOrAdd(code, Load);

    static string? TryResolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var exact = Languages.FirstOrDefault(l => string.Equals(l.Code, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.Code;
        try
        {
            return TryResolve(CultureInfo.GetCultureInfo(name));
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    static string? TryResolve(CultureInfo ui)
    {
        for (var culture = ui; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            var exact = Languages.FirstOrDefault(l => string.Equals(l.Code, culture.Name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact.Code;
        }

        var iso = ui.TwoLetterISOLanguageName;
        if (iso is "no" or "nn")
            return "nb";
        return Languages.FirstOrDefault(l =>
            string.Equals(l.Code.Split('-')[0], iso, StringComparison.OrdinalIgnoreCase))?.Code;
    }

    /// <summary>First language in the Windows display-language list that we ship; Kazakh-then-Russian gets Russian.</summary>
    static string ResolveFromWindows()
    {
        foreach (var name in WindowsDisplayLanguages())
        {
            if (TryResolve(name) is { } code)
                return code;
        }

        return English;
    }

    static IEnumerable<string> WindowsDisplayLanguages()
    {
        uint count = 0, length = 0;
        if (GetUserPreferredUILanguages(MuiLanguageName, out count, null, ref length) && length > 0)
        {
            var buffer = new char[length];
            if (GetUserPreferredUILanguages(MuiLanguageName, out count, buffer, ref length))
            {
                foreach (var name in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    yield return name;
            }
        }

        string? fallback = null;
        try
        {
            fallback = new CultureInfo(GetUserDefaultUILanguage()).Name;
        }
        catch (CultureNotFoundException)
        {
        }

        yield return fallback ?? CultureInfo.CurrentUICulture.Name;
    }

    static string? ReadSavedPreference()
    {
        try
        {
            var path = AppPaths.StateFilePath;
            if (!File.Exists(path))
                return null;
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            return json.RootElement.ValueKind == JsonValueKind.Object
                   && json.RootElement.TryGetProperty("language", out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static IReadOnlyDictionary<string, string> Load(string code)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"FolderManaging.Strings.{code}.json");
        if (stream is null)
            return table;
        using var json = JsonDocument.Parse(stream);
        foreach (var entry in json.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String)
                table[entry.Name] = entry.Value.GetString()!;
        }

        return table;
    }

    static CultureInfo GetCulture(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    const uint MuiLanguageName = 0x8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetUserPreferredUILanguages(uint dwFlags, out uint pulNumLanguages, char[]? pwszLanguagesBuffer, ref uint pcchLanguagesBuffer);

    [DllImport("kernel32.dll")]
    static extern ushort GetUserDefaultUILanguage();
}
