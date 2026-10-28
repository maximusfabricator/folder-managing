// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FolderManaging.Companion.Localization;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
namespace FolderManaging.Companion.Pages;

public sealed class ManagedFolderRow
{
    public ManagedFolderRow(FolderStateEntry entry, AppStateDocument document)
    {
        Entry = entry;
        Document = document;
    }

    public FolderStateEntry Entry { get; }

    public AppStateDocument Document { get; }

    public string PathDisplay => Entry.Path;

    public string ColorDisplay => FormatColor(Entry.ColorToken, Document);

    public string TagsDisplay => FormatTags(Entry.Tags, Document);

    public string PinnedDisplay => Entry.IsPinned ? UiText.Get("Yes") : UiText.Get("No");

    public bool IsPinned => Entry.IsPinned;

    public bool HasTags => Entry.Tags is { Count: > 0 };

    public bool HasColor => !string.IsNullOrWhiteSpace(Entry.ColorToken);

    static string FormatColor(string? token, AppStateDocument doc)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "—";

        if (token.StartsWith("rgb-", StringComparison.OrdinalIgnoreCase))
            return "#" + token["rgb-".Length..].ToUpperInvariant();

        var preset = doc.MenuColors.FirstOrDefault(c =>
            string.Equals(c.Id, token, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
            return token;
        return string.IsNullOrWhiteSpace(preset.Label) ? preset.Id : $"{preset.Label} ({preset.Id})";
    }

    static string FormatTags(IReadOnlyList<string> tags, AppStateDocument doc)
    {
        if (tags is null || tags.Count == 0)
            return "—";

        var parts = new List<string>();
        foreach (var id in tags)
        {
            var preset = doc.MenuTags.FirstOrDefault(t =>
                string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            parts.Add(preset is null ? id : $"{preset.Label}");
        }

        return string.Join(", ", parts);
    }
}

public partial class ManageFoldersPage : System.Windows.Controls.UserControl
{
    ObservableCollection<ManagedFolderRow>? _rows;
    bool _listening;

    public ManageFoldersPage()
    {
        InitializeComponent();
        UiDirection.Apply(this);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                Reload(forceDisk: true);
        };
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_listening)
            return;
        CompanionState.DocumentChanged += OnDocumentChanged;
        _listening = true;
        Reload(forceDisk: true);
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_listening)
            return;
        CompanionState.DocumentChanged -= OnDocumentChanged;
        _listening = false;
    }

    void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (IsVisible)
            Reload(forceDisk: false);
    }

    void Reload(bool forceDisk)
    {
        if (forceDisk)
            CompanionState.ReloadFromDiskIfStale();

        var doc = CompanionState.Document;
        // A renamed folder is followed, not dropped.
        var moved = FolderRelocator.FindMoved(doc);
        if (moved.Count > 0)
            App.SyncMovedInBackground(moved.Select(m => m.To).ToList());
        if (RemoveMissingFolders(doc) || moved.Count > 0)
            CompanionState.Save();

        var list = doc.Folders
            .Where(f =>
                f.IsPinned ||
                (f.Tags is not null && f.Tags.Count > 0) ||
                !string.IsNullOrWhiteSpace(f.ColorToken))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new ManagedFolderRow(f, doc))
            .ToList();

        _rows = new ObservableCollection<ManagedFolderRow>(list);
        FoldersGrid.ItemsSource = _rows;
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FoldersGrid.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ResetAllButton.IsEnabled = list.Any(r => r.HasColor || r.HasTags);
        UpdateSelectionButtons();
        Dispatcher.BeginInvoke(FitActions, DispatcherPriority.Loaded);
    }

    /// <summary>A path column narrower than this is unreadable, so the row buttons give up their text first.</summary>
    const double PathMinWidth = 180;

    /// <summary>A path that needs more than this is trimmed rather than taking the text away from the row buttons.</summary>
    const double PathMaxWidth = 400;

    double _actionsWidthWithText;

    void OnGridSizeChanged(object sender, SizeChangedEventArgs e) => FitActions();

    void FitActions()
    {
        if (!FoldersGrid.IsVisible || FoldersGrid.ActualWidth < 1)
            return;

        var compact = Equals(FoldersGrid.Tag, "compact");
        if (!compact)
            _actionsWidthWithText = ActionsColumn.ActualWidth;
        var otherColumns = FoldersGrid.Columns.Where(c => c != PathColumn && c != ActionsColumn).Sum(c => c.ActualWidth);
        var pathWanted = Math.Clamp(PathWidthNeeded(), PathMinWidth, PathMaxWidth);
        var wantCompact = FoldersGrid.ActualWidth - otherColumns - _actionsWidthWithText < pathWanted;
        if (wantCompact == compact)
            return;

        FoldersGrid.Tag = wantCompact ? "compact" : null;
        // An Auto column only grows by itself, and the star column keeps what it took meanwhile: reset the first
        // now and the second once the buttons have their new size.
        ActionsColumn.Width = new DataGridLength(0);
        ActionsColumn.Width = DataGridLength.Auto;
        Dispatcher.BeginInvoke(() =>
        {
            PathColumn.Width = new DataGridLength(0);
            PathColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            FitActions();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The width the path column needs to show the longest path in full, with the cell padding and a pixel for rounding.
    /// Only styles and texts go in: right after a column reset the widths on screen are not up to date yet.
    /// </summary>
    double PathWidthNeeded()
    {
        if (_rows is null || _rows.Select(row => PathColumn.GetCellContent(row)).OfType<TextBlock>().FirstOrDefault()
                is not { Parent: System.Windows.Controls.DataGridCell cell } text)
            return 0;

        var padding = cell.Padding.Left + cell.Padding.Right + text.Margin.Left + text.Margin.Right + 1;
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(text).PixelsPerDip;
        return padding + _rows.Max(row => new FormattedText(row.PathDisplay, CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight, typeface, text.FontSize, System.Windows.Media.Brushes.Black, pixelsPerDip)
            .WidthIncludingTrailingWhitespace);
    }

    /// <summary>
    /// Drops rows whose folder is gone. A missing drive (unplugged disk) is kept.
    /// Paths from another PC disappear here because those folders do not exist.
    /// </summary>
    static bool RemoveMissingFolders(AppStateDocument doc)
    {
        var removed = doc.Folders.RemoveAll(entry => IsDeletedFolder(entry.Path));
        return removed > 0;
    }

    static bool IsDeletedFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return false;
            return !Directory.Exists(full);
        }
        catch
        {
            return false;
        }
    }

    static void PruneIfEmpty(AppStateDocument doc, FolderStateEntry entry)
    {
        if (entry.IsPinned)
            return;
        if (entry.Tags is { Count: > 0 })
            return;
        if (!string.IsNullOrWhiteSpace(entry.ColorToken))
            return;

        doc.Folders.Remove(entry);
    }

    void OnUnpinClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { CommandParameter: ManagedFolderRow row })
            return;

        using var scope = ClickTrace.Begin("companion", "unpin", path: row.Entry.Path);
        try
        {
            PinManager.UnpinFromQuickAccess(row.Entry.Path);
            ClickTrace.Step("PinManager.Unpin OK");
        }
        catch (Exception ex)
        {
            // Windows kept the pin: the list must keep saying so.
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            FmMessageBox.Show(
                Window.GetWindow(this),
                UiText.Format("ChangeFolderFail", UiText.LeftToRight(row.Entry.Path), ex.Message),
                "Folder Managing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var doc = CompanionState.Document;
        row.Entry.IsPinned = false;
        PruneIfEmpty(doc, row.Entry);
        CompanionState.Save();
        FolderIconManager.RequestExplorerRefreshForFolder(row.Entry.Path);
        ClickTrace.Ok();
        scope.MarkFinished();
        Reload(forceDisk: false);
    }

    async void OnClearTagsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { CommandParameter: ManagedFolderRow row })
            return;

        using var scope = ClickTrace.Begin("companion", "clear-tags", path: row.Entry.Path);
        var previous = row.Entry.Tags.ToList();
        row.Entry.Tags.Clear();
        await SaveAndWriteFolderAsync(row.Entry, undo: () => row.Entry.Tags.AddRange(previous));
        scope.MarkFinished();
        Reload(forceDisk: false);
    }

    async void OnRemoveColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { CommandParameter: ManagedFolderRow row })
            return;

        using var scope = ClickTrace.Begin("companion", "remove-color", path: row.Entry.Path);
        var previous = row.Entry.ColorToken;
        row.Entry.ColorToken = null;
        await SaveAndWriteFolderAsync(row.Entry, undo: () => row.Entry.ColorToken = previous);
        scope.MarkFinished();
        Reload(forceDisk: false);
    }

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionButtons();

    void UpdateSelectionButtons() =>
        ResetSelectedButton.IsEnabled = SelectedRows().Any(r => r.HasColor || r.HasTags);

    List<ManagedFolderRow> SelectedRows() => FoldersGrid.SelectedItems.OfType<ManagedFolderRow>().ToList();

    async void OnResetSelectedClick(object sender, RoutedEventArgs e) =>
        await ResetAsync(SelectedRows(), "reset-selected");

    async void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        var rows = _rows?.ToList() ?? [];
        var count = rows.Count(r => r.HasColor || r.HasTags);
        if (count == 0)
            return;
        var answer = FmMessageBox.Show(
            Window.GetWindow(this),
            UiText.Format("ResetAllConfirm", count),
            "Folder Managing",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;
        await ResetAsync(rows, "reset-all");
    }

    /// <summary>Color and tags off for these rows; protected folders together with one UAC prompt. Pins stay.</summary>
    async Task ResetAsync(IReadOnlyList<ManagedFolderRow> rows, string action)
    {
        var paths = rows.Where(r => r.HasColor || r.HasTags).Select(r => r.Entry.Path).ToList();
        if (paths.Count == 0)
            return;
        using var scope = ClickTrace.Begin("companion", action, detail: $"x{paths.Count}");
        IsEnabled = false;
        try
        {
            var result = await FolderWriteGuard.RunAsync(Window.GetWindow(this),
                paths.Select(p => new FolderAction(FolderActionKind.Reset, p)).ToList());
            if (result.AllDone)
                ClickTrace.Ok($"x{result.Done.Count}");
            else
                ClickTrace.Fail($"{result.Failed.Count} not reset");
            scope.MarkFinished();
            FolderWriteGuard.Report(Window.GetWindow(this), result);
        }
        finally
        {
            IsEnabled = true;
            Reload(forceDisk: true);
        }
    }

    /// <summary>
    /// Saves the edit, then writes the folder's <c>desktop.ini</c>. If Windows refuses (and admin rights are declined),
    /// the edit is undone so the list and the folder never disagree.
    /// </summary>
    async Task SaveAndWriteFolderAsync(FolderStateEntry entry, Action undo)
    {
        CompanionState.Save();
        var error = await FolderWriteGuard.SyncAsync(Window.GetWindow(this), entry.Path);
        if (error is null)
        {
            PruneIfEmpty(CompanionState.Document, entry);
            CompanionState.Save();
            ClickTrace.Ok();
            return;
        }

        undo();
        CompanionState.Save();
        ClickTrace.Fail(error);
        FmMessageBox.Show(
            Window.GetWindow(this),
            UiText.Format("ChangeFolderFail", UiText.LeftToRight(entry.Path), error),
            "Folder Managing",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
