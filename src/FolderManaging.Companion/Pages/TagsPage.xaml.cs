using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;
using DrawingColor = System.Drawing.Color;

namespace FolderManaging.Companion.Pages;

public partial class TagsPage : System.Windows.Controls.UserControl
{
    ObservableCollection<MenuTagPreset>? _tags;

    public TagsPage()
    {
        InitializeComponent();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        var doc = CompanionState.Document;
        _tags = new ObservableCollection<MenuTagPreset>(doc.MenuTags);
        TagItems.ItemsSource = _tags;
        ClickTrace.Step($"TagsPage loaded count={_tags.Count}");
    }

    void OnTagLabelChanged(object sender, TextChangedEventArgs e) => CompanionState.Save();

    void OnTagLabelLostFocus(object sender, RoutedEventArgs e)
    {
        CompanionState.Save();
        FolderIconManager.RefreshAllFolderTags();
    }

    void OnAddTagClick(object sender, RoutedEventArgs e) => TryAddTag();

    void OnNewTagKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            TryAddTag();
    }

    void OnEditTagColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is MenuTagPreset preset)
            EditTagColor(preset);
    }

    void EditTagColor(MenuTagPreset preset)
    {
        using var scope = ClickTrace.Begin("companion", "edit-tag-color", detail: preset.Id);
        var start = MenuColorHex.TryParse(preset.HexRgb, out var parsed)
            ? parsed
            : MenuTagPresetDefaults.Color;

        using var dlg = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = start,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            ClickTrace.Ok("cancelled");
            scope.MarkFinished();
            return;
        }

        var c = DrawingColor.FromArgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
        preset.HexRgb = MenuColorHex.ToHexRgb(c);
        CompanionState.Save();
        TagItems.Items.Refresh();
        FolderIconManager.RefreshAllFolderTags();
        ClickTrace.Ok($"id={preset.Id} hex={preset.HexRgb}");
        scope.MarkFinished();
    }

    void TryAddTag()
    {
        using var scope = ClickTrace.Begin("companion", "add-tag");
        var doc = CompanionState.Document;
        var label = NewTagName.Text.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            ClickTrace.Fail("empty label");
            scope.MarkFinished();
            return;
        }

        var baseId = Slug(label);
        if (string.IsNullOrEmpty(baseId))
            baseId = "tag";

        var id = baseId;
        var n = 1;
        while (doc.MenuTags.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ||
               _tags!.Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{baseId}-{n++}";
        }

        var row = new MenuTagPreset { Id = id, Label = label, HexRgb = MenuTagPresetDefaults.HexRgb };
        doc.MenuTags.Add(row);
        _tags!.Add(row);
        NewTagName.Clear();
        CompanionState.Save();
        ClickTrace.Ok($"id={id} label={label}");
        scope.MarkFinished();
    }

    static string Slug(string label)
    {
        var s = label.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"[^a-z0-9\-]", "");
        s = Regex.Replace(s, "-{2,}", "-").Trim('-');
        return s;
    }

    void OnDeleteTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not MenuTagPreset preset)
            return;

        using var scope = ClickTrace.Begin("companion", "delete-tag", detail: preset.Id);
        var doc = CompanionState.Document;
        FolderStateTagSync.RemoveTagIdFromAllFolders(doc, preset.Id);
        doc.MenuTags.RemoveAll(t => ReferenceEquals(t, preset) ||
                                      string.Equals(t.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
        _tags!.Remove(preset);
        CompanionState.Save();
        FolderIconManager.RefreshAllFolderTags();
        ClickTrace.Ok($"deleted id={preset.Id}");
        scope.MarkFinished();
    }
}
