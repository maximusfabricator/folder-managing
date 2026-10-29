// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FolderManaging.Core.Updates;

/// <summary>
/// <c>update.json</c>, published as an asset of every release next to the setup and <c>update.json.sig</c>.
/// Written by scripts\release\Publish-Release.ps1; only trusted after <see cref="UpdateSignature"/> accepted its bytes.
/// </summary>
public sealed record UpdateManifest(
    UpdateVersion Version,
    string Tag,
    string SetupFile,
    long SetupSize,
    string SetupSha256,
    string ReleaseUrl,
    DateTimeOffset? Published,
    string Notes)
{
    public const int Schema = 1;
    public const string ProductId = "FolderManaging";

    /// <summary>The setup is about 130 MB; anything far bigger is not ours.</summary>
    public const long MaxSetupBytes = 512L * 1024 * 1024;

    const int MaxNotesChars = 20_000;

    static readonly Regex SetupFileName = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.exe$", RegexOptions.CultureInvariant);
    static readonly Regex Sha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    /// <exception cref="UpdateException">Not a valid manifest.</exception>
    public static UpdateManifest Parse(ReadOnlySpan<byte> utf8)
    {
        try
        {
            using var json = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Bad("not an object");
            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != Schema)
                throw Bad("unknown schema");
            if (Text(root, "product") != ProductId)
                throw Bad("other product");

            if (!UpdateVersion.TryParse(Text(root, "version"), out var version))
                throw Bad("bad version");
            var tag = Text(root, "tag") ?? "";
            if (tag != "v" + version)
                throw Bad("tag does not match the version");

            var setupFile = Text(root, "setupFile") ?? "";
            if (!SetupFileName.IsMatch(setupFile))
                throw Bad("bad setup file name");
            if (!root.TryGetProperty("setupSize", out var sizeElement) || sizeElement.ValueKind != JsonValueKind.Number ||
                !sizeElement.TryGetInt64(out var size) || size <= 0 || size > MaxSetupBytes)
                throw Bad("bad setup size");
            var sha = Text(root, "setupSha256") ?? "";
            if (!Sha256Hex.IsMatch(sha))
                throw Bad("bad SHA-256");

            var releaseUrl = Text(root, "releaseUrl");
            if (!IsReleasePage(releaseUrl))
                releaseUrl = UpdateEndpoint.ReleasesPageUrl;

            DateTimeOffset? published = DateTimeOffset.TryParse(Text(root, "published"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var p) ? p : null;

            return new UpdateManifest(version, tag, setupFile, size, sha.ToLowerInvariant(), releaseUrl!, published,
                CleanNotes(Text(root, "notes")));
        }
        catch (JsonException ex)
        {
            throw new UpdateException(UpdateError.BadManifest, "update.json is not valid JSON: " + ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new UpdateException(UpdateError.BadManifest, "update.json: " + ex.Message, ex);
        }
        catch (FormatException ex)
        {
            throw new UpdateException(UpdateError.BadManifest, "update.json: " + ex.Message, ex);
        }
    }

    static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    static UpdateException Bad(string what) => new(UpdateError.BadManifest, "update.json: " + what);

    /// <summary>Only a page of this project on github.com is opened from the update window.</summary>
    static bool IsReleasePage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort &&
        uri.AbsolutePath.StartsWith("/maximusfabricator/folder-managing/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Plain text, normal line breaks, no control characters.</summary>
    static string CleanNotes(string? notes)
    {
        if (string.IsNullOrEmpty(notes))
            return "";
        var sb = new StringBuilder(Math.Min(notes.Length, MaxNotesChars));
        foreach (var c in notes.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            if (sb.Length >= MaxNotesChars)
                break;
            if (c == '\n' || c == '\t' || !char.IsControl(c))
                sb.Append(c);
        }

        return sb.ToString().Trim();
    }
}

public enum UpdateError
{
    /// <summary>No connection, a timeout, or a server error.</summary>
    Network,

    /// <summary>The project has no release with update information (yet).</summary>
    NotFound,

    /// <summary>A redirect to a host or scheme that is not allowed.</summary>
    BlockedHost,

    /// <summary>update.json is not signed by a key this app trusts.</summary>
    BadSignature,

    BadManifest,

    /// <summary>The downloaded setup has the wrong size or SHA-256.</summary>
    BadDownload,

    /// <summary>A file bigger than the limit.</summary>
    TooLarge,

    /// <summary>The update folder could not be written.</summary>
    Disk,

    Cancelled,
}

public sealed class UpdateException : Exception
{
    public UpdateException(UpdateError error, string message, Exception? inner = null)
        : base(message, inner) => Error = error;

    public UpdateError Error { get; }
}
