// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Net;

namespace FolderManaging.Core.Updates;

/// <summary>
/// Where updates come from: the GitHub releases of the project, fetched as plain release assets
/// (<c>releases/latest/download/…</c>) without the GitHub API and its rate limit.
/// </summary>
public sealed class UpdateEndpoint
{
    public const string ReleasesRoot = AppInfo.SourceUrl + "/releases";
    public const string ReleasesPageUrl = ReleasesRoot;
    public const string ManifestFileName = "update.json";
    public const string SignatureFileName = "update.json.sig";

    /// <summary>GitHub answers release downloads with redirects to these hosts.</summary>
    public static IReadOnlyList<string> GitHubHosts { get; } =
        ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    UpdateEndpoint(string root, IReadOnlyList<string> hosts, bool allowLoopbackHttp)
    {
        Root = root.TrimEnd('/');
        AllowedHosts = hosts;
        AllowLoopbackHttp = allowLoopbackHttp;
    }

    public string Root { get; }

    public IReadOnlyList<string> AllowedHosts { get; }

    /// <summary>Only for a local test server in developer builds.</summary>
    public bool AllowLoopbackHttp { get; }

    public Uri ManifestUri => new(Root + "/latest/download/" + ManifestFileName);

    public Uri SignatureUri => new(Root + "/latest/download/" + SignatureFileName);

    public Uri AssetUri(string tag, string fileName) =>
        new(Root + "/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(fileName));

    public static UpdateEndpoint GitHub { get; } = new(ReleasesRoot, GitHubHosts, allowLoopbackHttp: false);

    /// <summary>
    /// The real GitHub releases. Developer builds: <c>FM_UPDATE_BASE_URL</c> (for example
    /// <c>http://127.0.0.1:8123/releases</c>) points the app at a test server instead.
    /// </summary>
    public static UpdateEndpoint Default
    {
        get
        {
#if FM_DEV
            if (Environment.GetEnvironmentVariable("FM_UPDATE_BASE_URL") is { Length: > 0 } custom &&
                ForTestServer(custom) is { } test)
                return test;
#endif
            return GitHub;
        }
    }

#if FM_DEV
    /// <summary>A local test server (developer builds and self-tests only).</summary>
    public static UpdateEndpoint? ForTestServer(string root)
    {
        if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;
        return new UpdateEndpoint(root, [uri.Host, .. GitHubHosts], allowLoopbackHttp: true);
    }
#endif

    /// <summary>HTTPS to an allowed host on the default port; plain HTTP only to a loopback test server.</summary>
    public bool IsAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        if (!AllowedHosts.Any(h => string.Equals(h, uri.IdnHost, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (uri.Scheme == Uri.UriSchemeHttps)
            return uri.IsDefaultPort || AllowLoopbackHttp && IsLoopback(uri);
        return uri.Scheme == Uri.UriSchemeHttp && AllowLoopbackHttp && IsLoopback(uri);
    }

    static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip);
}

/// <summary>Where downloaded setups wait until they run.</summary>
public static class UpdatePaths
{
    /// <summary><c>%LOCALAPPDATA%\FolderManaging\Updates</c>; developer builds may set <c>FM_UPDATE_DIR</c>.</summary>
    public static string DownloadDirectory
    {
        get
        {
#if FM_DEV
            if (Environment.GetEnvironmentVariable("FM_UPDATE_DIR") is { Length: > 0 } dev)
                return Path.GetFullPath(dev);
#endif
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderManaging", "Updates");
        }
    }
}
