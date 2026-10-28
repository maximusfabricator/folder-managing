// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

public enum FolderActionKind
{
    Color,
    Rgb,
    Reset,
    Tag,
    Untag,
    Sync,
}

/// <summary>One change to one folder. <see cref="Value"/> is the color id, the hex RGB or the tag id.</summary>
public sealed record FolderAction(FolderActionKind Kind, string Path, string? Value = null)
{
    /// <summary>Name used by the elevated worker and the CLI.</summary>
    public string Verb => Kind.ToString().ToLowerInvariant();

    public static bool TryParseKind(string verb, out FolderActionKind kind) =>
        Enum.TryParse(verb, ignoreCase: true, out kind) && Enum.IsDefined(kind);
}

public sealed record FolderActionFailure(string Path, string Message);

public sealed record FolderBatchResult(
    IReadOnlyList<string> Done,
    IReadOnlyList<FolderActionFailure> Failed,
    bool ElevationDeclined)
{
    public bool AllDone => Failed.Count == 0 && !ElevationDeclined;
}

/// <summary>
/// The same change for many folders (several folders selected in Explorer or rows in the app). Folders Windows protects
/// (Program Files, Windows) are collected and done together through the elevated worker: one UAC prompt for all.
/// </summary>
public static class FolderBatch
{
    /// <summary>Runs one action in this process: first the folder, then <c>state.json</c>.</summary>
    /// <exception cref="UnauthorizedAccessException">The folder needs admin rights; nothing was changed.</exception>
    public static void Execute(FolderAction action)
    {
        var editor = new FolderStateEditor(new JsonFolderStateStore());
        switch (action.Kind)
        {
            case FolderActionKind.Color:
            {
                var colorId = Required(action);
                FolderIconManager.Apply(action.Path, colorId);
                editor.SetColorTokenAsync(action.Path, colorId).GetAwaiter().GetResult();
                break;
            }
            case FolderActionKind.Rgb:
            {
                if (!MenuColorHex.TryParse(Required(action), out var tint))
                    throw new ArgumentException($"Bad color '{action.Value}'.");
                var hex = MenuColorHex.ToHexRgb(tint).ToLowerInvariant();
                FolderIconManager.ApplyRgb(action.Path, tint);
                editor.SetColorTokenAsync(action.Path, "rgb-" + hex).GetAwaiter().GetResult();
                break;
            }
            case FolderActionKind.Reset:
                FolderIconManager.RemoveCustomizationIfOurs(action.Path);
                editor.ResetFolderAsync(action.Path).GetAwaiter().GetResult();
                break;
            case FolderActionKind.Tag:
                // Fail before state.json changes, so the elevated worker does the whole action.
                FolderIconManager.EnsureFolderWritable(action.Path);
                editor.UpsertTagAsync(action.Path, Required(action)).GetAwaiter().GetResult();
                FolderIconManager.SyncFolderTags(action.Path);
                break;
            case FolderActionKind.Untag:
                FolderIconManager.EnsureFolderWritable(action.Path);
                editor.RemoveTagAsync(action.Path, Required(action)).GetAwaiter().GetResult();
                FolderIconManager.SyncFolderTags(action.Path);
                break;
            case FolderActionKind.Sync:
                FolderIconManager.SyncFolderTags(action.Path, force: action.Value == "force");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <param name="confirmElevation">
    /// Asked once with the protected folders; <c>true</c> lets the elevated worker do them (it may show UAC).
    /// </param>
    public static FolderBatchResult Run(IReadOnlyList<FolderAction> actions, Func<IReadOnlyList<string>, bool> confirmElevation) =>
        Run(actions, confirmElevation, RealHost.Instance);

    /// <summary>Where the work happens; self-tests pass a fake.</summary>
    public interface IHost
    {
        void Execute(FolderAction action);

        bool StartElevated(out string error, out bool declined);

        bool ExecuteElevated(FolderAction action, out string error);
    }

    sealed class RealHost : IHost
    {
        public static readonly RealHost Instance = new();

        public void Execute(FolderAction action) => FolderBatch.Execute(action);

        public bool StartElevated(out string error, out bool declined)
        {
            var ok = ElevatedCliInvoker.EnsureWorkerStarted(out error);
            declined = !ok && string.Equals(error, ElevatedCliInvoker.UacCancelledMessage, StringComparison.Ordinal);
            return ok;
        }

        public bool ExecuteElevated(FolderAction action, out string error) =>
            ElevatedCliInvoker.TryInvoke(action.Verb, action.Path, action.Value, out error, allowStartWorker: false);
    }

    public static FolderBatchResult Run(IReadOnlyList<FolderAction> actions, Func<IReadOnlyList<string>, bool> confirmElevation, IHost host)
    {
        var done = new List<string>();
        var failed = new List<FolderActionFailure>();
        var denied = new List<FolderAction>();
        foreach (var action in actions)
        {
            try
            {
                host.Execute(action);
                done.Add(action.Path);
            }
            catch (Exception ex) when (IsAccessDenied(ex))
            {
                denied.Add(action);
            }
            catch (Exception ex)
            {
                TraceLog.Append("shell-menu.log", $"[BATCH] {action.Verb} failed '{action.Path}': {ex.Message}");
                failed.Add(new FolderActionFailure(action.Path, ex.Message));
            }
        }

        if (denied.Count == 0)
            return new FolderBatchResult(done, failed, ElevationDeclined: false);

        if (!confirmElevation(denied.Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
        {
            failed.AddRange(denied.Select(a => new FolderActionFailure(a.Path, "")));
            return new FolderBatchResult(done, failed, ElevationDeclined: true);
        }

        if (!host.StartElevated(out var startError, out var declined))
        {
            failed.AddRange(denied.Select(a => new FolderActionFailure(a.Path, declined ? "" : startError)));
            return new FolderBatchResult(done, failed, declined);
        }

        foreach (var action in denied)
        {
            if (host.ExecuteElevated(action, out var error))
                done.Add(action.Path);
            else
                failed.Add(new FolderActionFailure(action.Path, error));
        }

        return new FolderBatchResult(done, failed, ElevationDeclined: false);
    }

    public static bool IsAccessDenied(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException)
                return true;
        }

        return false;
    }

    static string Required(FolderAction action) =>
        string.IsNullOrWhiteSpace(action.Value) ? throw new ArgumentException($"{action.Verb} needs a value.") : action.Value;
}
