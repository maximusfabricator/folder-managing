// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Reflection;

namespace FolderManaging.Core;

/// <summary>
/// Authorship shown in the app and the installer. GPL-2.0 requires the copyright notice to stay intact
/// in every copy and in changed versions.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "Folder Managing";
    public const string Author = "maximusfabricator";
    public const string Copyright = "Copyright (C) 2026 maximusfabricator";
    public const string SourceUrl = "https://github.com/maximusfabricator/folder-managing";
    public const string LicenseUrl = "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html";
    public const string NoticesUrl = SourceUrl + "/blob/main/THIRD-PARTY-NOTICES.md";

    /// <summary>Files copied next to the app (see the Companion project) and opened from About.</summary>
    public const string LicenseFileName = "LICENSE.txt";
    public const string NoticesFileName = "THIRD-PARTY-NOTICES.txt";

    public static string Version =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
