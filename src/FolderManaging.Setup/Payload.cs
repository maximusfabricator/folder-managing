// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.IO.Compression;
using FolderManaging.Core.Localization;

namespace FolderManaging.Setup;

/// <summary>Product files embedded into Setup.exe by Build-UserRelease.ps1 (zip resource <c>payload.zip</c>).</summary>
internal static class Payload
{
    const string ResourceName = "payload.zip";

    internal const string PackageFileName = "FolderManaging.msix";
    internal const string CertificateFileName = "FolderManaging.cer";
    internal const string PropdescFileName = "FolderManaging.propdesc";
    internal const string RuntimeInstallerFileName = "windowsdesktop-runtime-win-x64.exe";

    /// <summary>True for the downloadable Setup.exe, false for the installed maintenance copy.</summary>
    internal static bool IsEmbedded =>
        typeof(Payload).Assembly.GetManifestResourceInfo(ResourceName) is not null;

    internal static string ExtractToTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FolderManagingSetup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        using var stream = typeof(Payload).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(UiText.Get("PayloadBroken"));
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.ExtractToDirectory(dir, overwriteFiles: true);
        SetupLog.Write($"payload extracted to {dir}");
        return dir;
    }
}
