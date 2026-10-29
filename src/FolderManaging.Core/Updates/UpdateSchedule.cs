// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Core.Updates;

/// <summary>
/// When the tray app checks and what it shows. Checks run a little after start (sign-in stays fast) and then once a
/// day, counted from the last check saved in <c>state.json</c>, so restarting the app does not check again.
/// "Later" stores nothing: the window comes back with the next daily check. "Skip this version" stores the version.
/// </summary>
public static class UpdateSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(90);

    /// <summary>Compile-time switch: an edition updated by a store has no update checks.</summary>
    public static bool IsSupported =>
#if FM_NO_UPDATER
        false;
#else
        true;
#endif

    /// <summary>When the next automatic check is due.</summary>
    /// <param name="lastCheckUtc">Last check (any result), or <c>null</c>.</param>
    /// <param name="startedUtc">When the tray app started.</param>
    public static DateTime NextCheckUtc(DateTime? lastCheckUtc, DateTime startedUtc, DateTime nowUtc)
    {
        var earliest = startedUtc + StartupDelay;
        // A last check in the future means the clock was moved back; do not wait for it.
        if (lastCheckUtc is not { } last || last > nowUtc + TimeSpan.FromMinutes(5))
            return earliest;
        var due = last + Interval;
        return due > earliest ? due : earliest;
    }

    /// <summary>Whether the window offers <paramref name="latest"/>. A manual check also offers a skipped version.</summary>
    public static bool ShouldOffer(UpdateVersion latest, UpdateVersion current, string? skippedVersion, bool manual)
    {
        if (latest <= current)
            return false;
        if (manual)
            return true;
        return !(UpdateVersion.TryParse(skippedVersion, out var skipped) && skipped == latest);
    }
}
