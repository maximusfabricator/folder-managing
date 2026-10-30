// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Core.Updates;

/// <summary>
/// When the tray app checks and what it shows. Every start checks a little later (sign-in stays fast), even when the
/// previous run already checked today: a release that came out afterwards would otherwise stay quiet until the next
/// day. While that same run keeps going, the next check is a day after the last one.
/// "Later" stores nothing: the window comes back at the next check. "Skip this version" stores the version.
/// </summary>
public static class UpdateSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    /// <summary><c>QUERY_USER_NOTIFICATION_STATE</c> values that mean "do not open a window now".</summary>
    public const int NotificationFullScreen = 3;
    public const int NotificationPresentation = 4;
    public const int NotificationQuietTime = 6;
    public const int NotificationImmersive = 7;

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
        var startup = startedUtc + StartupDelay;
        // A last check in the future means the clock was moved back; do not wait for it.
        // A last check from before this start belongs to the previous run: check again after the startup delay.
        if (lastCheckUtc is not { } last || last > nowUtc + TimeSpan.FromMinutes(5) || last < startedUtc)
            return startup;
        var due = last + Interval;
        return due > startup ? due : startup;
    }

    /// <summary>
    /// Whether an automatic check may open the window. A normal desktop, including "busy", may.
    /// Full-screen games, presentations, Focus assist and a full-screen Store app wait.
    /// </summary>
    public static bool AcceptsUpdateWindow(int notificationState) =>
        notificationState is not (NotificationFullScreen or NotificationPresentation or NotificationQuietTime or NotificationImmersive);

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
