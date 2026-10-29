// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace FolderManaging.Core.Updates;

/// <summary>What a check found: the signed manifest of the newest release.</summary>
public sealed record UpdateCheckResult(UpdateManifest Manifest, UpdateVersion Current)
{
    public bool IsNewer => Manifest.Version > Current;
}

/// <summary>
/// A setup whose size and SHA-256 matched the signed manifest. The file stays open for reading and nobody can
/// change or delete it until this is disposed, so the checked bytes are the bytes that run.
/// </summary>
public sealed class VerifiedSetup : IDisposable
{
    internal VerifiedSetup(string path, FileStream handle)
    {
        Path = path;
        Handle = handle;
    }

    public string Path { get; }

    public FileStream Handle { get; }

    public void Dispose() => Handle.Dispose();
}

/// <summary>
/// Downloads and checks updates. HTTPS only; every redirect must stay on <see cref="UpdateEndpoint.AllowedHosts"/>;
/// size limits and timeouts on every request; the system proxy is used. Sends nothing but the request itself
/// (GitHub sees the IP address and the User-Agent <c>FolderManaging/&lt;version&gt;</c>).
/// </summary>
public sealed class UpdateClient : IDisposable
{
    public const int MaxManifestBytes = 64 * 1024;
    const int MaxRedirects = 6;
    static readonly TimeSpan SmallFileTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(60);

    readonly HttpClient _http;
    readonly UpdateEndpoint _endpoint;
    readonly IReadOnlyList<string> _keys;
    readonly UpdateVersion _current;

    public UpdateClient(UpdateEndpoint? endpoint = null, IReadOnlyList<string>? trustedKeys = null, UpdateVersion? current = null)
    {
        _endpoint = endpoint ?? UpdateEndpoint.Default;
        _keys = trustedKeys ?? UpdateSignature.TrustedKeys;
        _current = current ?? UpdateVersion.Current;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FolderManaging", _current.ToString()));
    }

    public UpdateEndpoint Endpoint => _endpoint;

    public UpdateVersion Current => _current;

    /// <summary>Fetches <c>update.json</c> and its signature and checks the signature before reading anything else.</summary>
    /// <exception cref="UpdateException"></exception>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var manifestBytes = await GetSmallAsync(_endpoint.ManifestUri, MaxManifestBytes, cancellationToken).ConfigureAwait(false);
        var signature = await GetSmallAsync(_endpoint.SignatureUri, UpdateSignature.MaxSignatureFileBytes, cancellationToken).ConfigureAwait(false);
        if (!UpdateSignature.Verify(manifestBytes, signature, _keys))
            throw new UpdateException(UpdateError.BadSignature, "update.json is not signed with a trusted key.");
        return new UpdateCheckResult(UpdateManifest.Parse(manifestBytes), _current);
    }

    /// <summary>
    /// Downloads the setup of <paramref name="manifest"/> from the same release into <paramref name="directory"/>
    /// (reusing an earlier complete download) and returns it open and checked.
    /// </summary>
    /// <exception cref="UpdateException"></exception>
    public async Task<VerifiedSetup> DownloadSetupAsync(
        UpdateManifest manifest,
        string directory,
        IProgress<(long Done, long Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(directory, manifest.SetupFile);
        var partial = target + ".partial";
        try
        {
            Directory.CreateDirectory(directory);
            CleanDirectory(directory, keep: manifest.SetupFile);
            if (File.Exists(target))
            {
                var reused = TryOpenVerified(target, manifest);
                if (reused is not null)
                {
                    progress?.Report((manifest.SetupSize, manifest.SetupSize));
                    return reused;
                }

                File.Delete(target);
            }

            using (var response = await SendAsync(_endpoint.AssetUri(manifest.Tag, manifest.SetupFile), cancellationToken).ConfigureAwait(false))
            {
                if (response.Content.Headers.ContentLength is { } length && length != manifest.SetupSize)
                    throw new UpdateException(UpdateError.BadDownload, $"The server announces {length} bytes, update.json says {manifest.SetupSize}.");

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                var buffer = new byte[81920];
                long done = 0;
                progress?.Report((0, manifest.SetupSize));
                while (true)
                {
                    int read;
                    using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        idle.CancelAfter(DownloadIdleTimeout);
                        read = await ReadAsync(source, buffer, idle.Token, cancellationToken).ConfigureAwait(false);
                    }

                    if (read == 0)
                        break;
                    done += read;
                    if (done > manifest.SetupSize)
                        throw new UpdateException(UpdateError.BadDownload, "The download is bigger than update.json says.");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report((done, manifest.SetupSize));
                }

                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(partial, target, overwrite: true);
            return TryOpenVerified(target, manifest)
                   ?? throw new UpdateException(UpdateError.BadDownload, "The downloaded setup does not match update.json (size or SHA-256).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(partial);
            throw new UpdateException(UpdateError.Disk, ex.Message, ex);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(partial);
            throw new UpdateException(UpdateError.Cancelled, "Cancelled.", ex);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>Opens <paramref name="path"/> so it cannot change, then checks it; <c>null</c> (and deleted) when it does not match.</summary>
    public static VerifiedSetup? TryOpenVerified(string path, UpdateManifest manifest)
    {
        var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        try
        {
            if (handle.Length == manifest.SetupSize)
            {
                var hash = Convert.ToHexStringLower(SHA256.HashData(handle));
                if (CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.ASCII.GetBytes(hash),
                        System.Text.Encoding.ASCII.GetBytes(manifest.SetupSha256)))
                {
                    handle.Position = 0;
                    return new VerifiedSetup(path, handle);
                }
            }
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        handle.Dispose();
        TryDelete(path);
        return null;
    }

    /// <summary>Deletes earlier downloads and status files; files still in use stay until next time.</summary>
    public static void CleanDirectory(string directory, string? keep = null)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (keep is not null && string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase))
                continue;
            TryDelete(file);
        }
    }

    async Task<byte[]> GetSmallAsync(Uri uri, int maxBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SmallFileTimeout);
        using var response = await SendAsync(uri, timeout.Token, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new UpdateException(UpdateError.TooLarge, $"{uri.AbsolutePath} is larger than {maxBytes} bytes.");

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await ReadAsync(stream, buffer, timeout.Token, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (memory.Length + read > maxBytes)
                throw new UpdateException(UpdateError.TooLarge, $"{uri.AbsolutePath} is larger than {maxBytes} bytes.");
            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken) => SendAsync(uri, cancellationToken, cancellationToken);

    /// <summary>GET with redirects followed by hand, each one checked against the allowed hosts.</summary>
    async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token, CancellationToken userToken)
    {
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!_endpoint.IsAllowed(uri))
                throw new UpdateException(UpdateError.BlockedHost, $"Refused to connect to {uri.GetLeftPart(UriPartial.Authority)}.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (userToken.IsCancellationRequested)
            {
                throw new UpdateException(UpdateError.Cancelled, "Cancelled.", ex);
            }
            catch (OperationCanceledException ex)
            {
                throw new UpdateException(UpdateError.Network, $"{uri.Host} did not answer in time.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new UpdateException(UpdateError.Network, ex.Message, ex);
            }

            var code = (int)response.StatusCode;
            if (code is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                    throw new UpdateException(UpdateError.Network, "A redirect without a target.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }

            if (response.IsSuccessStatusCode)
                return response;

            response.Dispose();
            throw code == 404
                ? new UpdateException(UpdateError.NotFound, $"{uri.AbsolutePath}: not found (404).")
                : new UpdateException(UpdateError.Network, $"{uri.Host} answered {code}.");
        }

        throw new UpdateException(UpdateError.Network, "Too many redirects.");
    }

    static async Task<int> ReadAsync(Stream stream, byte[] buffer, CancellationToken token, CancellationToken userToken)
    {
        try
        {
            return await stream.ReadAsync(buffer, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (userToken.IsCancellationRequested)
        {
            throw new UpdateException(UpdateError.Cancelled, "Cancelled.", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new UpdateException(UpdateError.Network, "The download stopped (timeout).", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new UpdateException(UpdateError.Network, ex.Message, ex);
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // still in use (a running setup); removed on a later check
        }
    }

    public void Dispose() => _http.Dispose();
}
