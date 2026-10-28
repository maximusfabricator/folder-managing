// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Core.QuickAccess;

/// <summary>A pinned Quick access item, in pin order.</summary>
/// <param name="Path">File system path of the pinned folder.</param>
/// <param name="StoredAttributes">The folder attributes Windows saved when the folder was pinned (not the live ones).</param>
/// <param name="IsPlainFolder">
/// A folder on a local drive that we may unpin and pin again. Known folders (Desktop, Downloads…), libraries,
/// network locations and other non-file-system items are not plain and are never touched.
/// </param>
public sealed record QuickAccessPin(string Path, FileAttributes StoredAttributes, bool IsPlainFolder);

/// <summary>What the folder looks like on disk right now.</summary>
public sealed record FolderSnapshot(FileAttributes Attributes, bool HasDesktopIni);

/// <summary>The Quick access list of Windows, or a fake one in the self-tests.</summary>
public interface IQuickAccess
{
    /// <summary>Pinned items only, in pin order.</summary>
    IReadOnlyList<QuickAccessPin> GetPinned();

    /// <summary>Adds the folder at the end. Windows toggles: on a pinned folder this unpins it, so callers check first.</summary>
    void Pin(string path);

    void Unpin(string path);
}

/// <summary>The re-pin order, saved before the first unpin so a crash never loses a pin.</summary>
public interface IPinJournal
{
    IReadOnlyList<string>? Load();

    void Save(IReadOnlyList<string> order);

    void Clear();
}

public enum PinRefreshOutcome
{
    NothingToDo,
    Refreshed,
    Skipped,
    Failed,
}

public sealed record PinRefreshResult(PinRefreshOutcome Outcome, IReadOnlyList<string> Paths, string? Reason = null)
{
    public static readonly PinRefreshResult NothingToDo = new(PinRefreshOutcome.NothingToDo, []);
}

/// <summary>
/// A Quick access pin keeps the folder attributes from the moment it was pinned. A folder pinned before it was colored
/// lacks the read-only flag in that copy, so the navigation pane ignores its desktop.ini and shows a plain folder.
/// Only unpinning and pinning again updates the copy, and a new pin always goes to the end: so every pin from the first
/// stale one to the end is pinned again, in the same order.
/// </summary>
public static class PinRefresher
{
    /// <summary>Automatic runs re-pin at most this many items; each one makes every open Explorer window redraw.</summary>
    public const int AutomaticTailLimit = 8;

    const FileAttributes CustomizationBits = FileAttributes.ReadOnly | FileAttributes.System;

    /// <summary>
    /// A pin is stale when the folder gained the read-only or system flag since it was pinned, or lost it while it still
    /// has a desktop.ini (a restored foreign one). A reset that deleted desktop.ini looks the same both ways.
    /// </summary>
    public static bool IsStale(QuickAccessPin pin, FolderSnapshot live)
    {
        var gained = live.Attributes & ~pin.StoredAttributes & CustomizationBits;
        var lost = pin.StoredAttributes & ~live.Attributes & CustomizationBits;
        return gained != 0 || (lost != 0 && live.HasDesktopIni);
    }

    /// <summary>Index of the first pin of a managed folder that is stale, or -1.</summary>
    public static int FindFirstStale(
        IReadOnlyList<QuickAccessPin> pins, Func<string, FolderSnapshot?> live, Func<string, bool> isManaged)
    {
        for (var i = 0; i < pins.Count; i++)
        {
            var pin = pins[i];
            if (pin.IsPlainFolder && isManaged(pin.Path) && live(pin.Path) is { } snapshot && IsStale(pin, snapshot))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Finishes a journal a crash left behind, then re-pins from the first stale pin to the end.
    /// Nothing is unpinned unless every pin in that tail can be pinned again.
    /// </summary>
    public static PinRefreshResult Refresh(
        IQuickAccess quickAccess,
        IPinJournal journal,
        Func<string, FolderSnapshot?> live,
        Func<string, bool> isManaged,
        int maxTail)
    {
        if (Resume(quickAccess, journal) is { } resumeFailure)
            return resumeFailure;

        var pins = quickAccess.GetPinned();
        var start = FindFirstStale(pins, live, isManaged);
        if (start < 0)
            return PinRefreshResult.NothingToDo;

        var tail = pins.Skip(start).ToList();
        var order = tail.Select(p => p.Path).ToList();
        if (tail.Count > maxTail)
            return new PinRefreshResult(PinRefreshOutcome.Skipped, order, $"{tail.Count} pins would be pinned again (limit {maxTail}).");
        if (tail.FirstOrDefault(p => !p.IsPlainFolder || live(p.Path) is null) is { } blocker)
            return new PinRefreshResult(PinRefreshOutcome.Skipped, order, $"'{blocker.Path}' could not be pinned again.");

        journal.Save(order);
        Exception? unpinError = null;
        try
        {
            // From the end: whatever is still pinned is always the start of the original order.
            for (var i = order.Count - 1; i >= 0; i--)
                UnpinVerified(quickAccess, order[i]);
        }
        catch (Exception ex)
        {
            unpinError = ex;
        }

        try
        {
            RepinMissing(quickAccess, order);
        }
        catch (Exception ex)
        {
            return new PinRefreshResult(PinRefreshOutcome.Failed, order, ex.Message);
        }

        journal.Clear();
        return unpinError is null
            ? new PinRefreshResult(PinRefreshOutcome.Refreshed, order)
            : new PinRefreshResult(PinRefreshOutcome.Failed, order, unpinError.Message);
    }

    /// <summary>Pins what a journal lists and is missing, in its order. Returns a failure, or <c>null</c> when all is pinned.</summary>
    public static PinRefreshResult? Resume(IQuickAccess quickAccess, IPinJournal journal)
    {
        var leftover = journal.Load();
        if (leftover is not { Count: > 0 })
            return null;
        try
        {
            RepinMissing(quickAccess, leftover);
        }
        catch (Exception ex)
        {
            return new PinRefreshResult(PinRefreshOutcome.Failed, leftover, ex.Message);
        }

        journal.Clear();
        return null;
    }

    /// <summary>Pins every folder of <paramref name="order"/> that is not pinned, in that order.</summary>
    public static void RepinMissing(IQuickAccess quickAccess, IReadOnlyList<string> order)
    {
        foreach (var path in order)
        {
            if (IsPinned(quickAccess, path))
                continue;
            quickAccess.Pin(path);
            if (!IsPinned(quickAccess, path))
                throw new InvalidOperationException($"Windows did not pin '{path}' again.");
        }
    }

    public static bool IsPinned(IQuickAccess quickAccess, string path)
    {
        var wanted = NormalizePath(path);
        return quickAccess.GetPinned().Any(p => string.Equals(NormalizePath(p.Path), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Compares paths the way Windows does: no <c>\\?\</c> prefix, no trailing separator, any case.</summary>
    public static string NormalizePath(string path)
    {
        var p = path.Trim();
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
            p = p[4..];
        p = p.Replace('/', '\\');
        return p.Length > 3 ? p.TrimEnd('\\') : p;
    }

    static void UnpinVerified(IQuickAccess quickAccess, string path)
    {
        if (!IsPinned(quickAccess, path))
            return;
        quickAccess.Unpin(path);
        if (IsPinned(quickAccess, path))
            throw new InvalidOperationException($"Windows did not unpin '{path}'.");
    }
}
