// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
using FolderManaging.Core.ShellProperties;
using FolderManaging.Core.Storage;

namespace FolderManaging.SelfTests;

/// <summary>
/// Checks the tag and reset rules without test packages or network.
/// Nothing here touches the real state.json or a folder on disk.
/// Run: dotnet run --project tests\FolderManaging.SelfTests -c Release
/// </summary>
static partial class Program
{
    const string Folder = @"C:\FmSelfTest\Projects";
    static int _failed;

    static async Task<int> Main()
    {
        await Check("toggle: removing the last tag drops the empty entry", async () =>
        {
            var store = new MemoryStore(Doc(new FolderStateEntry { Path = Folder, Tags = { "work" } }));
            await new FolderStateEditor(store).RemoveTagAsync(Folder, "WORK");
            Expect(store.Saved.Folders.Count == 0, $"folders left: {store.Saved.Folders.Count}");
        });

        await Check("toggle: other tags and the color stay", async () =>
        {
            var store = new MemoryStore(Doc(new FolderStateEntry { Path = Folder, Tags = { "work", "later" }, ColorToken = "red" }));
            await new FolderStateEditor(store).RemoveTagAsync(Folder + @"\", "work");
            var entry = store.Saved.Folders.Single();
            Expect(entry.Tags.SequenceEqual(new[] { "later" }), $"tags: {string.Join(",", entry.Tags)}");
            Expect(entry.ColorToken == "red", $"color: {entry.ColorToken}");
        });

        await Check("toggle: a pinned folder keeps its entry", async () =>
        {
            var store = new MemoryStore(Doc(new FolderStateEntry { Path = Folder, Tags = { "work" }, IsPinned = true }));
            await new FolderStateEditor(store).RemoveTagAsync(Folder, "work");
            Expect(store.Saved.Folders.Single().Tags.Count == 0, "tag was not removed");
        });

        await Check("toggle: adding the same tag twice keeps one", async () =>
        {
            var store = new MemoryStore(Doc());
            var editor = new FolderStateEditor(store);
            await editor.UpsertTagAsync(Folder, "work");
            await editor.UpsertTagAsync(Folder, "WORK");
            Expect(store.Saved.Folders.Single().Tags.Count == 1, $"tags: {string.Join(",", store.Saved.Folders.Single().Tags)}");
        });

        await Check("reset: color and tags go, the pin stays", async () =>
        {
            var store = new MemoryStore(Doc(new FolderStateEntry { Path = Folder, Tags = { "work" }, ColorToken = "rgb-112233", IsPinned = true }));
            await new FolderStateEditor(store).ResetFolderAsync(Folder);
            var entry = store.Saved.Folders.Single();
            Expect(entry.Tags.Count == 0 && entry.ColorToken is null && entry.IsPinned, "entry was not reset");
        });

        await Check("reset: an unpinned folder leaves the list", async () =>
        {
            var store = new MemoryStore(Doc(new FolderStateEntry { Path = Folder, Tags = { "work" }, ColorToken = "red" }));
            await new FolderStateEditor(store).ResetFolderAsync(Folder);
            Expect(store.Saved.Folders.Count == 0, $"folders left: {store.Saved.Folders.Count}");
        });

        await Check("reset: an unknown folder writes nothing", async () =>
        {
            var store = new MemoryStore(Doc());
            await new FolderStateEditor(store).ResetFolderAsync(Folder);
            Expect(store.SaveCount == 0, $"saves: {store.SaveCount}");
        });

        Check("menu checkmark: tag lookup ignores case and a trailing slash", () =>
        {
            var doc = Doc(new FolderStateEntry { Path = Folder, Tags = { "Work" } });
            Expect(FolderStateTagSync.FolderHasTag(doc, Folder.ToLowerInvariant() + @"\", "work"), "tag not found");
            Expect(!FolderStateTagSync.FolderHasTag(doc, Folder, "later"), "wrong tag found");
            Expect(!FolderStateTagSync.FolderHasTag(doc, @"C:\FmSelfTest\Other", "work"), "wrong folder matched");
        });

        Check("Explorer column: labels are unique and A to Z", () =>
        {
            var doc = Doc(new FolderStateEntry { Path = Folder, Tags = { "later", "work", "LATER" } });
            doc.MenuTags.Add(new MenuTagPreset { Id = "work", Label = "Work" });
            doc.MenuTags.Add(new MenuTagPreset { Id = "later", Label = "Later" });
            var labels = FolderTagProperty.LabelsFor(doc, Folder);
            Expect(labels.SequenceEqual(new[] { "Later", "Work" }), $"labels: {string.Join(";", labels)}");
        });

        Check("new user: no state.json gives the built-in defaults and no folders", () =>
        {
            // What JsonFolderStateStore.LoadSynchronously returns on a PC that never ran the app.
            var doc = new AppStateDocument();
            MenuPresetNormalizer.Merge(doc);
            Expect(doc.Folders.Count == 0, $"folders: {doc.Folders.Count}");
            Expect(doc.MenuColors.Select(c => c.Id).Order().SequenceEqual(new[] { "blue", "green", "purple", "red" }),
                $"colors: {string.Join(", ", doc.MenuColors.Select(c => c.Id))}");
            Expect(doc.MenuColors.All(c => c.Label.Length == 0), "a color has a name");
            Expect(doc.MenuTags.Select(t => t.Id).SequenceEqual(new[] { "work", "personal", "later", "archive" }),
                $"tags: {string.Join(", ", doc.MenuTags.Select(t => t.Id))}");
            Expect(doc.Language is null && doc.Theme == AppThemePreference.System && doc.AutoElevateProtectedFolders,
                $"settings: language={doc.Language} theme={doc.Theme} autoElevate={doc.AutoElevateProtectedFolders}");
            Expect(doc.ContextMenu is null && !doc.ColorSortTipShown, "menu layout or tip state is not the default");
        });

        Check("default tags: deleting every tag must not bring the four defaults back", () =>
        {
            var doc = new AppStateDocument();
            MenuPresetNormalizer.Merge(doc);
            doc.MenuTags.Clear();
            MenuPresetNormalizer.Merge(doc);
            Expect(doc.MenuTags.Count == 0, $"tags came back: {string.Join(", ", doc.MenuTags.Select(t => t.Id))}");
        });

        Check("strings: every language ships a table", () =>
        {
            var missing = UiText.Languages.Where(l => UiText.Strings(l.Code).Count == 0).Select(l => l.Code).ToList();
            Expect(missing.Count == 0, $"no table: {string.Join(", ", missing)}");
        });

        foreach (var language in UiText.Languages.Where(l => l.Code != UiText.English))
        {
            Check($"strings: {language.Code} matches English", () =>
            {
                var problems = StringTableProblems(UiText.Strings(UiText.English), UiText.Strings(language.Code));
                Expect(problems.Count == 0, string.Join("; ", problems.Take(8)));
            });
        }

        Check("language: Windows culture names map to a shipped language", () =>
        {
            var cases = new (string Culture, string Expected)[]
            {
                ("en-GB", "en"), ("de-AT", "de"), ("fr-CA", "fr"), ("es-MX", "es"), ("pt-PT", "pt-BR"), ("pt-BR", "pt-BR"),
                ("zh-CN", "zh-Hans"), ("zh-SG", "zh-Hans"), ("zh-TW", "zh-Hant"), ("zh-HK", "zh-Hant"), ("nn-NO", "nb"),
                ("nb-NO", "nb"), ("ar-EG", "ar"), ("fa-IR", "fa"), ("ur-PK", "ur"), ("hi-IN", "hi"), ("bn-BD", "bn"),
                ("ja-JP", "ja"), ("ko-KR", "ko"), ("uk-UA", "uk"), ("th-TH", "th"), ("kk-KZ", "en"), ("", "en"),
            };
            var wrong = cases.Where(c => UiText.ResolveLanguage(c.Culture) != c.Expected)
                .Select(c => $"{c.Culture} -> {UiText.ResolveLanguage(c.Culture)} (want {c.Expected})")
                .ToList();
            Expect(wrong.Count == 0, string.Join("; ", wrong));
        });

        Check("language: only Arabic, Persian and Urdu are right-to-left", () =>
        {
            var rtl = UiText.Languages.Where(l => l.IsRightToLeft).Select(l => l.Code).OrderBy(c => c);
            Expect(rtl.SequenceEqual(new[] { "ar", "fa", "ur" }), $"rtl: {string.Join(", ", rtl)}");
        });

        Check("strings: right-to-left tables mark their left-to-right runs", () =>
        {
            var unmarked = UiText.Languages.Where(l => l.IsRightToLeft)
                .SelectMany(l => UiText.Strings(l.Code).SelectMany(s =>
                    UnmarkedLtrRun.Matches(s.Value).Select(m => $"{l.Code} {s.Key} \"{m.Value}\"")))
                .ToList();
            Expect(unmarked.Count == 0, string.Join("; ", unmarked.Take(8)));
        });

        Check("language: switching changes the text and English fills nothing in", () =>
        {
            try
            {
                UiText.Use("de");
                Expect(UiText.Current.Code == "de", $"current: {UiText.Current.Code}");
                Expect(UiText.Get("Save") != UiText.Strings(UiText.English)["Save"], "German Save is the English text");
                UiText.Use("ar");
                Expect(UiText.IsRightToLeft, "Arabic is not right-to-left");
            }
            finally
            {
                UiText.Use(UiText.English);
            }
        });

        FeatureChecks();
        SchemeChecks();
        await UpdateChecks();

        Console.WriteLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        return _failed == 0 ? 0 : 1;
    }

    static readonly string[] MenuLabelKeys =
        ["Pin", "Unpin", "TagLine", "TagLineOn", "ColorLine", "ColorHex", "ColorPlain", "RecentLine", "LabelQuick", "LabelReset", "LabelSettings"];

    /// <summary>Differences that break the UI: lost keys, placeholders, line breaks or menu emoji.</summary>
    static List<string> StringTableProblems(IReadOnlyDictionary<string, string> english, IReadOnlyDictionary<string, string> table)
    {
        var problems = new List<string>();
        foreach (var key in english.Keys.Except(table.Keys))
            problems.Add($"missing {key}");
        foreach (var key in table.Keys.Except(english.Keys))
            problems.Add($"unknown {key}");

        foreach (var (key, source) in english)
        {
            if (!table.TryGetValue(key, out var text))
                continue;
            if (string.IsNullOrWhiteSpace(text))
                problems.Add($"{key} empty");
            if (!Placeholders(source).SequenceEqual(Placeholders(text)))
                problems.Add($"{key} placeholders");
            if (source.Count(c => c == '\n') != text.Count(c => c == '\n') || source.Count(c => c == '\r') != text.Count(c => c == '\r'))
                problems.Add($"{key} line breaks");
            try
            {
                _ = string.Format(text, "a", "b", "c", "d");
            }
            catch (FormatException)
            {
                problems.Add($"{key} bad format string");
            }
        }

        foreach (var key in MenuLabelKeys)
        {
            if (table.TryGetValue(key, out var text) && text.Split(' ')[0] != english[key].Split(' ')[0])
                problems.Add($"{key} emoji");
        }

        return problems;
    }

    /// <summary>
    /// Left-to-right bits that WPF shows in the wrong order inside right-to-left text unless U+200E marks them:
    /// a word starting with ".", "#" or "%" (".NET" turns into "NET."), a number after a Latin word
    /// ("Windows 11" turns into "11 Windows"), a number right after "#" ("#0000FF" turns into "0000FF#") and
    /// brackets opened right after a Latin word ("Windows (UAC)" turns into "(Windows (UAC").
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex UnmarkedLtrRun =
        new(@"(?<=^|[\s""\u00AB\u201C(])[.#%]\u200E?[A-Za-z{]|#[{0-9]|[A-Za-z][ \u00A0][0-9]|(?<!\u200E)Folder Manag|[A-Za-z0-9][ \u00A0]\([^()]*[A-Za-z0-9.]\)(?!\u200E)");

    static IEnumerable<string> Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{\d\}").Select(m => m.Value).Order();

    static async Task Check(string name, Func<Task> body)
    {
        try
        {
            await body();
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"FAIL  {name}: {ex.Message}");
        }
    }

    static void Check(string name, Action body)
    {
        try
        {
            body();
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"FAIL  {name}: {ex.Message}");
        }
    }

    static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    static AppStateDocument Doc(params FolderStateEntry[] folders)
    {
        var doc = new AppStateDocument();
        doc.Folders.AddRange(folders);
        return doc;
    }
}

/// <summary>In-memory <see cref="IFolderStateStore"/>: the tests never read or write the real state.json.</summary>
sealed class MemoryStore : IFolderStateStore
{
    public MemoryStore(AppStateDocument doc) => Saved = doc;

    public AppStateDocument Saved { get; private set; }

    public int SaveCount { get; private set; }

    public Task<AppStateDocument> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);

    public Task SaveAsync(AppStateDocument state, CancellationToken cancellationToken = default)
    {
        Saved = state;
        SaveCount++;
        return Task.CompletedTask;
    }
}
