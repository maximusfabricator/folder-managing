// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;
using FolderManaging.Core.Updates;

namespace FolderManaging.SelfTests;

/// <summary>
/// The updater: versions, signatures, manifests, allowed hosts, the schedule, state.json, and a full check and
/// download against a local fake server with throwaway keys. Nothing goes to the internet.
/// </summary>
static partial class Program
{
    static readonly JsonSerializerOptions StateJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static async Task UpdateChecks()
    {
        Check("update version: forms, order and what is refused", () =>
        {
            Expect(UpdateVersion.Parse("2.2.0") == new UpdateVersion(2, 2, 0), "2.2.0");
            Expect(UpdateVersion.Parse("v2.2.0") == UpdateVersion.Parse("2.2.0.0"), "v prefix or fourth part");
            Expect(UpdateVersion.Parse("2.2") == new UpdateVersion(2, 2, 0), "two parts");
            Expect(UpdateVersion.Parse("2.2.0+abc1234") == new UpdateVersion(2, 2, 0), "build metadata");
            Expect(UpdateVersion.Parse("2.10.0") > UpdateVersion.Parse("2.9.9"), "2.10.0 > 2.9.9");
            Expect(UpdateVersion.Parse("3.0.0") > UpdateVersion.Parse("2.99.99"), "major");
            Expect(UpdateVersion.Parse("2.1.1") < UpdateVersion.Parse("2.2.0"), "2.1.1 < 2.2.0");
            foreach (var bad in new[] { "", "abc", "2", "2.2.0.1", "2.-1.0", "2.2.0.0.0", "2..0", null })
                Expect(!UpdateVersion.TryParse(bad, out _), $"accepted '{bad}'");
            Expect(UpdateVersion.Current == UpdateVersion.Parse(FolderManaging.Core.AppInfo.Version), "current version");
        });

        using var key = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        using var ecKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keySpki = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var otherSpki = Convert.ToBase64String(otherKey.ExportSubjectPublicKeyInfo());
        var ecSpki = Convert.ToBase64String(ecKey.ExportSubjectPublicKeyInfo());

        Check("update signature: good, tampered, wrong key and key lists", () =>
        {
            var manifest = ManifestBytes("9.9.9", "FolderManaging-Setup-9.9.9.exe", 10, new string('a', 64));
            var sig = Sign(key, manifest);
            Expect(UpdateSignature.Verify(manifest, sig, [keySpki]), "good signature refused");

            var tampered = (byte[])manifest.Clone();
            tampered[^3] ^= 1;
            Expect(!UpdateSignature.Verify(tampered, sig, [keySpki]), "tampered manifest accepted");
            Expect(!UpdateSignature.Verify(manifest, sig, [otherSpki]), "wrong key accepted");
            Expect(UpdateSignature.Verify(manifest, sig, [otherSpki, keySpki]), "second key of the list not used");
            Expect(UpdateSignature.Verify(manifest, sig, ["not base64!", keySpki]), "a broken key stops the list");
            Expect(!UpdateSignature.Verify(manifest, sig, []), "no keys accepted");

            var rotated = Encoding.ASCII.GetBytes("garbage\n" + Encoding.ASCII.GetString(Sign(otherKey, manifest)) + "\n" + Encoding.ASCII.GetString(sig));
            Expect(UpdateSignature.Verify(manifest, rotated, [keySpki]), "a signature on a later line not found");
            Expect(!UpdateSignature.Verify(manifest, Encoding.ASCII.GetBytes("garbage\n"), [keySpki]), "garbage accepted");
            Expect(!UpdateSignature.Verify(manifest, [], [keySpki]), "empty file accepted");
            Expect(!UpdateSignature.Verify(manifest, new byte[UpdateSignature.MaxSignatureFileBytes + 1], [keySpki]), "huge file accepted");

            var ecSig = Encoding.ASCII.GetBytes(Convert.ToBase64String(ecKey.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)));
            Expect(UpdateSignature.Verify(manifest, ecSig, [keySpki, ecSpki]), "ECDSA key not accepted");

            Expect(!UpdateSignature.Verify(manifest, sig), "the shipped key accepts a test signature");
            Expect(UpdateSignature.TrustedKeys.Count >= 1 &&
                   UpdateSignature.TrustedKeys.All(k => PublicKey.CreateFromSubjectPublicKeyInfo(Convert.FromBase64String(k), out _).GetRSAPublicKey()?.KeySize >= 2048),
                "shipped keys are not RSA 2048+");
        });

        Check("update manifest: valid file and what is refused", () =>
        {
            var sha = new string('A', 64);
            var m = UpdateManifest.Parse(ManifestBytes("2.2.0", "FolderManaging-Setup-2.2.0.exe", 1234, sha,
                notes: "line1\r\nline2\u0007\u001b[31m"));
            Expect(m.Version == new UpdateVersion(2, 2, 0) && m.Tag == "v2.2.0" && m.SetupSize == 1234, "fields");
            Expect(m.SetupSha256 == new string('a', 64), "SHA-256 not lowercased");
            Expect(m.Notes == "line1\nline2[31m", $"notes not cleaned: {m.Notes}");
            Expect(m.ReleaseUrl == "https://github.com/maximusfabricator/folder-managing/releases/tag/v2.2.0", m.ReleaseUrl);

            var foreign = UpdateManifest.Parse(ManifestBytes("2.2.0", "a.exe", 1, sha, releaseUrl: "https://evil.example/x"));
            Expect(foreign.ReleaseUrl == UpdateEndpoint.ReleasesPageUrl, "foreign release link kept");

            ExpectBad(ManifestBytes("2.2.0", "a.exe", 1, sha, tag: "v2.2.1"), "tag mismatch");
            ExpectBad(ManifestBytes("2.2.0", "..\\a.exe", 1, sha), "path in file name");
            ExpectBad(ManifestBytes("2.2.0", "a.msi", 1, sha), "not an exe");
            ExpectBad(ManifestBytes("2.2.0", "a.exe", 0, sha), "size 0");
            ExpectBad(ManifestBytes("2.2.0", "a.exe", UpdateManifest.MaxSetupBytes + 1, sha), "too big");
            ExpectBad(ManifestBytes("2.2.0", "a.exe", 1, "xyz"), "bad SHA-256");
            ExpectBad(ManifestBytes("2.2.0", "a.exe", 1, sha, product: "Other"), "other product");
            ExpectBad(ManifestBytes("2.2.0", "a.exe", 1, sha, schema: 2), "schema 2");
            ExpectBad("{not json"u8.ToArray(), "not JSON");
            ExpectBad("[]"u8.ToArray(), "array");
        });

        Check("update hosts: HTTPS to GitHub hosts only", () =>
        {
            var gh = UpdateEndpoint.GitHub;
            foreach (var ok in new[]
                     {
                         "https://github.com/maximusfabricator/folder-managing/releases/latest/download/update.json",
                         "https://objects.githubusercontent.com/x", "https://release-assets.githubusercontent.com/x?sig=1",
                         "https://GitHub.com/x",
                     })
                Expect(gh.IsAllowed(new Uri(ok)), $"refused {ok}");
            foreach (var bad in new[]
                     {
                         "http://github.com/x", "https://github.com:8443/x", "https://user:pw@github.com/x", "https://evil.com/x",
                         "https://github.com.evil.com/x", "https://githubusercontent.com/x", "https://raw.githubusercontent.com/x",
                         "http://127.0.0.1/x", "ftp://github.com/x", "file:///C:/x.exe",
                     })
                Expect(!gh.IsAllowed(new Uri(bad)), $"allowed {bad}");
            Expect(gh.ManifestUri.ToString() == "https://github.com/maximusfabricator/folder-managing/releases/latest/download/update.json", gh.ManifestUri.ToString());
            Expect(gh.SignatureUri.ToString().EndsWith("/releases/latest/download/update.json.sig", StringComparison.Ordinal), gh.SignatureUri.ToString());
            Expect(gh.AssetUri("v2.2.0", "FolderManaging-Setup-2.2.0.exe").ToString() ==
                   "https://github.com/maximusfabricator/folder-managing/releases/download/v2.2.0/FolderManaging-Setup-2.2.0.exe", "asset link");
#if FM_DEV
            var test = UpdateEndpoint.ForTestServer("http://localhost:8123/releases")!;
            Expect(test.IsAllowed(new Uri("http://localhost:8123/releases/x")), "test server refused");
            Expect(!test.IsAllowed(new Uri("http://evil.com/x")), "plain HTTP to another host allowed");
#endif
        });

        Check("update schedule: start delay, 24 hours, clock moved back", () =>
        {
            var start = new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);
            var now = start.AddSeconds(5);
            Expect(UpdateSchedule.NextCheckUtc(null, start, now) == start + UpdateSchedule.StartupDelay, "first check");
            Expect(UpdateSchedule.StartupDelay >= TimeSpan.FromMinutes(1) && UpdateSchedule.StartupDelay <= TimeSpan.FromMinutes(2), "start delay");
            Expect(UpdateSchedule.Interval == TimeSpan.FromHours(24), "interval");
            Expect(UpdateSchedule.NextCheckUtc(now.AddHours(-1), start, now) == now.AddHours(23), "checked an hour ago");
            Expect(UpdateSchedule.NextCheckUtc(now.AddHours(-30), start, now) == start + UpdateSchedule.StartupDelay, "overdue");
            Expect(UpdateSchedule.NextCheckUtc(now.AddDays(3), start, now) == start + UpdateSchedule.StartupDelay, "last check in the future");
        });

        Check("update offer: later, skip, newer than skipped, manual, never downgrade", () =>
        {
            var current = UpdateVersion.Parse("2.2.0");
            var next = UpdateVersion.Parse("2.3.0");
            Expect(UpdateSchedule.ShouldOffer(next, current, null, manual: false), "newer not offered");
            Expect(!UpdateSchedule.ShouldOffer(next, current, "2.3.0", manual: false), "skipped version offered");
            Expect(UpdateSchedule.ShouldOffer(UpdateVersion.Parse("2.3.1"), current, "2.3.0", manual: false), "version after the skipped one not offered");
            Expect(UpdateSchedule.ShouldOffer(next, current, "2.3.0", manual: true), "manual check hides the skipped version");
            Expect(!UpdateSchedule.ShouldOffer(current, current, null, manual: true), "same version offered");
            Expect(!UpdateSchedule.ShouldOffer(UpdateVersion.Parse("2.1.1"), current, null, manual: true), "older version offered");
            Expect(UpdateSchedule.ShouldOffer(next, current, "garbage", manual: false), "broken skipped value blocks");
        });

        Check("update state: a 2.1.1 state.json loads with checks on and keeps everything", () =>
        {
            var old = JsonNode.Parse(State211)!.AsObject();
            var doc = JsonSerializer.Deserialize<AppStateDocument>(State211, StateJson)!;
            Expect(doc.CheckForUpdates && doc.UpdateLastCheckUtc is null && doc.UpdateSkippedVersion is null, "update defaults");
            Expect(doc.Folders.Count == 7 && doc.Folders.Count(f => f.ColorToken is null && f.Tags.Count == 0) == 5, "folder entries lost");

            var saved = JsonNode.Parse(JsonSerializer.Serialize(doc, StateJson))!.AsObject();
            foreach (var (name, value) in old)
                Expect(JsonNode.DeepEquals(value, saved[name]), $"{name} changed on save");
            Expect(saved["checkForUpdates"]?.GetValue<bool>() == true, "checkForUpdates not written");
        });

        Check("update state: settings survive a save and load", () =>
        {
            var doc = new AppStateDocument { CheckForUpdates = false, UpdateLastCheckUtc = new DateTime(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc), UpdateSkippedVersion = "2.3.0" };
            var back = JsonSerializer.Deserialize<AppStateDocument>(JsonSerializer.Serialize(doc, StateJson), StateJson)!;
            Expect(!back.CheckForUpdates && back.UpdateLastCheckUtc == doc.UpdateLastCheckUtc && back.UpdateSkippedVersion == "2.3.0", "round trip");
            back.ResetSettingsToDefaults();
            Expect(back.CheckForUpdates, "reset leaves checks off");
        });

        Check("update state: export carries the switch, not the check time or the skipped version", () =>
        {
            var doc = new AppStateDocument { CheckForUpdates = false, UpdateLastCheckUtc = DateTime.UtcNow, UpdateSkippedVersion = "2.3.0" };
            var json = SettingsTransfer.Export(doc, "2.2.0");
            var node = JsonNode.Parse(json)!.AsObject();
            Expect(node["checkForUpdates"]?.GetValue<bool>() == false, "switch not exported");
            Expect(!json.Contains("updateLastCheck", StringComparison.OrdinalIgnoreCase) && !json.Contains("2.3.0", StringComparison.Ordinal), "private update state exported");

            var target = new AppStateDocument();
            SettingsTransfer.Apply(target, SettingsTransfer.Parse(json), _ => false);
            Expect(!target.CheckForUpdates, "import ignored the switch");
            var older = new AppStateDocument { CheckForUpdates = false };
            node.Remove("checkForUpdates");
            SettingsTransfer.Apply(older, SettingsTransfer.Parse(node.ToJsonString()), _ => false);
            Expect(!older.CheckForUpdates, "an older export changed the switch");
        });

        var temp = Path.Combine(Path.GetTempPath(), "fm-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Check("update handoff: the app reads the last stage of the setup", () =>
            {
                var status = UpdateHandoff.NewStatusPath(temp);
                Expect(UpdateHandoff.ReadLast(status) is null, "stage before the file exists");
                UpdateHandoff.Write(status, UpdateStage.Started);
                UpdateHandoff.Write(status, UpdateStage.Failed, "line\nbreak");
                var last = UpdateHandoff.ReadLast(status);
                Expect(last?.Stage == UpdateStage.Failed && last?.Message == "line break", $"last: {last}");
                File.Delete(status);
            });

#if FM_DEV
            await FakeServerChecks(temp, key, otherKey, keySpki);
#endif
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

#if FM_DEV
    static async Task FakeServerChecks(string temp, RSA key, RSA otherKey, string keySpki)
    {
        var setup = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 17);
        var sha = Convert.ToHexStringLower(SHA256.HashData(setup));
        const string file = "FolderManaging-Setup-9.9.9.exe";
        var manifest = ManifestBytes("9.9.9", file, setup.Length, sha);
        var current = UpdateVersion.Parse("2.2.0");

        await using var server = FakeReleaseServer.Start();
        server.Routes["/releases/latest/download/update.json"] = FakeReleaseServer.Redirect("/releases/download/v9.9.9/update.json");
        server.Routes["/releases/download/v9.9.9/update.json"] = FakeReleaseServer.Bytes(manifest);
        server.Routes["/releases/latest/download/update.json.sig"] = FakeReleaseServer.Bytes(Sign(key, manifest));
        server.Routes["/releases/download/v9.9.9/" + file] = FakeReleaseServer.Bytes(setup);
        var endpoint = UpdateEndpoint.ForTestServer(server.Root)!;
        UpdateClient Client() => new(endpoint, [keySpki], current);
        var downloads = Path.Combine(temp, "Updates");

        await Check("update end to end: check follows the redirect and verifies the signature", async () =>
        {
            using var client = Client();
            var result = await client.CheckAsync();
            Expect(result.IsNewer && result.Manifest.Version == UpdateVersion.Parse("9.9.9"), "no newer version");
            var (exit, json) = await UpdateCheckReport.RunAsync(json: true, client);
            Expect(exit == UpdateCheckReport.UpdateAvailable, $"report exit {exit}");
            var node = JsonNode.Parse(json)!;
            Expect(node["signature"]!.GetValue<string>() == "valid" && node["latest"]!.GetValue<string>() == "9.9.9", json);
        });

        await Check("update end to end: download, size and SHA-256, and the file stays locked", async () =>
        {
            using var client = Client();
            var m = (await client.CheckAsync()).Manifest;
            Directory.CreateDirectory(downloads);
            File.WriteAllText(Path.Combine(downloads, "FolderManaging-Setup-9.9.8.exe"), "old");
            long last = 0;
            var progress = new SyncProgress(p => last = p.Done);
            using (var verified = await client.DownloadSetupAsync(m, downloads, progress))
            {
                Expect(verified.Path == Path.Combine(downloads, file), verified.Path);
                Expect(last == setup.Length, $"progress ended at {last}");
                Expect(!File.Exists(Path.Combine(downloads, "FolderManaging-Setup-9.9.8.exe")), "old download kept");
                var locked = false;
                try
                {
                    using var write = new FileStream(verified.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                }
                catch (IOException)
                {
                    locked = true;
                }

                Expect(locked, "the checked setup could be changed before it ran");
                using var read = new FileStream(verified.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Expect(read.Length == setup.Length, "setup cannot be read by the installer");
            }

            var hits = server.Hits("/releases/download/v9.9.9/" + file);
            using (await client.DownloadSetupAsync(m, downloads))
            {
            }

            Expect(server.Hits("/releases/download/v9.9.9/" + file) == hits, "a complete earlier download was fetched again");
        });

        await Check("update end to end: a setup with the wrong SHA-256 is refused and deleted", async () =>
        {
            var badManifest = UpdateManifest.Parse(ManifestBytes("9.9.9", file, setup.Length, new string('0', 64)));
            UpdateClient.CleanDirectory(downloads);
            using var client = Client();
            var error = await CatchUpdate(() => client.DownloadSetupAsync(badManifest, downloads));
            Expect(error == UpdateError.BadDownload, $"error {error}");
            Expect(!Directory.EnumerateFiles(downloads).Any(), "bad download left on disk");
        });

        await Check("update end to end: a setup of the wrong size is refused", async () =>
        {
            var shorter = UpdateManifest.Parse(ManifestBytes("9.9.9", file, setup.Length - 1, sha));
            using var client = Client();
            var error = await CatchUpdate(() => client.DownloadSetupAsync(shorter, downloads));
            Expect(error == UpdateError.BadDownload, $"error {error}");
            Expect(!Directory.EnumerateFiles(downloads).Any(), "bad download left on disk");

            server.Routes["/releases/download/v9.9.9/chunked.exe"] = FakeReleaseServer.Bytes(setup, chunked: true);
            var chunked = UpdateManifest.Parse(ManifestBytes("9.9.9", "chunked.exe", setup.Length - 1, sha));
            error = await CatchUpdate(() => client.DownloadSetupAsync(chunked, downloads));
            Expect(error == UpdateError.BadDownload, $"chunked: error {error}");
        });

        await Check("update end to end: tampered manifest, wrong key, blocked redirect, 404", async () =>
        {
            var tampered = (byte[])manifest.Clone();
            tampered[^5] ^= 1;
            server.Routes["/releases/download/v9.9.9/update.json"] = FakeReleaseServer.Bytes(tampered);
            using (var client = Client())
                Expect(await CatchUpdate(() => client.CheckAsync()) == UpdateError.BadSignature, "tampered manifest");
            server.Routes["/releases/download/v9.9.9/update.json"] = FakeReleaseServer.Bytes(manifest);

            using (var client = new UpdateClient(endpoint, [Convert.ToBase64String(otherKey.ExportSubjectPublicKeyInfo())], current))
                Expect(await CatchUpdate(() => client.CheckAsync()) == UpdateError.BadSignature, "wrong key");
            var (exit, _) = await UpdateCheckReport.RunAsync(json: false, new UpdateClient(endpoint, ["AAAA"], current));
            Expect(exit == UpdateCheckReport.NotTrusted, $"report exit {exit}");

            server.Routes["/releases/latest/download/update.json"] = FakeReleaseServer.Redirect("https://evil.example/update.json");
            using (var client = Client())
                Expect(await CatchUpdate(() => client.CheckAsync()) == UpdateError.BlockedHost, "redirect to a foreign host");
            server.Routes["/releases/latest/download/update.json"] = FakeReleaseServer.Redirect("http://127.0.0.2:1/update.json");
            using (var client = Client())
                Expect(await CatchUpdate(() => client.CheckAsync()) == UpdateError.BlockedHost, "redirect to another loopback host");
            server.Routes["/releases/latest/download/update.json"] = FakeReleaseServer.Redirect("/releases/latest/download/update.json");
            using (var client = Client())
                Expect(await CatchUpdate(() => client.CheckAsync()) == UpdateError.Network, "redirect loop");

            server.Routes.Remove("/releases/latest/download/update.json");
            using (var client = Client())
                Expect(await CatchUpdate(() => client.CheckAsync()) == UpdateError.NotFound, "404");
            server.Routes["/releases/latest/download/update.json"] = FakeReleaseServer.Bytes(new byte[UpdateClient.MaxManifestBytes + 1]);
            using (var client = Client())
                Expect(await CatchUpdate(() => client.CheckAsync()) == UpdateError.TooLarge, "huge manifest");
            server.Routes["/releases/latest/download/update.json"] = FakeReleaseServer.Bytes(manifest);
        });

        await Check("update end to end: an older or equal release is not offered", async () =>
        {
            using var client = new UpdateClient(endpoint, [keySpki], UpdateVersion.Parse("9.9.9"));
            Expect(!(await client.CheckAsync()).IsNewer, "same version is newer");
            using var newer = new UpdateClient(endpoint, [keySpki], UpdateVersion.Parse("10.0.0"));
            Expect(!(await newer.CheckAsync()).IsNewer, "downgrade offered");
            var (exit, _) = await UpdateCheckReport.RunAsync(json: false, new UpdateClient(endpoint, [keySpki], UpdateVersion.Parse("10.0.0")));
            Expect(exit == UpdateCheckReport.UpToDate, $"report exit {exit}");
        });

        await Check("update end to end: cancelling a download leaves nothing behind", async () =>
        {
            UpdateClient.CleanDirectory(downloads);
            server.Routes["/releases/download/v9.9.9/slow.exe"] = FakeReleaseServer.Bytes(setup, delayPerChunkMs: 50);
            var slow = UpdateManifest.Parse(ManifestBytes("9.9.9", "slow.exe", setup.Length, sha));
            using var client = Client();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var error = await CatchUpdate(() => client.DownloadSetupAsync(slow, downloads, null, cts.Token));
            Expect(error == UpdateError.Cancelled, $"error {error}");
            await Task.Delay(100);
            Expect(!Directory.EnumerateFiles(downloads).Any(), string.Join(", ", Directory.EnumerateFiles(downloads)));
        });
    }
#endif

    static async Task<UpdateError?> CatchUpdate(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (UpdateException ex)
        {
            return ex.Error;
        }
    }

    static void ExpectBad(byte[] manifest, string what)
    {
        try
        {
            UpdateManifest.Parse(manifest);
        }
        catch (UpdateException ex) when (ex.Error == UpdateError.BadManifest)
        {
            return;
        }

        throw new InvalidOperationException($"accepted: {what}");
    }

    static byte[] Sign(RSA key, byte[] data) =>
        Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) + "\n");

    static byte[] ManifestBytes(string version, string file, long size, string sha, string? tag = null, string? releaseUrl = null,
        string notes = "## 9.9.9\n\n- Better.", string product = "FolderManaging", int schema = 1) =>
        JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["schema"] = schema,
            ["product"] = product,
            ["version"] = version,
            ["tag"] = tag ?? "v" + version,
            ["setupFile"] = file,
            ["setupSize"] = size,
            ["setupSha256"] = sha,
            ["releaseUrl"] = releaseUrl ?? $"https://github.com/maximusfabricator/folder-managing/releases/tag/v{version}",
            ["published"] = "2026-10-29T12:00:00Z",
            ["notes"] = notes,
        }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>A state.json as 2.1.1 wrote it (no update fields), with five folders that only hold a pin or an id.</summary>
    const string State211 = """
        {
          "version": 2,
          "folders": [
            { "path": "C:\\FmSelfTest\\A", "tags": [ "work" ], "isPinned": false, "colorToken": "red", "id": "a1" },
            { "path": "C:\\FmSelfTest\\B", "tags": [], "isPinned": true, "colorToken": "rgb-112233", "id": "b2" },
            { "path": "C:\\FmSelfTest\\C", "tags": [], "isPinned": true, "colorToken": null, "id": "c3" },
            { "path": "C:\\FmSelfTest\\D", "tags": [], "isPinned": true, "colorToken": null, "id": "d4" },
            { "path": "C:\\FmSelfTest\\E", "tags": [], "isPinned": false, "colorToken": null, "id": "e5" },
            { "path": "C:\\FmSelfTest\\F", "tags": [], "isPinned": true, "colorToken": null, "id": null },
            { "path": "C:\\FmSelfTest\\G", "tags": [], "isPinned": false, "colorToken": null, "id": "g7" }
          ],
          "recentColors": [ "red", "rgb-112233" ],
          "menuColors": [ { "id": "red", "label": "", "hexRgb": "E81123" } ],
          "menuTags": [ { "id": "work", "label": "Work", "hexRgb": "0078D4" } ],
          "enabledColorSchemes": [],
          "defaultTagsSeeded": true,
          "autoElevateProtectedFolders": true,
          "theme": 0,
          "language": "ru",
          "colorSortTipShown": true
        }
        """;

    sealed class SyncProgress(Action<(long Done, long Total)> report) : IProgress<(long Done, long Total)>
    {
        public void Report((long Done, long Total) value) => report(value);
    }
}

/// <summary>A release server on localhost for the end-to-end checks.</summary>
sealed class FakeReleaseServer : IAsyncDisposable
{
    readonly HttpListener _listener = new();
    readonly Dictionary<string, int> _hits = new();
    readonly Task _loop;

    FakeReleaseServer(int port)
    {
        Root = $"http://localhost:{port}/releases";
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    public string Root { get; }

    public Dictionary<string, Func<HttpListenerContext, Task>> Routes { get; } = new(StringComparer.Ordinal);

    public int Hits(string path)
    {
        lock (_hits)
            return _hits.GetValueOrDefault(path);
    }

    public static FakeReleaseServer Start()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new FakeReleaseServer(port);
    }

    public static Func<HttpListenerContext, Task> Bytes(byte[] body, bool chunked = false, int delayPerChunkMs = 0) => async context =>
    {
        context.Response.ContentType = "application/octet-stream";
        if (chunked)
            context.Response.SendChunked = true;
        else
            context.Response.ContentLength64 = body.Length;
        for (var offset = 0; offset < body.Length; offset += 65536)
        {
            await context.Response.OutputStream.WriteAsync(body.AsMemory(offset, Math.Min(65536, body.Length - offset)));
            if (delayPerChunkMs > 0)
                await Task.Delay(delayPerChunkMs);
        }
    };

    public static Func<HttpListenerContext, Task> Redirect(string location) => context =>
    {
        context.Response.StatusCode = 302;
        context.Response.RedirectLocation = location;
        return Task.CompletedTask;
    };

    async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                var path = context.Request.Url!.AbsolutePath;
                lock (_hits)
                    _hits[path] = _hits.GetValueOrDefault(path) + 1;
                try
                {
                    if (Routes.TryGetValue(path, out var handler))
                        await handler(context);
                    else
                        context.Response.StatusCode = 404;
                    context.Response.Close();
                }
                catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
                {
                    try
                    {
                        context.Response.Abort();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        _listener.Close();
        await _loop;
    }
}
