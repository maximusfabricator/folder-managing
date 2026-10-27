// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Text;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core.Diagnostics;

/// <summary>
/// Append-only tracing for COM shell handlers, CLI and Companion.
/// Dev edition mirrors every line to several folders; the user edition keeps only failures in one size-capped file.
/// </summary>
public static class TraceLog
{
    static readonly object Gate = new();

    /// <summary>Primary (classic) location under %LocalAppData%.</summary>
    public static string LocalAppDataLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderManaging");

    /// <summary>Secondary location next to <c>state.json</c> (%USERPROFILE%\.FolderManaging\).</summary>
    public static string ProfileLogDirectory => AppPaths.DataDirectory;

#if FM_DEV
    static int _hintWritten;

    /// <summary>Hard-fallback location: writable for any user, never virtualized.</summary>
    public static string PublicLogDirectory =>
        Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

    /// <summary>Folder the Companion opens from «Открыть логи…».</summary>
    public static string UserVisibleLogDirectory => ProfileLogDirectory;

    public static void Append(string fileName, string message)
    {
        var line = $"{DateTimeOffset.Now:O}\t{message}{Environment.NewLine}";
        lock (Gate)
        {
            foreach (var dir in LogRoots())
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, fileName), line, Encoding.UTF8);
                }
                catch
                {
                    // never throw from tracing
                }
            }

            if (Interlocked.Increment(ref _hintWritten) == 1)
                TryWriteLogLocationsHint();
        }
    }

    /// <summary>Rewrites <c>WHERE-ARE-LOGS.txt</c> under the profile data folder.</summary>
    public static void EnsureLogLocationsHint() => TryWriteLogLocationsHint();

    static void TryWriteLogLocationsHint()
    {
        try
        {
            Directory.CreateDirectory(ProfileLogDirectory);
            var hintPath = Path.Combine(ProfileLogDirectory, "WHERE-ARE-LOGS.txt");
            var a = Path.Combine(LocalAppDataLogDirectory, "shell-menu.log");
            var b = Path.Combine(ProfileLogDirectory, "shell-menu.log");
            var c = Path.Combine(PublicLogDirectory, "shell-menu.log");
            var extreme = Path.Combine(ProfileLogDirectory, "FolderManager_ExtremeTrace.log");
            File.WriteAllText(
                hintPath,
                UiText.Format("LogReadme", "  " + a + "\r\n  " + b + "\r\n  " + c, "  " + extreme),
                Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }

    static IEnumerable<string> LogRoots()
    {
        yield return LocalAppDataLogDirectory;
        yield return ProfileLogDirectory;
        yield return PublicLogDirectory;
    }
#else
    const string ErrorLogFileName = "errors.log";
    const long MaxErrorLogBytes = 512 * 1024;

    static readonly string[] FailureMarkers = ["FAIL", "ERROR", "EXCEPTION", "crash", "failed"];

    /// <summary>Folder the Companion opens from «Журнал ошибок…».</summary>
    public static string UserVisibleLogDirectory => LocalAppDataLogDirectory;

    public static string ErrorLogPath => Path.Combine(LocalAppDataLogDirectory, ErrorLogFileName);

    /// <summary>Keeps only failure lines; <paramref name="fileName"/> is ignored (single error log).</summary>
    public static void Append(string fileName, string message)
    {
        if (!IsFailure(message))
            return;

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}\t{message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LocalAppDataLogDirectory);
                var path = ErrorLogPath;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxErrorLogBytes)
                    File.Move(path, Path.ChangeExtension(path, ".old.log"), overwrite: true);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch
            {
                // never throw from tracing
            }
        }
    }

    public static void EnsureLogLocationsHint()
    {
    }

    static bool IsFailure(string message)
    {
        foreach (var marker in FailureMarkers)
        {
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
#endif
}
