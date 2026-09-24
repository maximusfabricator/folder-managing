using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;

namespace FolderManaging.Shell;

internal static class ShellCliLauncher
{
    /// <summary>
    /// Runs <c>FolderManaging.Cli.exe</c> with <c>--action</c> in a child process. Prefer this for color/pin/unpin
    /// from the packaged COM surrogate: the CLI runs as the package's full-trust Win32 app and avoids surrogate FS limits.
    /// </summary>
    internal static bool TryInvokeVerb(string action, string folderPath, string? value, string? tag, out string errorMessage)
    {
        errorMessage = "";
        try
        {
            TraceLog.Append(
                "shell-menu.log",
                $"[COM] [START] TryInvokeVerb action={action} path={folderPath} value={value ?? "(none)"} tag={tag ?? "(none)"}");

            if (!TryResolveCliExecutablePath(out var cli))
            {
                errorMessage =
                    $"CLI not found. Tried: {string.Join("; ", GetCandidateInstallRoots().Select(static d => Path.Combine(d, "FolderManaging.Cli.exe")))}";
                TraceLog.Append("shell-menu.log", $"[COM] [FAIL] {errorMessage}");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = cli,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            psi.ArgumentList.Add("--action");
            psi.ArgumentList.Add(action);
            psi.ArgumentList.Add("--path");
            psi.ArgumentList.Add(folderPath);

            if (string.Equals(action, "color", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    errorMessage = "color requires --value.";
                    return false;
                }

                psi.ArgumentList.Add("--value");
                psi.ArgumentList.Add(value!);
            }
            else if (string.Equals(action, "tag", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    errorMessage = "tag requires --tag.";
                    TraceLog.Append("shell-menu.log", $"[COM] [FAIL] {errorMessage}");
                    return false;
                }

                psi.ArgumentList.Add("--tag");
                psi.ArgumentList.Add(tag!);
            }
            else if (string.Equals(action, "reset", StringComparison.OrdinalIgnoreCase))
            {
                // no extra arguments
            }

            TraceLog.Append("shell-menu.log", $"[COM] [PROCESS] starting CLI: {cli}");

            using var p = Process.Start(psi);
            if (p is null)
            {
                errorMessage = "Process.Start returned null.";
                return false;
            }

            if (!p.WaitForExit(120_000))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }

                errorMessage = "CLI timed out after 120s.";
                TraceLog.Append("shell-menu.log", $"[COM] [FAIL] {errorMessage}");
                return false;
            }

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            if (p.ExitCode != 0)
            {
                errorMessage = $"CLI exit code {p.ExitCode}. stderr={stderr.Trim()} stdout={stdout.Trim()}";
                TraceLog.Append("shell-menu.log", $"[COM] [FAIL] {errorMessage}");
                return false;
            }

            TraceLog.Append("shell-menu.log", $"[COM] [SUCCESS] action={action} exit=0");
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.ToString();
            TraceLog.Append("shell-menu.log", $"[COM] [FAIL] exception: {errorMessage}");
            return false;
        }
    }

    internal static void TryLaunchShellPreview(string kind, string folderPath, string? token)
    {
        try
        {
            if (!TryResolveCliExecutablePath(out var cli))
            {
                ShellActionLog.Write(
                    $"shell-preview skipped (CLI missing). Tried: {string.Join("; ", GetCandidateInstallRoots().Select(static d => Path.Combine(d, "FolderManaging.Cli.exe")))}");
                return;
            }

            var args = new StringBuilder();
            args.Append("shell-preview ");
            args.Append("--kind ").Append(Quote(kind)).Append(' ');
            args.Append("--path ").Append(Quote(folderPath));
            if (!string.IsNullOrWhiteSpace(token))
            {
                args.Append(" --token ").Append(Quote(token!));
            }

            Process.Start(new ProcessStartInfo(cli, args.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            ShellActionLog.Write($"shell-preview launch failed: {ex.Message}");
        }
    }

    /// <summary>Re-runs a color/reset via the CLI with a UAC prompt (for protected folders such as <c>C:\Program Files</c>).</summary>
    internal static bool TryInvokeVerbElevated(string action, string folderPath, string? value) =>
        ElevatedCliInvoker.TryInvoke(action, folderPath, value, out _);

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    /// <summary>
    /// COM/comhost often leaves <see cref="AppContext.BaseDirectory"/> empty; the shell DLL and CLI are siblings in the MSIX root.
    /// </summary>
    private static bool TryResolveCliExecutablePath(out string cliPath)
    {
        foreach (var dir in GetCandidateInstallRoots())
        {
            var candidate = Path.Combine(dir, "FolderManaging.Cli.exe");
            if (File.Exists(candidate))
            {
                cliPath = candidate;
                return true;
            }
        }

        cliPath = "";
        return false;
    }

    private static IReadOnlyList<string> GetCandidateInstallRoots()
    {
        var roots = new List<string>();

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return;
            try
            {
                var full = Path.GetFullPath(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
            Add(Path.GetDirectoryName(asm));

        Add(AppContext.BaseDirectory);

        try
        {
            var main = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(main))
                Add(Path.GetDirectoryName(main));
        }
        catch
        {
            // MainModule can throw (e.g. restricted ACLs)
        }

        return roots;
    }
}
