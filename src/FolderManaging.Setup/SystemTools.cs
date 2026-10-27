// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using FolderManaging.Core.Localization;

namespace FolderManaging.Setup;

internal static class SystemTools
{
    const int ErrorCancelled = 1223;

    /// <summary>Runs a PowerShell snippet (Appx cmdlets). Throws with the error text when it fails.</summary>
    internal static string RunPowerShell(string script)
    {
        SetupLog.Write($"powershell> {script}");
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " + script);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException(UiText.Get("PowershellStartFail"));
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        var stdout = stdoutTask.Result.Trim();
        var stderr = stderrTask.Result.Trim();
        if (stdout.Length > 0)
            SetupLog.Write("  out: " + stdout);
        if (p.ExitCode != 0)
        {
            SetupLog.Write($"  exit {p.ExitCode}: {stderr}");
            throw new InvalidOperationException(stderr.Length > 0 ? stderr : UiText.Format("PowershellExit", p.ExitCode));
        }

        return stdout;
    }

    internal static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>Re-launches this exe elevated with <paramref name="arguments"/>; returns its exit code.</summary>
    internal static int RunElevated(IReadOnlyList<string> arguments)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException(UiText.Get("InstallerPathMissing"));
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(' ', arguments.Select(QuoteArgument)),
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        SetupLog.Write($"elevate> {psi.Arguments}");
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException(UiText.Get("AdminStepStartFail"));
            p.WaitForExit();
            SetupLog.Write($"  elevated exit {p.ExitCode}");
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException(UiText.Get("AdminDenied"));
        }
    }

    static string QuoteArgument(string value) =>
        value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')
            ? value
            : "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    internal static void StopProcesses(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                    SetupLog.Write($"stopped {name} pid={p.Id}");
                }
                catch (Exception ex)
                {
                    SetupLog.Write($"could not stop {name} pid={p.Id}: {ex.Message}");
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }

    /// <summary>Restarts Explorer so it drops cached shell extensions; reopens the shell if Windows did not.</summary>
    internal static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try
            {
                p.Kill();
                p.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                SetupLog.Write($"explorer stop failed: {ex.Message}");
            }
            finally
            {
                p.Dispose();
            }
        }

        for (var i = 0; i < 20; i++)
        {
            Thread.Sleep(250);
            if (Process.GetProcessesByName("explorer").Length > 0)
                return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
    }

    internal static void CreateShortcut(string shortcutPath, string targetPath, string arguments, string iconPath, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(shortcutPath);
            link.TargetPath = targetPath;
            link.Arguments = arguments;
            link.IconLocation = iconPath + ",0";
            link.Description = description;
            link.WorkingDirectory = Path.GetDirectoryName(iconPath);
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>The shell extension is framework-dependent (comhost cannot be self-contained), so .NET 8 Desktop Runtime x64 is required.</summary>
    internal static bool IsDesktopRuntime8Installed()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        if (!Directory.Exists(root))
            return false;
        return Directory.EnumerateDirectories(root, "8.*").Any();
    }

    internal static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            SetupLog.Write($"delete failed '{path}': {ex.Message}");
        }
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            SetupLog.Write($"delete failed '{path}': {ex.Message}");
        }
    }
}
