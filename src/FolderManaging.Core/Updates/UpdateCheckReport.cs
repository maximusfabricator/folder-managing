// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FolderManaging.Core.Updates;

/// <summary>
/// <c>foldermanaging-cli update --check [--json]</c>: what the app would find, without downloading or installing
/// anything and without changing <c>state.json</c>. Exit code 0 up to date, 10 newer version available,
/// 2 no connection or no release, 3 signature or manifest not valid.
/// </summary>
public static class UpdateCheckReport
{
    public const int UpToDate = 0;
    public const int UpdateAvailable = 10;
    public const int NetworkError = 2;
    public const int NotTrusted = 3;

    public static async Task<(int ExitCode, string Text)> RunAsync(bool json, UpdateClient? client = null, CancellationToken cancellationToken = default)
    {
        var own = client is null;
        client ??= new UpdateClient();
        try
        {
            var current = client.Current;
            UpdateCheckResult? result = null;
            UpdateException? failure = null;
            try
            {
                result = await client.CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (UpdateException ex)
            {
                failure = ex;
            }

            var signature = failure is null ? "valid"
                : failure.Error is UpdateError.BadSignature ? "invalid"
                : failure.Error is UpdateError.BadManifest ? "valid, manifest rejected"
                : "not checked";
            var exit = failure is null
                ? result!.IsNewer ? UpdateAvailable : UpToDate
                : failure.Error is UpdateError.BadSignature or UpdateError.BadManifest or UpdateError.BlockedHost ? NotTrusted : NetworkError;

            if (json)
            {
                var payload = new Dictionary<string, object?>
                {
                    ["current"] = current.ToString(),
                    ["latest"] = result?.Manifest.Version.ToString(),
                    ["signature"] = signature,
                    ["updateAvailable"] = result?.IsNewer ?? false,
                    ["setupFile"] = result?.Manifest.SetupFile,
                    ["setupSize"] = result?.Manifest.SetupSize,
                    ["setupSha256"] = result?.Manifest.SetupSha256,
                    ["releaseUrl"] = result?.Manifest.ReleaseUrl,
                    ["published"] = result?.Manifest.Published?.ToString("O", CultureInfo.InvariantCulture),
                    ["source"] = client.Endpoint.ManifestUri.ToString(),
                    ["error"] = failure is null ? null : $"{failure.Error}: {failure.Message}",
                };
                return (exit, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            }

            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture, $"Current version: {current}");
            text.AppendLine(CultureInfo.InvariantCulture, $"Source:          {client.Endpoint.ManifestUri}");
            if (result is not null)
            {
                var published = result.Manifest.Published is { } p ? $" (published {p:yyyy-MM-dd})" : "";
                text.AppendLine(CultureInfo.InvariantCulture, $"Latest version:  {result.Manifest.Version}{published}");
                text.AppendLine(CultureInfo.InvariantCulture, $"Signature:       {signature}");
                text.AppendLine(CultureInfo.InvariantCulture, $"Setup:           {result.Manifest.SetupFile}, {result.Manifest.SetupSize} bytes, SHA-256 {result.Manifest.SetupSha256}");
                text.AppendLine(result.IsNewer ? "Result:          a newer version is available" : "Result:          up to date");
            }
            else
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Signature:       {signature}");
                text.AppendLine(CultureInfo.InvariantCulture, $"Result:          check failed ({failure!.Error}): {failure.Message}");
            }

            return (exit, text.ToString().TrimEnd());
        }
        finally
        {
            if (own)
                client.Dispose();
        }
    }
}
