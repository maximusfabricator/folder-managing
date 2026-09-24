using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;

namespace FolderManaging.Shell.Diagnostics;

/// <summary>
/// Crash-resilient tracing for COM / dllhost. Writes the <b>same line to every path that accepts a write</b>
/// (MSIX may block <c>C:\Users\Public</c> on some builds; <c>ProgramData</c>, profile, <c>LocalAppData</c>, <c>%TEMP%</c> usually work).
/// </summary>
internal static class ExtremeLogger
{
    const string TraceFileName = "FolderManager_ExtremeTrace.log";
    const string CrashFileName = "FolderManagerCrash.txt";
    const string PointerFileName = "FOLDERMANAGING_EXTREME_LOG.txt";

    static readonly object Gate = new();
    static int _bannerWritten;
    static int _pointerWritten;

    internal static void Log(string message, [CallerMemberName] string caller = "")
    {
        try
        {
            EnsureBannerOnce();
            WriteLineAllTargets($"{Timestamp()}\t[{caller}]\t{message}");
        }
        catch
        {
            // Tracing must never throw.
        }
    }

    internal static void LogException(Exception ex, string context, [CallerMemberName] string caller = "")
    {
        try
        {
            EnsureBannerOnce();
            var body = $"{Timestamp()}\t[{caller}]\t[EXCEPTION] {context}\r\n{ex}\r\n";
            WriteLineAllTargets(body);
            WriteCrashAllTargets(body);
        }
        catch
        {
            // ignore
        }
    }

    internal static void LogEnvironmentSnapshot(string context)
    {
        try
        {
            EnsureBannerOnce();
            var sb = new StringBuilder(2048);
            sb.AppendLine($"{Timestamp()}\t[ENV]\t{context}");

            sb.AppendLine($"  pid={Environment.ProcessId}  hostExe={SafeMainModule()}");
            sb.AppendLine($"  identity={SafeIdentity()}");
            sb.AppendLine($"  cwd={SafeCurrentDir()}");
            sb.AppendLine($"  AppContext.BaseDirectory={AppContext.BaseDirectory}");
            sb.AppendLine($"  AppDomain.BaseDirectory={AppDomain.CurrentDomain.BaseDirectory}");
            sb.AppendLine($"  ShellAssembly.Location={SafeShellAssemblyLocation()}");
            sb.AppendLine($"  CLRVersion={Environment.Version}");
            sb.AppendLine($"  Is64Bit={Environment.Is64BitProcess}");
            sb.AppendLine($"  USERPROFILE={Environment.GetEnvironmentVariable("USERPROFILE")}");
            sb.AppendLine($"  LOCALAPPDATA={Environment.GetEnvironmentVariable("LOCALAPPDATA")}");
            sb.AppendLine($"  TEMP={Path.GetTempPath()}");
            sb.AppendLine($"  PUBLIC={Environment.GetEnvironmentVariable("PUBLIC")}");
            sb.AppendLine($"  ProgramData={Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}");
            sb.AppendLine($"  traceTargets={string.Join(" | ", EnumerateTracePaths(TraceFileName))}");

            WriteLineAllTargets(sb.ToString().TrimEnd() + Environment.NewLine);
        }
        catch (Exception ex)
        {
            LogException(ex, "snapshot failed");
        }
    }

    /// <summary>First thing in module init: touch disk before any other managed setup.</summary>
    internal static void BootTouch()
    {
        try
        {
            var stamp = $"boot-touch {DateTimeOffset.Now:O} pid={Environment.ProcessId}{Environment.NewLine}";
            WriteRawAllTargets(TraceFileName, stamp);
            TryWritePointerFile();
        }
        catch
        {
            // ignore
        }
    }

    static void EnsureBannerOnce()
    {
        if (Interlocked.CompareExchange(ref _bannerWritten, 1, 0) != 0)
            return;

        var banner =
            $"================ ExtremeLogger session ================{Environment.NewLine}" +
            $"  started={Timestamp()}{Environment.NewLine}" +
            $"  targets={string.Join(Environment.NewLine + "  ", EnumerateTracePaths(TraceFileName))}{Environment.NewLine}" +
            $"  hostExe={SafeMainModule()}{Environment.NewLine}" +
            $"  identity={SafeIdentity()}{Environment.NewLine}" +
            $"  shellAssembly={SafeShellAssemblyLocation()}{Environment.NewLine}" +
            $"  AppContext.BaseDirectory={AppContext.BaseDirectory}{Environment.NewLine}";
        WriteLineAllTargets(banner);
        TryWritePointerFile();
    }

    static void TryWritePointerFile()
    {
        if (Interlocked.CompareExchange(ref _pointerWritten, 1, 0) != 0)
            return;

        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile))
                return;

            var dir = Path.Combine(profile, ".FolderManaging");
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, PointerFileName);
            var lines = new StringBuilder(512);
            lines.AppendLine("Folder Managing — where to find COM / dllhost logs:");
            lines.AppendLine();
            foreach (var t in EnumerateTracePaths(TraceFileName))
                lines.AppendLine("  " + t);
            lines.AppendLine();
            lines.AppendLine("Also check the same filenames under ProgramData\\FolderManaging and %TEMP%.");
            File.WriteAllText(p, lines.ToString(), Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }

    static void WriteLineAllTargets(string text)
    {
        var line = text.EndsWith(Environment.NewLine, StringComparison.Ordinal) ? text : text + Environment.NewLine;
        lock (Gate)
        {
            foreach (var path in EnumerateTracePaths(TraceFileName))
                TryAppend(path, line);
        }
    }

    static void WriteCrashAllTargets(string text)
    {
        lock (Gate)
        {
            foreach (var path in EnumerateTracePaths(CrashFileName))
                TryAppend(path, text);
        }
    }

    static void WriteRawAllTargets(string fileName, string text)
    {
        foreach (var path in EnumerateTracePaths(fileName))
            TryAppend(path, text);
    }

    static IEnumerable<string> EnumerateTracePaths(string fileName)
    {
        // Order: profile + LocalAppData first (usually writable), then TEMP, then Public / ProgramData.
        // (Cannot yield inside try/catch — CS1626 — so resolve paths outside try blocks.)
        string? profile = null;
        try
        {
            profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        catch
        {
            profile = null;
        }

        if (!string.IsNullOrEmpty(profile))
            yield return Path.Combine(profile, ".FolderManaging", fileName);

        string? local = null;
        try
        {
            local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        catch
        {
            local = null;
        }

        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, "FolderManaging", fileName);

        yield return Path.Combine(Path.GetTempPath(), fileName);

        var pub = Environment.GetEnvironmentVariable("PUBLIC");
        if (string.IsNullOrEmpty(pub))
            pub = @"C:\Users\Public";
        yield return Path.Combine(pub, fileName);
        yield return Path.Combine(pub, "Documents", fileName);

        string? pd = null;
        try
        {
            pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        }
        catch
        {
            pd = null;
        }

        if (!string.IsNullOrEmpty(pd))
            yield return Path.Combine(pd, "FolderManaging", fileName);
    }

    static bool TryAppend(string path, string line)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(path, line, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string Timestamp() => DateTimeOffset.Now.ToString("O");

    static string SafeIdentity()
    {
        try
        {
            return WindowsIdentity.GetCurrent().Name;
        }
        catch (Exception ex)
        {
            return "(identity error: " + ex.Message + ")";
        }
    }

    static string SafeMainModule()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName ?? "(unknown)";
        }
        catch (Exception ex)
        {
            return "(MainModule error: " + ex.Message + ")";
        }
    }

    static string SafeCurrentDir()
    {
        try
        {
            return Environment.CurrentDirectory;
        }
        catch (Exception ex)
        {
            return "(cwd error: " + ex.Message + ")";
        }
    }

    static string SafeShellAssemblyLocation()
    {
        try
        {
            return typeof(ExtremeLogger).Assembly.Location;
        }
        catch (Exception ex)
        {
            return "(assembly error: " + ex.Message + ")";
        }
    }

    internal static string ShellAssemblyDirectory
    {
        get
        {
            try
            {
                return Path.GetDirectoryName(typeof(ExtremeLogger).Assembly.Location) ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
