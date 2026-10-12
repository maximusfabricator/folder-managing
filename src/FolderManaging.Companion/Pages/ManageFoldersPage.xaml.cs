using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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

    static string FormatColor(string? token, AppStateDocument doc)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "—";

        var preset = doc.MenuColors.FirstOrDefault(c =>
            string.Equals(c.Id, token, StringComparison.OrdinalIgnoreCase));
        return preset is null ? token : $"{preset.Label} ({preset.Id})";
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
            ClickTrace.Step($"PinManager.Unpin failed (continuing state clear): {ex.Message}");
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

    void OnClearTagsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { CommandParameter: ManagedFolderRow row })
            return;

        using var scope = ClickTrace.Begin("companion", "clear-tags", path: row.Entry.Path);
        var doc = CompanionState.Document;
        row.Entry.Tags.Clear();
        PruneIfEmpty(doc, row.Entry);
        CompanionState.Save();
        FolderIconManager.RequestExplorerRefreshForFolder(row.Entry.Path);
        ClickTrace.Ok();
        scope.MarkFinished();
        Reload(forceDisk: false);
    }

    void OnRemoveColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { CommandParameter: ManagedFolderRow row })
            return;

        using var scope = ClickTrace.Begin("companion", "remove-color", path: row.Entry.Path);
        if (!FolderIconManager.TryRemoveCustomization(row.Entry.Path, out var warn) && !string.IsNullOrEmpty(warn))
            ClickTrace.Step($"TryRemoveCustomization warn: {warn}");

        var doc = CompanionState.Document;
        row.Entry.ColorToken = null;
        PruneIfEmpty(doc, row.Entry);
        CompanionState.Save();
        FolderIconManager.RequestExplorerRefreshForFolder(row.Entry.Path);
        ClickTrace.Ok();
        scope.MarkFinished();
        Reload(forceDisk: false);
    }
}
