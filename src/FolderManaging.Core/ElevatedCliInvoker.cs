// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>
/// Keeps one elevated CLI worker alive (named pipe) so protected-folder color/reset/tags need UAC only once,
/// not on every action. Medium-IL callers send commands to the worker after the first consent.
/// </summary>
public static class ElevatedCliInvoker
{
    const string PipePrefix = "FolderManaging.Elevated.";

    /// <summary>Error text of <see cref="EnsureWorkerStarted"/> when the user said No to UAC.</summary>
    public const string UacCancelledMessage = "User cancelled UAC.";
    const int ConnectTimeoutMs = 1_500;
    const int StartTimeoutMs = 90_000;

    /// <summary>Pipe name for the current Windows user (local only).</summary>
    public static string PipeName
    {
        get
        {
            try
            {
                var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
                return PipePrefix + sid;
            }
            catch
            {
                return PipePrefix + "default";
            }
        }
    }

    /// <summary>True when the elevated worker answers PING.</summary>
    public static bool IsWorkerRunning()
    {
        try
        {
            return string.Equals(Send("PING"), "OK", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures the elevated worker is running. Shows UAC once if it is not already up.
    /// Returns <c>false</c> if the user cancelled UAC or the worker failed to start.
    /// </summary>
    public static bool EnsureWorkerStarted(out string errorMessage)
    {
        errorMessage = "";
        if (IsWorkerRunning())
        {
            ClickTrace.Step("elevated worker already running");
            return true;
        }

        if (!TryResolveCliPath(out var cli))
        {
            errorMessage = "FolderManaging.Cli.exe not found next to the app.";
            ClickTrace.Step($"elevated worker: {errorMessage}");
            return false;
        }

        try
        {
            ClickTrace.Step($"elevated worker: starting with UAC → {cli} --elevated-worker");
            using var p = Process.Start(new ProcessStartInfo(cli, "--elevated-worker")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null)
            {
                errorMessage = "Could not start elevated worker.";
                return false;
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            errorMessage = UacCancelledMessage;
            ClickTrace.Step("elevated worker: user cancelled UAC");
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            ClickTrace.Step($"elevated worker start failed: {ex.Message}");
            return false;
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < StartTimeoutMs)
        {
            if (IsWorkerRunning())
            {
                ClickTrace.Step($"elevated worker ready in {sw.ElapsedMilliseconds}ms");
                return true;
            }

            Thread.Sleep(200);
        }

        errorMessage = "Elevated worker did not become ready (UAC timeout or start failure).";
        ClickTrace.Step(errorMessage);
        return false;
    }

    /// <summary>Asks a running worker to exit (best-effort). Used when auto-elevate is turned off.</summary>
    public static void TryStopWorker()
    {
        try
        {
            if (!IsWorkerRunning())
                return;
            var r = Send("SHUTDOWN");
            ClickTrace.Step($"elevated worker shutdown → {r}");
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"elevated worker shutdown failed: {ex.Message}");
        }
    }

    /// <summary>
    /// After the UI language changes: a running worker renames the Explorer columns (one title for the whole PC, HKLM).
    /// Without a worker the columns follow the next time it starts; no UAC prompt is shown for this.
    /// </summary>
    public static void TrySyncLanguage()
    {
        try
        {
            if (IsWorkerRunning())
                ClickTrace.Step($"elevated worker language sync → {Send("LANGUAGE")}");
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"elevated worker language sync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs color/reset/tag/sync via the elevated worker. Starts the worker (one UAC) if needed when auto-elevate is on
    /// or when <paramref name="allowStartWorker"/> is true.
    /// </summary>
    public static bool TryInvoke(string action, string folderPath, string? value, out string errorMessage, bool allowStartWorker = true)
    {
        errorMessage = "";
        var cmd = BuildCommand(action, folderPath, value);
        if (cmd is null)
        {
            errorMessage = $"Unsupported elevated action '{action}'.";
            return false;
        }

        try
        {
            if (IsWorkerRunning())
            {
                var reply = Send(cmd);
                return InterpretReply(reply, out errorMessage);
            }
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"elevated worker first try failed: {ex.Message}");
        }

        if (!allowStartWorker)
        {
            errorMessage = "Elevated worker is not running.";
            return false;
        }

        if (!EnsureWorkerStarted(out errorMessage))
            return false;

        try
        {
            var reply = Send(cmd);
            return InterpretReply(reply, out errorMessage);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            ClickTrace.Step($"elevated worker invoke failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Blocking server loop for <c>--elevated-worker</c> (must run elevated).</summary>
    public static int RunWorkerServer()
    {
        try
        {
            if (!IsProcessElevated())
            {
                Console.Error.WriteLine("elevated-worker must run as administrator.");
                return 20;
            }

            UiText.ApplyToProcess();
            TraceLog.Append("shell-menu.log", $"[WORKER] listening pipe={PipeName}");
            ClickTrace.Step($"elevated worker listening on {PipeName}");
            try
            {
                ShellProperties.PropertySchemaRegistration.SyncColumnLabelForCurrentLanguage();
            }
            catch (Exception ex)
            {
                ClickTrace.Step($"column label sync failed: {ex.Message}");
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            while (!cts.IsCancellationRequested)
            {
                var security = CreatePipeSecurityForCurrentUser();
                using var server = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity: security);

                try
                {
                    server.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    writer.WriteLine("ERR\tempty");
                    continue;
                }

                ClickTrace.Step($"elevated worker recv: {Truncate(line, 160)}");
                var reply = HandleWorkerCommand(line.Trim());
                writer.WriteLine(reply);
                ClickTrace.Step($"elevated worker reply: {Truncate(reply, 120)}");

                if (line.StartsWith("SHUTDOWN", StringComparison.OrdinalIgnoreCase))
                    break;
            }

            TraceLog.Append("shell-menu.log", "[WORKER] stopped");
            return 0;
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[WORKER] crash: {ex}");
            Console.Error.WriteLine(ex);
            return 21;
        }
    }

    static string HandleWorkerCommand(string line)
    {
        try
        {
            var parts = line.Split('\t');
            var op = parts[0].Trim().ToUpperInvariant();
            switch (op)
            {
                case "PING":
                    return "OK";
                case "SHUTDOWN":
                    return "OK";
                case "COLOR" or "RGB" or "RESET" or "TAG" or "UNTAG" or "SYNC" or "UNBLOCK":
                {
                    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                        return $"ERR\t{op} needs a path";
                    var value = parts.Length > 2 ? parts[2] : null;
                    if (op == "UNBLOCK")
                    {
                        FolderDiagnostics.RemoveDownloadMarks(parts[1]);
                        return "OK";
                    }

                    FolderAction.TryParseKind(op, out var kind);
                    FolderBatch.Execute(new FolderAction(kind, parts[1], value));
                    return "OK";
                }
                case "LANGUAGE":
                    UiText.ApplyToProcess();
                    ShellProperties.PropertySchemaRegistration.SyncColumnLabelForCurrentLanguage();
                    return "OK";
                default:
                    return $"ERR\tunknown op {op}";
            }
        }
        catch (Exception ex)
        {
            return "ERR\t" + ex.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    static string? BuildCommand(string action, string folderPath, string? value)
    {
        var path = folderPath.TrimEnd('\\');
        return action.ToLowerInvariant() switch
        {
            "color" when !string.IsNullOrWhiteSpace(value) => $"COLOR\t{path}\t{value}",
            "rgb" when !string.IsNullOrWhiteSpace(value) => $"RGB\t{path}\t{value}",
            "reset" => $"RESET\t{path}",
            "tag" when !string.IsNullOrWhiteSpace(value) => $"TAG\t{path}\t{value}",
            "untag" when !string.IsNullOrWhiteSpace(value) => $"UNTAG\t{path}\t{value}",
            "sync" => string.IsNullOrWhiteSpace(value) ? $"SYNC\t{path}" : $"SYNC\t{path}\t{value}",
            "unblock" => $"UNBLOCK\t{path}",
            _ => null,
        };
    }

    static bool InterpretReply(string? reply, out string errorMessage)
    {
        errorMessage = "";
        if (string.IsNullOrWhiteSpace(reply))
        {
            errorMessage = "Empty reply from elevated worker.";
            return false;
        }

        if (reply.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            reply.StartsWith("OK\t", StringComparison.OrdinalIgnoreCase))
        {
            ClickTrace.Step("elevated worker invoke OK");
            return true;
        }

        errorMessage = reply.StartsWith("ERR\t", StringComparison.OrdinalIgnoreCase)
            ? reply[4..]
            : reply;
        ClickTrace.Step($"elevated worker invoke FAIL: {errorMessage}");
        return false;
    }

    static string Send(string command)
    {
        using var client = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.None);
        client.Connect(ConnectTimeoutMs);
        using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        writer.WriteLine(command);
        return reader.ReadLine() ?? "";
    }

    static PipeSecurity CreatePipeSecurityForCurrentUser()
    {
        var security = new PipeSecurity();
        var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        security.AddAccessRule(new PipeAccessRule(
            user,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    static bool TryResolveCliPath(out string cliPath)
    {
        foreach (var dir in GetCandidateRoots())
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

    static IEnumerable<string> GetCandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var raw = new List<string?>
        {
            AppContext.BaseDirectory,
        };
        // Empty in the single-file maintenance tool, where AppContext.BaseDirectory already is the right folder.
#pragma warning disable IL3000
        var asm = Assembly.GetExecutingAssembly().Location;
#pragma warning restore IL3000
        if (!string.IsNullOrEmpty(asm))
            raw.Add(Path.GetDirectoryName(asm));
        try
        {
            raw.Add(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName));
        }
        catch
        {
            // MainModule can throw under restricted ACLs
        }

        foreach (var dir in raw)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            string full;
            try
            {
                full = Path.GetFullPath(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch
            {
                continue;
            }

            if (seen.Add(full))
                yield return full;
        }
    }
}
