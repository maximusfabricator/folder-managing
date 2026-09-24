using System.Text;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core.Diagnostics;

/// <summary>Append-only tracing for COM shell handlers and CLI (mirrored to two locations).</summary>
public static class TraceLog
{
    static readonly object Gate = new();
    static int _hintWritten;

    /// <summary>Primary (classic) location under %LocalAppData%.</summary>
    public static string LocalAppDataLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderManaging");

    /// <summary>Secondary location next to <c>state.json</c> (%USERPROFILE%\.FolderManaging\).</summary>
    public static string ProfileLogDirectory => AppPaths.DataDirectory;

    /// <summary>Hard-fallback location: writable for any user, never virtualized.</summary>
    public static string PublicLogDirectory =>
        Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

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
                "Folder Managing — куда смотреть после клика в меню / Companion\r\n" +
                "\r\n" +
                "Главный лог кликов (строки [CLICK ….]):\r\n" +
                "  " + a + "\r\n" +
                "  " + b + "\r\n" +
                "  " + c + "\r\n" +
                "\r\n" +
                "Подробный COM / dllhost трейс:\r\n" +
                "  " + extreme + "\r\n" +
                "\r\n" +
                "Ищите: [CLICK id] START … RESULT=OK или RESULT=FAIL\r\n",
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
}
