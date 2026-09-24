using System.Diagnostics;
using System.Reflection;
using System.Text;
using FolderManaging.Core.Diagnostics;

namespace FolderManaging.Shell;

/// <summary>Locates and launches <c>FolderManaging.Companion.exe</c> from the COM surrogate (medium-IL full-trust).</summary>
internal static class CompanionLauncher
{
    const string CompanionExeName = "FolderManaging.Companion.exe";

    internal static bool TryLaunch(out string errorMessage) =>
        TryLaunch(Array.Empty<string>(), out errorMessage);

    /// <summary>Starts Companion with optional args (e.g. <c>--quick-color --path "…"</c>).</summary>
    internal static bool TryLaunch(IReadOnlyList<string> arguments, out string errorMessage)
    {
        errorMessage = "";
        try
        {
            if (!TryResolveCompanionPath(out var exe))
            {
                errorMessage = $"Companion not found. Tried: {string.Join("; ", GetCandidateInstallRoots().Select(d => System.IO.Path.Combine(d, CompanionExeName)))}";
                TraceLog.Append("shell-menu.log", $"[COM] [FAIL] CompanionLauncher: {errorMessage}");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? string.Empty,
            };
            if (arguments.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var a in arguments)
                {
                    if (sb.Length > 0)
                        sb.Append(' ');
                    sb.Append(QuoteArg(a));
                }

                psi.Arguments = sb.ToString();
            }

            using var p = Process.Start(psi);
            var argPreview = arguments.Count == 0 ? "(none)" : psi.Arguments;
            TraceLog.Append("shell-menu.log", $"[COM] [SUCCESS] CompanionLauncher started: {exe} args={argPreview}");
            return p is not null;
        }
        catch (Exception ex)
        {
            errorMessage = ex.ToString();
            TraceLog.Append("shell-menu.log", $"[COM] [FAIL] CompanionLauncher: {errorMessage}");
            return false;
        }
    }

    static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        if (value.Contains('"') || value.Contains(' ') || value.Contains('\t'))
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        return value;
    }

    static bool TryResolveCompanionPath(out string exePath)
    {
        foreach (var dir in GetCandidateInstallRoots())
        {
            var candidate = System.IO.Path.Combine(dir, CompanionExeName);
            if (System.IO.File.Exists(candidate))
            {
                exePath = candidate;
                return true;
            }
        }

        exePath = "";
        return false;
    }

    static IReadOnlyList<string> GetCandidateInstallRoots()
    {
        var roots = new List<string>();

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return;
            try
            {
                var full = System.IO.Path.GetFullPath(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                if (roots.Contains(full, StringComparer.OrdinalIgnoreCase))
                    return;
                roots.Add(full);
            }
            catch
            {
                // ignore invalid paths
            }
        }

        var asm = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(asm))
            Add(System.IO.Path.GetDirectoryName(asm));

        Add(AppContext.BaseDirectory);

        try
        {
            var main = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(main))
                Add(System.IO.Path.GetDirectoryName(main));
        }
        catch
        {
            // MainModule can throw under restricted ACLs
        }

        return roots;
    }
}
