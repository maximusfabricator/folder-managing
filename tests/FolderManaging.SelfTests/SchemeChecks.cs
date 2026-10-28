// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Text;
using FolderManaging.Core;
using FolderManaging.Core.Models;
using FolderManaging.Core.ShellProperties;
using FolderManaging.Core.Storage;

namespace FolderManaging.SelfTests;

/// <summary>
/// Checks for the built-in color schemes (2.1.1). The load and save checks run on a state.json in a temp folder
/// (FM_DATA_DIR, developer builds only) and refuse to run when the app would use the real one.
/// </summary>
static partial class Program
{
    /// <summary>Ids folders store in state.json and desktop.ini. They must never change.</summary>
    static readonly string[] SchemeColorIds =
    [
        "scheme-vivid-red", "scheme-vivid-orange", "scheme-vivid-yellow", "scheme-vivid-green", "scheme-vivid-blue", "scheme-vivid-purple",
        "scheme-pastel-pink", "scheme-pastel-orange", "scheme-pastel-yellow", "scheme-pastel-green", "scheme-pastel-blue", "scheme-pastel-purple",
        "scheme-earth-brown", "scheme-earth-orange", "scheme-earth-yellow", "scheme-earth-green", "scheme-earth-teal", "scheme-earth-gray",
        "scheme-ocean-indigo", "scheme-ocean-blue", "scheme-ocean-cyan", "scheme-ocean-teal", "scheme-ocean-green", "scheme-ocean-lime",
        "scheme-status-red", "scheme-status-orange", "scheme-status-yellow", "scheme-status-green", "scheme-status-gray",
    ];

    /// <summary>A settings file as 2.1.0 wrote it: a custom menu without the new block and five empty folder entries.</summary>
    const string StateFrom210 = """
        {
          "version": 2,
          "folders": [
            { "path": "Q:\\FmSelfTest\\Red", "id": "11111111111111111111111111111111", "colorToken": "red", "tags": [], "isPinned": false },
            { "path": "Q:\\FmSelfTest\\Mint", "id": "22222222222222222222222222222222", "colorToken": "custom-01", "tags": [ "work" ], "isPinned": true },
            { "path": "Q:\\FmSelfTest\\Wheel", "id": "33333333333333333333333333333333", "colorToken": "rgb-3a86ff", "tags": [], "isPinned": false },
            { "path": "Q:\\FmSelfTest\\Tagged", "id": "44444444444444444444444444444444", "colorToken": null, "tags": [ "later", "archive" ], "isPinned": false },
            { "path": "Q:\\FmSelfTest\\Pinned", "id": null, "colorToken": null, "tags": [], "isPinned": true },
            { "path": "Q:\\FmSelfTest\\Empty1", "id": null, "colorToken": null, "tags": [], "isPinned": false },
            { "path": "Q:\\FmSelfTest\\Empty2", "id": null, "colorToken": null, "tags": [], "isPinned": false },
            { "path": "Q:\\FmSelfTest\\Empty3", "id": null, "colorToken": null, "tags": [], "isPinned": false },
            { "path": "Q:\\FmSelfTest\\Empty4", "id": null, "colorToken": null, "tags": [], "isPinned": false },
            { "path": "Q:\\FmSelfTest\\Empty5", "id": null, "colorToken": null, "tags": [], "isPinned": false }
          ],
          "recentColors": [ "custom-01", "rgb-3a86ff", "red" ],
          "menuColors": [
            { "id": "red", "label": "", "hexRgb": "FF7256" },
            { "id": "blue", "label": "", "hexRgb": "007ACC" },
            { "id": "green", "label": "", "hexRgb": "48C78E" },
            { "id": "purple", "label": "", "hexRgb": "8E44AD" },
            { "id": "custom-01", "label": "Blue (Ocean)", "hexRgb": "0F6CBD" }
          ],
          "menuTags": [
            { "id": "work", "label": "Work", "hexRgb": "0078D4" },
            { "id": "later", "label": "Later", "hexRgb": "FFB900" },
            { "id": "archive", "label": "Archive", "hexRgb": "8A8886" }
          ],
          "defaultTagsSeeded": true,
          "autoElevateProtectedFolders": false,
          "theme": 2,
          "language": "ru",
          "contextMenu": {
            "rootTitle": "Папки",
            "entries": [
              { "kind": "quick-color", "visible": true, "label": null },
              { "kind": "pin", "visible": true, "label": null },
              { "kind": "color-presets", "visible": true, "label": null },
              { "kind": "system-palette", "visible": false, "label": null },
              { "kind": "reset", "visible": true, "label": "Сброс" },
              { "kind": "tags", "visible": true, "label": null },
              { "kind": "settings", "visible": true, "label": null }
            ]
          },
          "colorSortTipShown": true
        }
        """;

    static void SchemeChecks()
    {
        Check("color schemes: ids are the ones folders stored, unique and apart from other colors", () =>
        {
            var ids = ColorSchemes.All.SelectMany(s => s.Colors).Select(c => c.Id).ToList();
            Expect(ids.SequenceEqual(SchemeColorIds), $"ids: {string.Join(",", ids)}");
            Expect(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Count, "duplicate id");
            Expect(ColorSchemes.All.Select(s => s.Id).SequenceEqual(["vivid", "pastel", "earth", "ocean", "status"]), "scheme ids changed");
            Expect(ids.All(id => !FolderColors.PresetRgb.ContainsKey(id) && !id.StartsWith("custom-") && !id.StartsWith("rgb-")), "clash with another kind of id");
            Expect(ColorSchemes.All.SelectMany(s => s.Colors).All(c => MenuColorHex.TryParse(c.HexRgb, out _)), "bad scheme color");
            Expect(ColorSchemes.TryGetColor("SCHEME-OCEAN-BLUE", out var blue) && MenuColorHex.ToHexRgb(blue) == "0F6CBD", "lookup ignores case");
            Expect(!ColorSchemes.TryGetColor("scheme-ocean", out _) && !ColorSchemes.TryGetColor("red", out _), "a non-scheme id resolved");
        });

        Check("color schemes: names follow the language, ids do not", () =>
        {
            try
            {
                ColorSchemes.TryFindColor("scheme-ocean-blue", out var scheme, out var color);
                Expect(ColorSchemes.DisplayName(scheme, color) == "Blue (Ocean)", $"en: {ColorSchemes.DisplayName(scheme, color)}");
                FolderManaging.Core.Localization.UiText.Use("ru");
                Expect(ColorSchemes.DisplayName(scheme, color) == "Синий (Морская)", $"ru: {ColorSchemes.DisplayName(scheme, color)}");
                Expect(color.Id == "scheme-ocean-blue", "id changed with the language");
            }
            finally
            {
                FolderManaging.Core.Localization.UiText.Use(FolderManaging.Core.Localization.UiText.English);
            }
        });

        Check("color schemes: off for new users; turning on and off keeps unknown ids", () =>
        {
            var doc = new AppStateDocument();
            MenuPresetNormalizer.Merge(doc);
            Expect(doc.EnabledColorSchemes.Count == 0 && ColorSchemes.Enabled(doc).Count == 0, "a scheme is on by default");
            doc.EnabledColorSchemes.Add("from-a-newer-version");
            ColorSchemes.SetEnabled(doc, "status", true);
            ColorSchemes.SetEnabled(doc, "OCEAN", true);
            ColorSchemes.SetEnabled(doc, "ocean", true);
            Expect(ColorSchemes.Enabled(doc).Select(s => s.Id).SequenceEqual(["ocean", "status"]), $"enabled: {string.Join(",", doc.EnabledColorSchemes)}");
            ColorSchemes.SetEnabled(doc, "status", false);
            Expect(doc.EnabledColorSchemes.SequenceEqual(["from-a-newer-version", "ocean"]), $"after off: {string.Join(",", doc.EnabledColorSchemes)}");
        });

        Check("menu: schemes off change nothing; on, each scheme is a heading and its colors", () =>
        {
            var doc = new AppStateDocument();
            MenuPresetNormalizer.Merge(doc);
            var before = ContextMenuPlan.Build(doc);
            Expect(before.All(l => !l.VerbId.StartsWith("scheme-") && l.Token?.StartsWith(ColorSchemes.IdPrefix) != true), "scheme rows while off");

            foreach (var scheme in ColorSchemes.All)
                ColorSchemes.SetEnabled(doc, scheme.Id, true);
            var lines = ContextMenuPlan.Build(doc);
            var headings = lines.Where(l => l.VerbId.StartsWith("scheme-")).ToList();
            var colors = lines.Where(l => l.Token?.StartsWith(ColorSchemes.IdPrefix) == true).ToList();
            Expect(headings.Count == 5 && headings.All(h => h.Disabled && h.Icon == MenuLineIcon.None), $"headings: {headings.Count}");
            Expect(colors.Select(c => c.Token).SequenceEqual(SchemeColorIds) && colors.All(c => c.Kind == "color" && c.Icon == MenuLineIcon.Preset),
                "scheme color rows");
            Expect(lines.Count == before.Count + 5 + SchemeColorIds.Length, $"rows: {before.Count} -> {lines.Count}");
            Expect(lines.Select(l => l.VerbId).Distinct().Count() == lines.Count, "two rows share a verb id");
            var firstHeading = lines.ToList().IndexOf(headings[0]);
            Expect(lines[firstHeading - 1].Token == "purple", $"schemes do not follow My colors: {lines[firstHeading - 1].Title}");
            Expect(headings[0].Title.Contains("Vivid") && colors[0].Title.Contains("Red"), $"titles: {headings[0].Title} / {colors[0].Title}");
            Expect(lines.Count(l => l.Disabled) == 5 + before.Count(l => l.Disabled), "a separator was added next to a heading");

            doc.ContextMenu = doc.GetContextMenuLayout();
            doc.ContextMenu.Entries.Single(e => e.Kind == ContextMenuItemKinds.ColorSchemes).Visible = false;
            Expect(ContextMenuPlan.Build(doc).Count == before.Count, "hidden block still shows rows");
        });

        Check("menu: a scheme right after a separator gets no second divider", () =>
        {
            var doc = new AppStateDocument();
            ColorSchemes.SetEnabled(doc, "status", true);
            doc.ContextMenu = new ContextMenuLayout
            {
                Entries =
                {
                    new() { Kind = ContextMenuItemKinds.Reset },
                    new() { Kind = ContextMenuItemKinds.Separator },
                    new() { Kind = ContextMenuItemKinds.ColorSchemes },
                    new() { Kind = ContextMenuItemKinds.Separator },
                    new() { Kind = ContextMenuItemKinds.Settings },
                },
            };
            var kinds = ContextMenuPlan.Build(doc).Select(l => l.VerbId).ToList();
            Expect(kinds.Take(3).SequenceEqual(["reset-color", "scheme-status", "color-scheme-status-red"]), $"rows: {string.Join(",", kinds)}");
            Expect(kinds.IndexOf("sep-0") == kinds.IndexOf("settings") - 1 && kinds.Count(k => k.StartsWith("sep-")) == 1,
                $"rows: {string.Join(",", kinds)}");
        });

        Check("menu layout: the color schemes block joins a 2.1.0 layout right after My colors", () =>
        {
            var saved = new ContextMenuLayout
            {
                Entries =
                {
                    new() { Kind = ContextMenuItemKinds.Settings },
                    new() { Kind = ContextMenuItemKinds.ColorPresets },
                    new() { Kind = ContextMenuItemKinds.QuickColor },
                },
            };
            var kinds = ContextMenuLayout.Normalize(saved).Entries.Select(e => e.Kind).ToList();
            Expect(kinds.IndexOf(ContextMenuItemKinds.ColorSchemes) == kinds.IndexOf(ContextMenuItemKinds.ColorPresets) + 1, $"kinds: {string.Join(",", kinds)}");
            Expect(kinds[0] == ContextMenuItemKinds.Settings, $"order changed: {string.Join(",", kinds)}");
        });

        Check("export/import: the schemes that are on travel; a 2.1.0 file leaves them alone", () =>
        {
            var source = new AppStateDocument();
            ColorSchemes.SetEnabled(source, "earth", true);
            ColorSchemes.SetEnabled(source, "ocean", true);
            source.Folders.Add(new FolderStateEntry { Path = @"C:\Fm\Here", ColorToken = "scheme-earth-teal" });
            var json = SettingsTransfer.Export(source, "2.1.1");
            Expect(json.Contains("\"enabledColorSchemes\""), "not in the file");

            var target = new AppStateDocument();
            ColorSchemes.SetEnabled(target, "status", true);
            SettingsTransfer.Apply(target, SettingsTransfer.Parse(json), _ => true);
            Expect(target.EnabledColorSchemes.SequenceEqual(["earth", "ocean"]), $"imported: {string.Join(",", target.EnabledColorSchemes)}");
            Expect(target.Folders.Single().ColorToken == "scheme-earth-teal", "folder color lost");

            var old = "{\"format\":\"FolderManaging.Settings\",\"version\":1,\"appVersion\":\"2.1.0\",\"menuColors\":[],\"menuTags\":[],\"folders\":[]}";
            SettingsTransfer.Apply(target, SettingsTransfer.Parse(old), _ => true);
            Expect(target.EnabledColorSchemes.SequenceEqual(["earth", "ocean"]), "a 2.1.0 file turned schemes off");
        });

        WithTempState(dir =>
        {
            var state = Path.Combine(dir, "state.json");

            Check("upgrade: a 2.1.0 state.json keeps everything and gets no scheme", () =>
            {
                File.WriteAllText(state, StateFrom210, new UTF8Encoding(false));
                var doc = JsonFolderStateStore.LoadSynchronously();
                ExpectSame210(doc);
                Expect(doc.EnabledColorSchemes.Count == 0, "a scheme was turned on");
                var kinds = doc.GetContextMenuLayout().Entries.Select(e => e.Kind).ToList();
                Expect(kinds.IndexOf(ContextMenuItemKinds.ColorSchemes) == kinds.IndexOf(ContextMenuItemKinds.ColorPresets) + 1, $"kinds: {string.Join(",", kinds)}");

                JsonFolderStateStore.SaveSynchronously(doc);
                Expect(!File.Exists(Path.Combine(dir, StateMigration.BackupFileName)), "a 2.1.0 file was backed up as old");
                var again = JsonFolderStateStore.LoadSynchronously();
                ExpectSame210(again);
                Expect(File.ReadAllText(state).Contains("\"enabledColorSchemes\": []"), "new property not written");
            });

            Check("state: the schemes that are on survive save and load", () =>
            {
                var doc = JsonFolderStateStore.LoadSynchronously();
                ColorSchemes.SetEnabled(doc, "pastel", true);
                ColorSchemes.SetEnabled(doc, "status", true);
                JsonFolderStateStore.SaveSynchronously(doc);
                var again = JsonFolderStateStore.LoadSynchronously();
                Expect(again.EnabledColorSchemes.SequenceEqual(["pastel", "status"]), $"loaded: {string.Join(",", again.EnabledColorSchemes)}");
                Expect(again.Folders.Count == 10, $"folders: {again.Folders.Count}");
            });

            Check("scheme color: a folder keeps resolving after its scheme is turned off", () =>
            {
                var doc = JsonFolderStateStore.LoadSynchronously();
                ColorSchemes.SetEnabled(doc, "ocean", true);
                doc.Folders.Single(f => f.Path.EndsWith("Empty1")).ColorToken = "scheme-ocean-blue";
                doc.RememberRecentColor("scheme-ocean-blue");
                ColorSchemes.SetEnabled(doc, "ocean", false);
                JsonFolderStateStore.SaveSynchronously(doc);
                MenuStateReader.Invalidate();

                Expect(MenuStateReader.TryGetMenuTint("scheme-ocean-blue", out var tint) && MenuColorHex.ToHexRgb(tint) == "0F6CBD", "tint lost");
                Expect(FolderColorHueProperty.FromColor(tint) == FolderColorHueProperty.FromColor(System.Drawing.Color.FromArgb(0x0F, 0x6C, 0xBD)), "sort hue differs");
                var loaded = MenuStateReader.GetMerged();
                FolderManaging.Core.Localization.UiText.Use(FolderManaging.Core.Localization.UiText.English);
                var lines = ContextMenuPlan.Build(loaded);
                Expect(!lines.Any(l => l.Kind == "color" && l.Token == "scheme-ocean-blue"), "turned-off scheme still in the menu");
                Expect(lines.Any(l => l.Kind == "recent" && l.Token == "scheme-ocean-blue" && l.Title.Contains("Blue (Ocean)")), "recent row lost its name");
                Expect(ContextMenuPlan.ColorName(loaded, "scheme-ocean-blue") == "Blue (Ocean)", "no name for the folder list");
            });
        });
    }

    static void ExpectSame210(AppStateDocument doc)
    {
        Expect(doc.Version == AppStateDocument.CurrentVersion, $"version {doc.Version}");
        Expect(doc.Folders.Count == 10, $"folders: {doc.Folders.Count}");
        Expect(doc.Folders.Count(f => f.Path.Contains("Empty") && f.ColorToken is null && f.Tags.Count == 0 && !f.IsPinned) == 5, "an empty entry changed");
        Expect(doc.Folders[0].ColorToken == "red" && doc.Folders[0].Id == "11111111111111111111111111111111", "red folder changed");
        Expect(doc.Folders[1].ColorToken == "custom-01" && doc.Folders[1].IsPinned && doc.Folders[1].Tags.SequenceEqual(["work"]), "pinned folder changed");
        Expect(doc.Folders[2].ColorToken == "rgb-3a86ff" && doc.Folders[3].Tags.SequenceEqual(["later", "archive"]), "folder colors or tags changed");
        Expect(doc.RecentColors.SequenceEqual(["custom-01", "rgb-3a86ff", "red"]), $"recent: {string.Join(",", doc.RecentColors)}");
        Expect(doc.MenuColors.Count == 5 && doc.MenuColors.Single(c => c.Id == "custom-01") is { Label: "Blue (Ocean)", HexRgb: "0F6CBD" }, "my colors changed");
        Expect(doc.MenuColors.Single(c => c.Id == "red").HexRgb == "FF7256", "built-in color changed");
        Expect(doc.MenuTags.Select(t => t.Id).SequenceEqual(["work", "later", "archive"]), "tags changed");
        Expect(!doc.AutoElevateProtectedFolders && doc.Theme == AppThemePreference.Dark && doc.Language == "ru" && doc.ColorSortTipShown, "options changed");
        var layout = doc.GetContextMenuLayout();
        Expect(layout.RootTitle == "Папки" && layout.Entries.Single(e => e.Kind == ContextMenuItemKinds.Reset).Label == "Сброс", "menu names changed");
        Expect(!layout.Entries.Single(e => e.Kind == ContextMenuItemKinds.SystemPalette).Visible, "hidden block shown");
    }

    /// <summary>Runs <paramref name="body"/> with the app's data folder pointed at a new temp folder, deleted afterwards.</summary>
    static void WithTempState(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "fm-selftest-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("FM_DATA_DIR");
        Environment.SetEnvironmentVariable("FM_DATA_DIR", dir);
        try
        {
            MenuStateReader.Invalidate();
            if (!string.Equals(Path.GetDirectoryName(AppPaths.StateFilePath), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            {
                Check("state checks need a developer build (FM_DATA_DIR)", () => Expect(false, "the app would use the real state.json; skipped"));
                return;
            }

            body(dir);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FM_DATA_DIR", previous);
            MenuStateReader.Invalidate();
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
