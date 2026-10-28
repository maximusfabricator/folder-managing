// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.QuickAccess;

namespace FolderManaging.Companion;

/// <summary>
/// After the app colors or resets folders: re-pins Quick access entries that still show the old picture.
/// Waits a moment so a batch of changes leads to one run.
/// </summary>
internal static class PinRefreshScheduler
{
    static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);
    static readonly object Gate = new();
    static System.Threading.Timer? _timer;

    internal static void Request()
    {
        lock (Gate)
        {
            _timer ??= new System.Threading.Timer(_ => Run());
            _timer.Change(Delay, Timeout.InfiniteTimeSpan);
        }
    }

    static void Run()
    {
        try
        {
            var result = QuickAccessService.RefreshStalePins(automatic: true);
            if (result.Outcome == PinRefreshOutcome.Refreshed)
                ClickTrace.Step($"Quick access re-pinned: {string.Join(" | ", result.Paths)}");
        }
        catch (Exception ex)
        {
            TraceLog.Append("pins.log", $"scheduled refresh failed: {ex.Message}");
        }
    }
}
