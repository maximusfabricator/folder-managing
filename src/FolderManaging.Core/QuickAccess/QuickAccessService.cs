// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.ExceptionServices;
using System.Text.Json;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core.QuickAccess;

/// <summary>Journal file next to <c>state.json</c>, written atomically.</summary>
public sealed class FilePinJournal(string path) : IPinJournal
{
    public static FilePinJournal Default => new(Path.Combine(AppPaths.DataDirectory, "pin-refresh.json"));

    public IReadOnlyList<string>? Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            TraceLog.Append("pins.log", $"journal unreadable: {ex.Message}");
            return null;
        }
    }

    public void Save(IReadOnlyList<string> order)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(order));
        File.Move(temp, path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

/// <summary>
/// Quick access for the app: pin and unpin that check the real state first (Windows toggles), and the re-pin of stale
/// pins. Runs the shell calls on their own STA thread and never two re-pins at once (also across processes).
/// Logs name pinned folders only, never recent items.
/// </summary>
public static class QuickAccessService
{
    const string MutexName = @"Local\FolderManaging.PinRefresh";

    public static bool IsPinned(string folderPath) =>
        RunSta(() => PinRefresher.IsPinned(new ShellQuickAccess(), folderPath));

    /// <summary>Pinned folder paths in pin order (empty when Quick access cannot be read).</summary>
    public static IReadOnlyList<QuickAccessPin> GetPinned()
    {
        try
        {
            return RunSta(() => new ShellQuickAccess().GetPinned());
        }
        catch (Exception ex)
        {
            TraceLog.Append("pins.log", $"read failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>Pins the folder unless it already is. Throws when Windows did not pin it.</summary>
    public static void Pin(string folderPath) => RunSta(() =>
    {
        var quickAccess = new ShellQuickAccess();
        if (PinRefresher.IsPinned(quickAccess, folderPath))
            return true;
        quickAccess.Pin(folderPath);
        if (!PinRefresher.IsPinned(quickAccess, folderPath))
            throw new InvalidOperationException($"Windows did not pin '{folderPath}'.");
        TraceLog.Append("pins.log", $"pinned '{folderPath}'");
        return true;
    });

    /// <summary>Unpins the folder if it is pinned. Throws when Windows kept it.</summary>
    public static void Unpin(string folderPath) => RunSta(() =>
    {
        var quickAccess = new ShellQuickAccess();
        if (!PinRefresher.IsPinned(quickAccess, folderPath))
            return true;
        quickAccess.Unpin(folderPath);
        if (PinRefresher.IsPinned(quickAccess, folderPath))
            throw new InvalidOperationException($"Windows did not unpin '{folderPath}'.");
        TraceLog.Append("pins.log", $"unpinned '{folderPath}'");
        return true;
    });

    /// <summary>
    /// Re-pins stale pins of colored or tagged folders. <paramref name="automatic"/> limits the run to
    /// <see cref="PinRefresher.AutomaticTailLimit"/> pins; the Settings button has no limit.
    /// </summary>
    public static PinRefreshResult RefreshStalePins(bool automatic)
    {
#if FM_DEV
        // Runs on a copy of the state (UI tours, experiments): the real Quick access belongs to the installed app.
        if (Environment.GetEnvironmentVariable("FM_DATA_DIR") is { Length: > 0 })
            return new PinRefreshResult(PinRefreshOutcome.Skipped, [], "FM_DATA_DIR is set.");
#endif
        using var mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(automatic ? TimeSpan.Zero : TimeSpan.FromSeconds(30));
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned)
            return new PinRefreshResult(PinRefreshOutcome.Skipped, [], "Another re-pin is running.");

        try
        {
            var managed = ManagedFolders();
            var result = RunSta(() => PinRefresher.Refresh(
                new ShellQuickAccess(),
                FilePinJournal.Default,
                ReadSnapshot,
                path => managed.Contains(PinRefresher.NormalizePath(path)),
                automatic ? PinRefresher.AutomaticTailLimit : int.MaxValue));
            if (result.Outcome != PinRefreshOutcome.NothingToDo)
                TraceLog.Append("pins.log", $"refresh {result.Outcome}: {string.Join(" | ", result.Paths)} {result.Reason}");
            return result;
        }
        catch (Exception ex)
        {
            TraceLog.Append("pins.log", $"refresh failed: {ex.Message}");
            return new PinRefreshResult(PinRefreshOutcome.Failed, [], ex.Message);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>True when the folder is pinned and its pin is stale (the navigation pane shows a plain folder).</summary>
    public static bool HasStalePin(string folderPath)
    {
        var wanted = PinRefresher.NormalizePath(folderPath);
        var pin = GetPinned().FirstOrDefault(p =>
            string.Equals(PinRefresher.NormalizePath(p.Path), wanted, StringComparison.OrdinalIgnoreCase));
        return pin is { IsPlainFolder: true } && ReadSnapshot(pin.Path) is { } live && PinRefresher.IsStale(pin, live);
    }

    /// <summary>Makes <c>isPinned</c> in <c>state.json</c> match Quick access for the folders already listed there.</summary>
    /// <returns><c>true</c> when the document changed.</returns>
    public static bool SyncPinnedFlags(AppStateDocument doc) => SyncPinnedFlags(doc, GetPinned());

    /// <param name="pins">From <see cref="GetPinned"/>; empty (unreadable) changes nothing.</param>
    public static bool SyncPinnedFlags(AppStateDocument doc, IReadOnlyList<QuickAccessPin> pins)
    {
        if (pins.Count == 0)
            return false;
        var pinned = pins.Select(p => PinRefresher.NormalizePath(p.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var entry in doc.Folders)
        {
            var isPinned = pinned.Contains(PinRefresher.NormalizePath(entry.Path));
            if (entry.IsPinned == isPinned)
                continue;
            entry.IsPinned = isPinned;
            changed = true;
        }

        return changed;
    }

    internal static FolderSnapshot? ReadSnapshot(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.Directory))
                return null;
            return new FolderSnapshot(attributes, File.Exists(Path.Combine(path, "desktop.ini")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    static HashSet<string> ManagedFolders()
    {
        var doc = JsonFolderStateStore.LoadSynchronously();
        return doc.Folders
            .Where(f => !string.IsNullOrWhiteSpace(f.ColorToken) || f.Tags.Count > 0)
            .Select(f => PinRefresher.NormalizePath(f.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    static T RunSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        })
        {
            IsBackground = true,
            Name = "Quick access",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
            ExceptionDispatchInfo.Capture(caught).Throw();
        return result;
    }
}
