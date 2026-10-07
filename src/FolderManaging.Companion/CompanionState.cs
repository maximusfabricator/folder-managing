using System.IO;
using System.Windows.Threading;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Companion;

internal static class CompanionState
{
    static AppStateDocument? _document;
    static long _loadedTicks = long.MinValue;
    static FileSystemWatcher? _watcher;
    static DispatcherTimer? _reloadDebounce;
    static int _saving;
    static bool _watchStarted;

    /// <summary>Raised on the UI thread after <see cref="Document"/> is replaced from disk (external edit).</summary>
    internal static event EventHandler? DocumentChanged;

    internal static AppStateDocument Document
    {
        get
        {
            EnsureWatching();
            return _document ??= LoadFresh();
        }
    }

    /// <summary>Re-reads <c>state.json</c> when the file is newer than the in-memory copy.</summary>
    internal static bool ReloadFromDiskIfStale()
    {
        EnsureWatching();
        var ticks = ReadStateTicks();
        if (_document is not null && ticks == _loadedTicks)
            return false;

        _document = LoadFresh(ticks);
        MenuStateReader.Invalidate();
        ClickTrace.Step($"CompanionState reloaded from disk ticks={ticks}");
        DocumentChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    internal static void Save()
    {
        Interlocked.Increment(ref _saving);
        try
        {
            MenuPresetNormalizer.Merge(Document);
            JsonFolderStateStore.SaveSynchronously(Document);
            _loadedTicks = ReadStateTicks();
            MenuStateReader.Invalidate();
            ClickTrace.Step("CompanionState.Save → state.json written + MenuStateReader invalidated");
        }
        finally
        {
            Interlocked.Decrement(ref _saving);
        }
    }

    internal static void Shutdown()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnStateFileChanged;
            _watcher.Created -= OnStateFileChanged;
            _watcher.Renamed -= OnStateFileRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        _reloadDebounce?.Stop();
        _reloadDebounce = null;
        _watchStarted = false;
    }

    static AppStateDocument LoadFresh(long? ticks = null)
    {
        var doc = JsonFolderStateStore.LoadSynchronously();
        _loadedTicks = ticks ?? ReadStateTicks();
        return doc;
    }

    static long ReadStateTicks()
    {
        var path = AppPaths.StateFilePath;
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L;
        }
        catch
        {
            return 0L;
        }
    }

    static void EnsureWatching()
    {
        if (_watchStarted)
            return;
        _watchStarted = true;

        try
        {
            var dir = AppPaths.DataDirectory;
            var name = Path.GetFileName(AppPaths.StateFilePath);
            _watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };
            _watcher.Changed += OnStateFileChanged;
            _watcher.Created += OnStateFileChanged;
            _watcher.Renamed += OnStateFileRenamed;
            _watcher.EnableRaisingEvents = true;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null)
            {
                _reloadDebounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(250),
                };
                _reloadDebounce.Tick += (_, _) =>
                {
                    _reloadDebounce.Stop();
                    if (Volatile.Read(ref _saving) > 0)
                        return;
                    try
                    {
                        ReloadFromDiskIfStale();
                    }
                    catch (Exception ex)
                    {
                        ClickTrace.Step($"CompanionState reload failed: {ex.Message}");
                    }
                };
            }

            ClickTrace.Step($"CompanionState watching '{AppPaths.StateFilePath}'");
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"CompanionState watch failed: {ex.Message}");
            _watchStarted = false;
        }
    }

    static void OnStateFileRenamed(object sender, RenamedEventArgs e) =>
        ScheduleReload();

    static void OnStateFileChanged(object sender, FileSystemEventArgs e) =>
        ScheduleReload();

    static void ScheduleReload()
    {
        if (Volatile.Read(ref _saving) > 0)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        if (dispatcher.CheckAccess())
        {
            _reloadDebounce?.Stop();
            _reloadDebounce?.Start();
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            _reloadDebounce?.Stop();
            _reloadDebounce?.Start();
        });
    }
}
