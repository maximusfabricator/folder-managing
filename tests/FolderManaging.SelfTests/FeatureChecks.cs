// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Text;
using FolderManaging.Core;
using FolderManaging.Core.Models;
using FolderManaging.Core.QuickAccess;
using FolderManaging.Core.Storage;

namespace FolderManaging.SelfTests;

/// <summary>Checks for the 2.1 features. Quick access, the elevated worker and the disk are fakes, except one temp folder.</summary>
static partial class Program
{
    const FileAttributes Plain = FileAttributes.Directory;
    const FileAttributes Colored = FileAttributes.Directory | FileAttributes.ReadOnly;

    static void FeatureChecks()
    {
        Check("batch: protected folders share one confirmation and one worker start", () =>
        {
            var host = new FakeBatchHost(denied: [@"C:\Program Files\A", @"C:\Program Files\B"]);
            var asked = new List<IReadOnlyList<string>>();
            var actions = new[] { @"C:\Work", @"C:\Program Files\A", @"C:\Program Files\B" }
                .Select(p => new FolderAction(FolderActionKind.Color, p, "red")).ToList();
            var result = FolderBatch.Run(actions, paths => { asked.Add(paths); return true; }, host);
            Expect(asked.Count == 1 && asked[0].Count == 2, $"confirmations: {asked.Count}");
            Expect(host.Starts == 1, $"worker starts: {host.Starts}");
            Expect(result.AllDone && result.Done.Count == 3, $"done: {result.Done.Count}, failed: {result.Failed.Count}");
        });

        Check("batch: saying no changes only the folders that need no rights", () =>
        {
            var host = new FakeBatchHost(denied: [@"C:\Program Files\A"]);
            var actions = new[] { @"C:\Work", @"C:\Program Files\A" }
                .Select(p => new FolderAction(FolderActionKind.Reset, p)).ToList();
            var result = FolderBatch.Run(actions, _ => false, host);
            Expect(result.ElevationDeclined && host.Starts == 0, "the worker was started");
            Expect(result.Done.SequenceEqual([@"C:\Work"]), $"done: {string.Join(";", result.Done)}");
            Expect(result.Failed.Single().Path == @"C:\Program Files\A", "wrong failure");
        });

        Check("batch: a folder asked twice (repair) is listed once", () =>
        {
            var host = new FakeBatchHost(denied: [@"C:\Program Files\A"]);
            var actions = new List<FolderAction>
            {
                new(FolderActionKind.Sync, @"C:\Program Files\A", "unblock"),
                new(FolderActionKind.Sync, @"C:\Program Files\A", "force"),
            };
            IReadOnlyList<string> asked = [];
            FolderBatch.Run(actions, paths => { asked = paths; return true; }, host);
            Expect(asked.Count == 1, $"listed: {asked.Count}");
            Expect(host.Elevated.Count == 2, $"elevated actions: {host.Elevated.Count}");
        });

        Check("quick access: stale means the pin missed the read-only flag", () =>
        {
            Expect(PinRefresher.IsStale(new QuickAccessPin(@"C:\A", Plain, true), new FolderSnapshot(Colored, true)), "gained flag not stale");
            Expect(!PinRefresher.IsStale(new QuickAccessPin(@"C:\A", Colored, true), new FolderSnapshot(Colored, true)), "fresh pin stale");
            Expect(PinRefresher.IsStale(new QuickAccessPin(@"C:\A", Colored, true), new FolderSnapshot(Plain, true)), "lost flag with desktop.ini not stale");
            Expect(!PinRefresher.IsStale(new QuickAccessPin(@"C:\A", Colored, true), new FolderSnapshot(Plain, false)), "reset folder stale");
        });

        Check("quick access: re-pin keeps the order and leaves known folders alone", () =>
        {
            var qa = new FakeQuickAccess(
                new QuickAccessPin(@"C:\Users\Me\Desktop", Plain, false),
                new QuickAccessPin(@"C:\B", Colored, true),
                new QuickAccessPin(@"C:\A", Plain, true),
                new QuickAccessPin(@"C:\C", Plain, true));
            qa.Live[@"C:\A"] = Colored;
            var journal = new FakeJournal();
            var result = PinRefresher.Refresh(qa, journal, qa.Snapshot, _ => true, PinRefresher.AutomaticTailLimit);
            Expect(result.Outcome == PinRefreshOutcome.Refreshed, $"outcome: {result.Outcome} {result.Reason}");
            Expect(qa.Order.SequenceEqual([@"C:\Users\Me\Desktop", @"C:\B", @"C:\A", @"C:\C"]), $"order: {string.Join(";", qa.Order)}");
            Expect(qa.Unpinned.Order().SequenceEqual([@"C:\A", @"C:\C"]), $"unpinned: {string.Join(";", qa.Unpinned)}");
            Expect(qa.Pins.Single(p => p.Path == @"C:\A").StoredAttributes.HasFlag(FileAttributes.ReadOnly), "pin still stale");
            Expect(journal.Saved is null, "journal left behind");
        });

        Check("quick access: a journal left by a crash is pinned back first, in order", () =>
        {
            var qa = new FakeQuickAccess(new QuickAccessPin(@"C:\Users\Me\Desktop", Plain, false));
            var journal = new FakeJournal { Saved = [@"C:\A", @"C:\C"] };
            var result = PinRefresher.Refresh(qa, journal, qa.Snapshot, _ => true, PinRefresher.AutomaticTailLimit);
            Expect(result.Outcome == PinRefreshOutcome.NothingToDo, $"outcome: {result.Outcome} {result.Reason}");
            Expect(qa.Order.SequenceEqual([@"C:\Users\Me\Desktop", @"C:\A", @"C:\C"]), $"order: {string.Join(";", qa.Order)}");
            Expect(journal.Saved is null, "journal not cleared");
        });

        Check("quick access: nothing is unpinned when a later pin cannot come back", () =>
        {
            var qa = new FakeQuickAccess(
                new QuickAccessPin(@"C:\A", Plain, true),
                new QuickAccessPin(@"\\server\share", Plain, false));
            qa.Live[@"C:\A"] = Colored;
            var result = PinRefresher.Refresh(qa, new FakeJournal(), qa.Snapshot, _ => true, PinRefresher.AutomaticTailLimit);
            Expect(result.Outcome == PinRefreshOutcome.Skipped && qa.Unpinned.Count == 0, $"outcome: {result.Outcome}, unpinned {qa.Unpinned.Count}");
        });

        Check("quick access: automatic runs stop at the tail limit", () =>
        {
            var pins = Enumerable.Range(0, PinRefresher.AutomaticTailLimit + 1)
                .Select(i => new QuickAccessPin($@"C:\P{i}", Plain, true)).ToArray();
            var qa = new FakeQuickAccess(pins);
            qa.Live[@"C:\P0"] = Colored;
            var result = PinRefresher.Refresh(qa, new FakeJournal(), qa.Snapshot, _ => true, PinRefresher.AutomaticTailLimit);
            Expect(result.Outcome == PinRefreshOutcome.Skipped && qa.Unpinned.Count == 0, $"outcome: {result.Outcome}");
        });

        Check("quick access: a failed pin keeps the journal for the next start", () =>
        {
            var qa = new FakeQuickAccess(
                new QuickAccessPin(@"C:\A", Plain, true),
                new QuickAccessPin(@"C:\B", Plain, true)) { RefusePin = @"C:\B" };
            qa.Live[@"C:\A"] = Colored;
            var journal = new FakeJournal();
            var result = PinRefresher.Refresh(qa, journal, qa.Snapshot, _ => true, PinRefresher.AutomaticTailLimit);
            Expect(result.Outcome == PinRefreshOutcome.Failed, $"outcome: {result.Outcome}");
            Expect(journal.Saved?.SequenceEqual([@"C:\A", @"C:\B"]) == true, "journal lost");
            qa.RefusePin = null;
            PinRefresher.Resume(qa, journal);
            Expect(qa.Order.SequenceEqual([@"C:\A", @"C:\B"]) && journal.Saved is null, $"after resume: {string.Join(";", qa.Order)}");
        });

        Check("quick access: unmanaged folders are never re-pinned", () =>
        {
            var qa = new FakeQuickAccess(new QuickAccessPin(@"C:\A", Plain, true));
            qa.Live[@"C:\A"] = Colored;
            var result = PinRefresher.Refresh(qa, new FakeJournal(), qa.Snapshot, _ => false, PinRefresher.AutomaticTailLimit);
            Expect(result.Outcome == PinRefreshOutcome.NothingToDo && qa.Unpinned.Count == 0, $"outcome: {result.Outcome}");
        });

        Check("rename: a renamed folder is found by its id, its subfolders follow", () =>
        {
            var doc = Doc(
                new FolderStateEntry { Path = @"C:\Fm\Old", Id = "id-1", ColorToken = "red" },
                new FolderStateEntry { Path = @"C:\Fm\Old\Inner", Id = "id-2", Tags = { "work" } },
                new FolderStateEntry { Path = @"C:\Fm\Empty" });
            var fs = new FakeTree(@"C:\", @"C:\Fm", @"C:\Fm\New", @"C:\Fm\New\Inner", @"C:\Fm\Empty");
            fs.Ids[@"C:\Fm\New"] = "id-1";
            var moved = FolderRelocator.FindMoved(doc, fs.Exists, fs.List, fs.ReadId);
            Expect(moved.Count == 1 && moved[0].To == @"C:\Fm\New", $"moved: {string.Join(";", moved)}");
            Expect(doc.Folders.Select(f => f.Path).SequenceEqual([@"C:\Fm\New", @"C:\Fm\New\Inner", @"C:\Fm\Empty"]),
                $"paths: {string.Join(";", doc.Folders.Select(f => f.Path))}");
        });

        Check("rename: a folder moved into a neighbor is found one level down", () =>
        {
            var doc = Doc(new FolderStateEntry { Path = @"C:\Fm\Photos", Id = "id-9", ColorToken = "blue" });
            var fs = new FakeTree(@"C:\", @"C:\Fm", @"C:\Fm\Archive", @"C:\Fm\Archive\Photos");
            fs.Ids[@"C:\Fm\Archive\Photos"] = "id-9";
            FolderRelocator.FindMoved(doc, fs.Exists, fs.List, fs.ReadId);
            Expect(doc.Folders.Single().Path == @"C:\Fm\Archive\Photos", $"path: {doc.Folders.Single().Path}");
        });

        Check("rename: a missing drive leaves the entry alone", () =>
        {
            var doc = Doc(new FolderStateEntry { Path = @"E:\Usb\Photos", Id = "id-3", ColorToken = "red" });
            var moved = FolderRelocator.FindMoved(doc, new FakeTree(@"C:\").Exists, _ => [], _ => "id-3");
            Expect(moved.Count == 0 && doc.Folders.Single().Path == @"E:\Usb\Photos", "entry moved");
        });

        Check("rename: Explorer's rename notice moves the entry unless the new path has one", () =>
        {
            var doc = Doc(
                new FolderStateEntry { Path = @"C:\Fm\A", ColorToken = "red" },
                new FolderStateEntry { Path = @"C:\Fm\B", ColorToken = "blue" });
            var moved = FolderRelocator.Relocate(doc, @"C:\Fm\A", @"C:\Fm\B");
            Expect(moved.Count == 0 && doc.Folders[0].Path == @"C:\Fm\A", "entry overwrote another");
            moved = FolderRelocator.Relocate(doc, @"C:\Fm\A\", @"C:\Fm\C");
            Expect(moved.SequenceEqual([@"C:\Fm\C"]), $"moved: {string.Join(";", moved)}");
        });

        Check("export/import: the round trip keeps colors, tags and folder colors", () =>
        {
            var source = new AppStateDocument();
            source.MenuColors.Add(new MenuColorPreset { Id = "custom-01", Label = "Mint", HexRgb = "98FF98" });
            source.MenuTags.Add(new MenuTagPreset { Id = "work", Label = "Work" });
            source.Theme = AppThemePreference.Dark;
            source.Folders.Add(new FolderStateEntry { Path = @"C:\Fm\Here", ColorToken = "custom-01", Tags = { "work", "gone" } });
            source.Folders.Add(new FolderStateEntry { Path = @"C:\Fm\Elsewhere", ColorToken = "red" });
            source.Folders.Add(new FolderStateEntry { Path = @"C:\Fm\PinnedOnly", IsPinned = true });

            var parsed = SettingsTransfer.Parse(SettingsTransfer.Export(source, "2.1.0"));
            Expect(parsed.Folders.Count == 2, $"exported folders: {parsed.Folders.Count}");

            var target = new AppStateDocument();
            target.MenuTags.Add(new MenuTagPreset { Id = "work", Label = "Arbeid" });
            var result = SettingsTransfer.Apply(target, parsed, p => p == @"C:\Fm\Here");
            Expect(target.MenuColors.Any(c => c.Id == "custom-01" && c.HexRgb == "98FF98"), "color not imported");
            Expect(target.MenuTags.Single(t => t.Id == "work").Label == "Work", "tag label not imported");
            Expect(target.Theme == AppThemePreference.Dark, "theme not imported");
            var entry = target.Folders.Single();
            Expect(entry.ColorToken == "custom-01" && entry.Tags.SequenceEqual(["work"]), $"folder: {entry.ColorToken} {string.Join(",", entry.Tags)}");
            Expect(result.Folders.Count == 1 && result.MissingFolders.SequenceEqual([@"C:\Fm\Elsewhere"]),
                $"changed {result.Folders.Count}, missing {string.Join(";", result.MissingFolders)}");
            Expect(SettingsTransfer.Apply(target, parsed, p => p == @"C:\Fm\Here").Folders.Count == 0, "second import changed the folder again");
        });

        Check("export/import: other JSON files are refused", () =>
        {
            foreach (var json in new[] { "{\"version\":2,\"folders\":[]}", "not json", "null" })
            {
                try
                {
                    SettingsTransfer.Parse(json);
                    throw new InvalidOperationException($"accepted: {json}");
                }
                catch (InvalidDataException)
                {
                }
            }
        });

        Check("state: an old file gets ids, keeps empty entries and agrees across processes", () =>
        {
            AppStateDocument Old() => new()
            {
                Version = 1,
                Folders =
                {
                    new FolderStateEntry { Path = @"C:\Fm\Red", ColorToken = "red" },
                    new FolderStateEntry { Path = @"C:\Fm\Empty" },
                    new FolderStateEntry { Path = @"C:\Fm\Ini", Tags = { "work" } },
                },
            };
            var first = Old();
            var second = Old();
            Func<string, string?> ini = p => p == @"C:\Fm\Ini" ? "from-ini" : null;
            Expect(StateMigration.Upgrade(first, ini) && StateMigration.Upgrade(second, ini), "not upgraded");
            Expect(first.Version == AppStateDocument.CurrentVersion, $"version {first.Version}");
            Expect(first.Folders.Count == 3 && first.Folders[1].Id is null, "empty entry dropped or given an id");
            Expect(first.Folders[0].Id == second.Folders[0].Id && first.Folders[0].Id == StateMigration.IdFromPath(@"C:\Fm\Red"), "ids differ");
            Expect(first.Folders[2].Id == "from-ini", $"ini id: {first.Folders[2].Id}");
            Expect(!StateMigration.Upgrade(first, ini), "upgraded twice");
        });

        Check("state: the old file is copied byte for byte once, before the first save", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "fm-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var state = Path.Combine(dir, "state.json");
                var backup = Path.Combine(dir, StateMigration.BackupFileName);
                var v1 = Encoding.UTF8.GetBytes("{\r\n  \"version\": 1,\r\n  \"folders\": [ { \"path\": \"C:\\\\Fm\\\\\u00c6\u00d8\u00c5\" } ]\r\n}");
                File.WriteAllBytes(state, v1);
                StateMigration.BackupIfOlder(state);
                Expect(File.Exists(backup) && File.ReadAllBytes(backup).SequenceEqual(v1), "backup missing or different");
                File.WriteAllBytes(state, Encoding.UTF8.GetBytes("{\"version\": 1, \"folders\": []}"));
                StateMigration.BackupIfOlder(state);
                Expect(File.ReadAllBytes(backup).SequenceEqual(v1), "backup overwritten");

                File.Delete(backup);
                File.WriteAllBytes(state, Encoding.UTF8.GetBytes("{\"version\": 2}"));
                StateMigration.BackupIfOlder(state);
                Expect(!File.Exists(backup), "backup of a current file");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        });

        Check("hex: codes with #, 0x, short form and any case", () =>
        {
            var good = new (string Text, string Hex)[]
            {
                ("#3A86FF", "3A86FF"), ("3a86ff", "3A86FF"), ("0x3A86FF", "3A86FF"), (" #abc ", "AABBCC"), ("# 102030", "102030"),
            };
            foreach (var (text, hex) in good)
                Expect(MenuColorHex.TryParse(text, out var c) && MenuColorHex.ToHexRgb(c) == hex, $"'{text}' not {hex}");
            foreach (var text in new[] { "", "   ", "#12345", "GGGGGG", "#1234567", "12 34 56", "+12345" })
                Expect(!MenuColorHex.TryParse(text, out _), $"'{text}' accepted");
        });

        Check("recent colors: newest first, no doubles, at most five", () =>
        {
            var doc = new AppStateDocument();
            foreach (var token in new[] { "red", "rgb-112233", "blue", "RED", "green", "a", "b" })
                doc.RememberRecentColor(token);
            Expect(doc.RecentColors.SequenceEqual(["b", "a", "green", "red", "blue"]), $"recent: {string.Join(",", doc.RecentColors)}");
        });

        Check("color schemes: adding twice adds nothing new; Save to my colors reuses a color", () =>
        {
            var doc = new AppStateDocument();
            var scheme = ColorSchemes.All.First(s => s.Id == "pastel");
            var added = ColorSchemes.AddTo(doc, scheme, (s, c) => $"{c} ({s})");
            Expect(added == scheme.Colors.Count && ColorSchemes.AddTo(doc, scheme, (s, c) => "") == 0, $"added {added}");
            var id = ColorSchemes.SaveToMyColors(doc, "#" + scheme.Colors[0].HexRgb.ToLowerInvariant());
            Expect(id == doc.MenuColors[0].Id && doc.MenuColors.Count == scheme.Colors.Count, "duplicate color saved");
            Expect(ColorSchemes.All.All(s => s.Colors.All(c => MenuColorHex.TryParse(c.HexRgb, out _))), "bad scheme color");
        });

        Check("menu layout: Recent colors joins a saved layout right after Quick color", () =>
        {
            var saved = new ContextMenuLayout
            {
                Entries =
                {
                    new() { Kind = ContextMenuItemKinds.Settings },
                    new() { Kind = ContextMenuItemKinds.QuickColor },
                    new() { Kind = ContextMenuItemKinds.ColorPresets },
                },
            };
            var kinds = ContextMenuLayout.Normalize(saved).Entries.Select(e => e.Kind).ToList();
            var quick = kinds.IndexOf(ContextMenuItemKinds.QuickColor);
            Expect(kinds[0] == ContextMenuItemKinds.Settings, $"order changed: {string.Join(",", kinds)}");
            Expect(kinds.IndexOf(ContextMenuItemKinds.RecentColors) == quick + 1, $"kinds: {string.Join(",", kinds)}");
        });
    }
}

sealed class FakeBatchHost(IReadOnlyList<string> denied) : FolderBatch.IHost
{
    public int Starts { get; private set; }

    public List<FolderAction> Elevated { get; } = new();

    public void Execute(FolderAction action)
    {
        if (denied.Contains(action.Path))
            throw new UnauthorizedAccessException("denied");
    }

    public bool StartElevated(out string error, out bool declined)
    {
        Starts++;
        error = "";
        declined = false;
        return true;
    }

    public bool ExecuteElevated(FolderAction action, out string error)
    {
        Elevated.Add(action);
        error = "";
        return true;
    }
}

/// <summary>Quick access as Windows behaves: a new pin goes to the end and stores the attributes the folder has now.</summary>
sealed class FakeQuickAccess : IQuickAccess
{
    public FakeQuickAccess(params QuickAccessPin[] pins)
    {
        Pins = pins.ToList();
        foreach (var pin in pins)
            Live[pin.Path] = pin.StoredAttributes;
    }

    public List<QuickAccessPin> Pins { get; }

    public Dictionary<string, FileAttributes> Live { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Unpinned { get; } = new();

    public string? RefusePin { get; set; }

    public IEnumerable<string> Order => Pins.Select(p => p.Path);

    public FolderSnapshot? Snapshot(string path) =>
        Live.TryGetValue(path, out var a) ? new FolderSnapshot(a, a.HasFlag(FileAttributes.ReadOnly)) : null;

    public IReadOnlyList<QuickAccessPin> GetPinned() => Pins.ToList();

    public void Pin(string path)
    {
        if (string.Equals(path, RefusePin, StringComparison.OrdinalIgnoreCase))
            return;
        Pins.Add(new QuickAccessPin(path, Live.GetValueOrDefault(path, FileAttributes.Directory), !path.StartsWith(@"\\", StringComparison.Ordinal)));
    }

    public void Unpin(string path)
    {
        Unpinned.Add(path);
        Pins.RemoveAll(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
    }
}

sealed class FakeJournal : IPinJournal
{
    public IReadOnlyList<string>? Saved { get; set; }

    public IReadOnlyList<string>? Load() => Saved;

    public void Save(IReadOnlyList<string> order) => Saved = order.ToList();

    public void Clear() => Saved = null;
}

/// <summary>A made-up folder tree; <see cref="Ids"/> holds the FolderId of each desktop.ini.</summary>
sealed class FakeTree(params string[] directories)
{
    readonly HashSet<string> _dirs = new(directories, StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Ids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Exists(string path) => _dirs.Contains(path);

    public IEnumerable<string> List(string path) =>
        _dirs.Where(d => string.Equals(Path.GetDirectoryName(d), path.TrimEnd('\\') == "C:" ? @"C:\" : path, StringComparison.OrdinalIgnoreCase));

    public string? ReadId(string path) => Ids.GetValueOrDefault(path);
}
