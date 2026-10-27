// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;

namespace FolderManaging.Core.Diagnostics;

/// <summary>
/// One correlated log session per user click (context menu or Companion button).
/// Lines go to <c>shell-menu.log</c> via <see cref="TraceLog"/> with a short id so you can follow START → steps → OK/FAIL.
/// </summary>
public static class ClickTrace
{
    static readonly AsyncLocal<string?> CurrentId = new();
    static readonly AsyncLocal<string?> CurrentContext = new();
    static int _seq;

    public static string? Current => CurrentId.Value;

    /// <summary>Starts a click session. Dispose (or call <see cref="Ok"/> / <see cref="Fail"/>) to finish.</summary>
    public static Scope Begin(string source, string action, string? detail = null, string? path = null)
    {
        var id = $"{Interlocked.Increment(ref _seq):D4}-{Guid.NewGuid().ToString("N")[..6]}";
        CurrentId.Value = id;
        TraceLog.EnsureLogLocationsHint();
        var detailPart = string.IsNullOrWhiteSpace(detail) ? "" : $" detail={detail}";
        var pathPart = string.IsNullOrWhiteSpace(path) ? "" : $" path='{path}'";
        CurrentContext.Value = $"{source}/{action}{detailPart}{pathPart}";
        Write(id, $"START source={source} action={action}{detailPart}{pathPart} pid={Environment.ProcessId}");
        return new Scope(id);
    }

    public static void Step(string message)
    {
        var id = CurrentId.Value;
        if (id is null)
        {
            TraceLog.Append("shell-menu.log", $"[CLICK] {message}");
            return;
        }

        Write(id, message);
    }

    public static void Ok(string? summary = null)
    {
        var id = CurrentId.Value ?? "????";
        Write(id, string.IsNullOrWhiteSpace(summary) ? "RESULT=OK" : $"RESULT=OK {summary}");
        CurrentId.Value = null;
    }

    public static void Fail(Exception ex)
    {
        var id = CurrentId.Value ?? "????";
        Write(id, $"RESULT=FAIL [{CurrentContext.Value}] type={ex.GetType().Name} message={ex.Message}");
        if (ex.InnerException is not null)
            Write(id, $"FAIL-INNER type={ex.InnerException.GetType().Name} message={ex.InnerException.Message}");
        CurrentId.Value = null;
    }

    public static void Fail(string reason)
    {
        var id = CurrentId.Value ?? "????";
        Write(id, $"RESULT=FAIL [{CurrentContext.Value}] {reason}");
        CurrentId.Value = null;
    }

    static void Write(string id, string message) =>
        TraceLog.Append("shell-menu.log", $"[CLICK {id}] {message}");

    public sealed class Scope : IDisposable
    {
        readonly string _id;
        readonly Stopwatch _sw = Stopwatch.StartNew();
        bool _finished;

        internal Scope(string id) => _id = id;

        public void Dispose()
        {
            if (_finished)
                return;
            _finished = true;
            if (CurrentId.Value == _id)
            {
                Write(_id, $"RESULT=END (no explicit OK/FAIL) elapsedMs={_sw.ElapsedMilliseconds}");
                CurrentId.Value = null;
            }
        }

        public void MarkFinished() => _finished = true;
    }
}
