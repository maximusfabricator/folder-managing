// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.IO;
using Microsoft.Win32;

namespace FolderManaging.Companion;

/// <summary>
/// «Запускать вместе с Windows»: HKCU Run entry that starts the tray through the package's app execution alias.
/// Package files under WindowsApps cannot be launched by path, only through the alias (user edition manifest).
/// </summary>
internal static class AutostartService
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "FolderManaging";
    internal const string AliasFileName = "foldermanaging.exe";
    internal const string TrayArgument = "--tray";

    static string AliasPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            AliasFileName);

    /// <summary>False for dev packages without the execution alias.</summary>
    internal static bool IsAvailable => File.Exists(AliasPath);

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string s && s.Length > 0;
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
            key.SetValue(ValueName, $"\"{AliasPath}\" {TrayArgument}", RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
