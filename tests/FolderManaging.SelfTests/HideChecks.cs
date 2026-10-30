// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Text.Json;
using FolderManaging.Core;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.SelfTests;

/// <summary>
/// Hiding a built-in or scheme color (2.2.1). Runs on an in-memory document, and on a temp state.json
/// (FM_DATA_DIR) when it checks that an older file still loads. Never the real state.json.
/// </summary>
static partial class Program
{
    static readonly JsonSerializerOptions HideJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>What 2.2.0 knew. Extra JSON fields must not stop this shape from loading.</summary>
    sealed class StateAs220
    {
        public int Version { get; set; }
        public List<FolderStateEntry> Folders { get; set; } = new();
        public List<string> RecentColors { get; set; } = new();
        public List<MenuColorPreset> MenuColors { get; set; } = new();
        public List<string> EnabledColorSchemes { get; set; } = new();
    }

    static void HideChecks()
    {
        Check("hide: a built-in color leaves the menu and comes back; a custom color is deleted, not hidden", () =>
        {
            var doc = new AppStateDocument();
            MenuPresetNormalizer.Merge(doc);
            doc.MenuColors.Add(new MenuColorPreset { Id = "custom-01", Label = "Mine", HexRgb = "112233" });
            doc.Folders.Add(new FolderStateEntry { Path = @"C:\FmSelfTest\Red", ColorToken = "red", Id = "abc" });
            doc.RememberRecentColor("red");

            Expect(HiddenColors.CanHide("red") && !HiddenColors.CanDelete("red"), "built-in can be deleted");
            Expect(HiddenColors.CanDelete("custom-01") && !HiddenColors.CanHide("custom-01"), "custom color can be hidden");
            Expect(!HiddenColors.CanDelete("scheme-vivid-red") && HiddenColors.CanHide("scheme-vivid-red"), "scheme color can be deleted");

            HiddenColors.Hide(doc, "custom-01");
            Expect(!HiddenColors.IsHidden(doc, "custom-01"), "custom color was hidden");

            HiddenColors.Hide(doc, "red");
            Expect(HiddenColors.IsHidden(doc, "RED"), "hide ignored case");
            Expect(doc.Folders.Single().ColorToken == "red" && doc.Folders.Single().Id == "abc", "painted folder changed");
            Expect(doc.RecentColors.Contains("red"), "recent history was cleared");
            Expect(doc.MenuColors.Any(c => c.Id == "red"), "the color row was removed");
            var lines = ContextMenuPlan.Build(doc);
            Expect(!lines.Any(l => l.Token == "red"), "hidden color still offered");
            Expect(lines.Any(l => l.Token == "custom-01"), "custom color left the menu");

            HiddenColors.Restore(doc, "Red");
            Expect(!HiddenColors.IsHidden(doc, "red"), "restore failed");
            Expect(ContextMenuPlan.Build(doc).Any(l => l.Kind == "color" && l.Token == "red"), "restored color missing from the menu");
        });

        Check("hide: a scheme color stays hidden when the scheme is turned off and on", () =>
        {
            UiText.Use(UiText.English);
            var doc = new AppStateDocument();
            MenuPresetNormalizer.Merge(doc);
            ColorSchemes.SetEnabled(doc, "vivid", true);
            doc.Folders.Add(new FolderStateEntry { Path = @"C:\FmSelfTest\Vivid", ColorToken = "scheme-vivid-red" });
            doc.RememberRecentColor("scheme-vivid-red");
            HiddenColors.Hide(doc, "scheme-vivid-red");
            HiddenColors.Hide(doc, "purple");

            ColorSchemes.SetEnabled(doc, "vivid", false);
            MenuPresetNormalizer.Merge(doc);
            ColorSchemes.SetEnabled(doc, "vivid", true);
            MenuPresetNormalizer.Merge(doc);

            Expect(doc.HiddenColorIds.Contains("scheme-vivid-red") && doc.HiddenColorIds.Contains("purple"),
                $"hidden list changed: {string.Join(",", doc.HiddenColorIds)}");
            Expect(doc.Folders.Single().ColorToken == "scheme-vivid-red", "scheme folder was repainted");
            var lines = ContextMenuPlan.Build(doc);
            Expect(!lines.Any(l => l.Token is "scheme-vivid-red" or "purple"), "a hidden color came back");
            Expect(!lines.Any(l => l.Kind == "recent" && l.Token == "scheme-vivid-red"), "recent row still offers it");
            Expect(lines.Any(l => l.VerbId == "scheme-vivid") && lines.Any(l => l.Token == "scheme-vivid-blue"), "the rest of the scheme disappeared");
            Expect(ContextMenuPlan.ColorName(doc, "scheme-vivid-red")?.Contains("Vivid") == true, "a painted folder lost its color name");
        });

        Check("hide: an old state file without the field loads with nothing hidden", () =>
        {
            var doc = JsonSerializer.Deserialize<AppStateDocument>(StateFrom210, HideJson)!;
            Expect(doc.HiddenColorIds is { Count: 0 }, "missing field became a hidden color");
            MenuPresetNormalizer.Merge(doc);
            Expect(doc.HiddenColorIds.Count == 0, "merge hid a color");
            Expect(ContextMenuPlan.Build(doc).Any(l => l.Token == "red"), "built-in color missing");
            Expect(doc.Folders[0].ColorToken == "red" && doc.Folders.Count == 10, "old folders changed");

            doc.HiddenColorIds.Add("red");
            var newer = JsonSerializer.Serialize(doc, HideJson);
            var as220 = JsonSerializer.Deserialize<StateAs220>(newer, HideJson)!;
            Expect(as220.Version == 2 && as220.Folders.Count == 10 && as220.Folders[0].ColorToken == "red", "2.2.0 could not read the file");
            Expect(as220.MenuColors.Any(c => c.Id == "red"), "2.2.0 lost the color row");
        });

        Check("hide: export carries the list; an older export leaves it alone", () =>
        {
            var doc = new AppStateDocument();
            MenuPresetNormalizer.Merge(doc);
            HiddenColors.Hide(doc, "blue");
            ColorSchemes.SetEnabled(doc, "ocean", true);
            HiddenColors.Hide(doc, "scheme-ocean-blue");
            var json = SettingsTransfer.Export(doc, "2.2.1");
            Expect(json.Contains("\"hiddenColorIds\""), "not exported");

            var target = new AppStateDocument();
            HiddenColors.Hide(target, "green");
            SettingsTransfer.Apply(target, SettingsTransfer.Parse(json), _ => false);
            Expect(target.HiddenColorIds.SequenceEqual(["blue", "scheme-ocean-blue"]),
                $"imported: {string.Join(",", target.HiddenColorIds)}");

            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            node.Remove("hiddenColorIds");
            HiddenColors.Hide(target, "red");
            SettingsTransfer.Apply(target, SettingsTransfer.Parse(node.ToJsonString()), _ => false);
            Expect(target.HiddenColorIds.Contains("red") && target.HiddenColorIds.Contains("blue"), "an older export cleared hidden colors");
        });

        WithTempState(dir =>
        {
            Check("hide: save and load keeps the hidden ids and the painted folder", () =>
            {
                File.WriteAllText(Path.Combine(dir, "state.json"), StateFrom210, new System.Text.UTF8Encoding(false));
                var doc = JsonFolderStateStore.LoadSynchronously();
                Expect(doc.HiddenColorIds.Count == 0 && doc.Folders[0].ColorToken == "red", "old file did not load");
                var before = doc.Folders[0].ColorToken;
                HiddenColors.Hide(doc, "red");
                ColorSchemes.SetEnabled(doc, "status", true);
                HiddenColors.Hide(doc, "scheme-status-red");
                JsonFolderStateStore.SaveSynchronously(doc);

                Expect(!File.Exists(Path.Combine(dir, StateMigration.BackupFileName)), "saving hidden colors wrote a v1 backup");
                Expect(!Directory.EnumerateFiles(dir, "desktop.ini", SearchOption.AllDirectories).Any(), "a desktop.ini was written");

                MenuStateReader.Invalidate();
                Expect(MenuStateReader.TryGetMenuTint("red", out var redTint) && MenuColorHex.ToHexRgb(redTint) == "FF7256", "painted red lost its tint");
                Expect(MenuStateReader.TryGetMenuTint("scheme-status-red", out var schemeTint) && MenuColorHex.ToHexRgb(schemeTint) == "D13438", "painted scheme color lost its tint");
                var again = JsonFolderStateStore.LoadSynchronously();
                Expect(again.Folders[0].ColorToken == before && again.Folders.Count == 10, "folders changed on disk");
                Expect(again.HiddenColorIds.Contains("red") && again.HiddenColorIds.Contains("scheme-status-red"),
                    $"loaded hidden: {string.Join(",", again.HiddenColorIds)}");
                ColorSchemes.SetEnabled(again, "status", false);
                ColorSchemes.SetEnabled(again, "status", true);
                var lines = ContextMenuPlan.Build(again);
                Expect(!lines.Any(l => l.Token is "red" or "scheme-status-red"), "reload offered a hidden color");
                Expect(lines.Any(l => l.Token == "scheme-status-green"), "the rest of the scheme stayed hidden");
                Expect(again.RecentColors.Contains("red"), "recent history dropped the hidden color");
                Expect(!lines.Any(l => l.Kind == "recent" && l.Token == "red"), "recent menu row offers a hidden color");
            });
        });

        UiText.Use(UiText.English);
    }
}
