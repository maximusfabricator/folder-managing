// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Windows;
using System.Windows.Controls;
using FolderManaging.Companion.Localization;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = Wpf.Ui.Controls.TextBox;

namespace FolderManaging.Companion.Pages;

public partial class ContextMenuPage : System.Windows.Controls.UserControl
{
    ContextMenuLayout _layout = ContextMenuLayout.CreateDefault();
    bool _loading;

    public ContextMenuPage()
    {
        InitializeComponent();
        UiDirection.Apply(this);
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _layout = CompanionState.Document.GetContextMenuLayout().Clone();
        _loading = true;
        try
        {
            RootTitleBox.Text = _layout.RootTitle;
        }
        finally
        {
            _loading = false;
        }

        RebuildEntries();
        RefreshPreview();
    }

    void SaveLayout(string what)
    {
        CompanionState.Document.ContextMenu = _layout.Clone();
        CompanionState.Save();
        ClickTrace.Step($"context-menu layout saved ({what})");
        RefreshPreview();
    }

    void CommitLayout(string what)
    {
        CompanionState.Document.ContextMenu = ContextMenuLayout.Normalize(_layout);
        CompanionState.Save();
        _layout = CompanionState.Document.GetContextMenuLayout().Clone();
        ClickTrace.Step($"context-menu layout committed ({what})");
        RebuildEntries();
        RefreshPreview();
    }

    void OnRootTitleChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;
        _layout.RootTitle = string.IsNullOrWhiteSpace(RootTitleBox.Text) ? ContextMenuLayout.DefaultRootTitle : RootTitleBox.Text.Trim();
        SaveLayout("root title");
    }

    void OnAddSeparatorClick(object sender, RoutedEventArgs e)
    {
        var sep = new ContextMenuEntry { Kind = ContextMenuItemKinds.Separator };

        // Prefer appending. If the list already ends with a separator (would be collapsed as duplicate),
        // insert into the first gap between two content blocks that has no separator yet.
        if (_layout.Entries.Count == 0 || _layout.Entries[^1].Kind != ContextMenuItemKinds.Separator)
        {
            _layout.Entries.Add(sep);
            CommitLayout("add separator");
            return;
        }

        for (var i = 0; i < _layout.Entries.Count - 1; i++)
        {
            if (_layout.Entries[i].Kind == ContextMenuItemKinds.Separator)
                continue;
            if (_layout.Entries[i + 1].Kind != ContextMenuItemKinds.Separator)
            {
                _layout.Entries.Insert(i + 1, sep);
                CommitLayout("add separator");
                return;
            }
        }

        FmMessageBox.Show(
            Window.GetWindow(this),
            UiText.Get("SeparatorsFull"),
            UiText.Get("MenuPageTitle"),
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    void OnResetMenuClick(object sender, RoutedEventArgs e)
    {
        var answer = FmMessageBox.Show(
            Window.GetWindow(this),
            UiText.Get("ResetMenuConfirm"),
            UiText.Get("MenuPageTitle"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes)
            return;

        using var scope = ClickTrace.Begin("companion", "context-menu-reset");
        CompanionState.Document.ContextMenu = null;
        CompanionState.Save();
        _layout = ContextMenuLayout.CreateDefault();
        _loading = true;
        try { RootTitleBox.Text = _layout.RootTitle; }
        finally { _loading = false; }
        RebuildEntries();
        RefreshPreview();
        ClickTrace.Ok("context menu reset to default");
        scope.MarkFinished();
    }

    void Move(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= _layout.Entries.Count)
            return;
        (_layout.Entries[index], _layout.Entries[target]) = (_layout.Entries[target], _layout.Entries[index]);
        CommitLayout("move");
    }

    /// <summary>
    /// A row needs about 400 px (toggle, text, name box, arrows), the preview 316 px.
    /// In a narrower window the preview steps aside so the rows keep room for their text.
    /// </summary>
    const double MinWidthWithPreview = 720;

    void OnBodySizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= MinWidthWithPreview;
        PreviewCard.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        PreviewGapColumn.Width = new GridLength(wide ? 16 : 0);
        PreviewColumn.Width = new GridLength(wide ? 300 : 0);
    }

    void RebuildEntries()
    {
        EntriesPanel.Children.Clear();
        for (var i = 0; i < _layout.Entries.Count; i++)
            EntriesPanel.Children.Add(BuildRow(i, _layout.Entries[i]));
    }

    UIElement BuildRow(int index, ContextMenuEntry entry)
    {
        var isSeparator = entry.Kind == ContextMenuItemKinds.Separator;
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(12, isSeparator ? 6 : 10, 8, isSeparator ? 6 : 10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Opacity = entry.Visible || isSeparator ? 1.0 : 0.55,
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (!isSeparator)
        {
            var toggle = new ToggleSwitch
            {
                IsChecked = entry.Visible,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = UiText.Get("ShowInMenu"),
            };
            toggle.Click += (_, _) =>
            {
                entry.Visible = toggle.IsChecked == true;
                card.Opacity = entry.Visible ? 1.0 : 0.55;
                SaveLayout($"visible {entry.Kind}={entry.Visible}");
            };
            Grid.SetColumn(toggle, 0);
            grid.Children.Add(toggle);
        }

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = ThemedText(
            ContextMenuItemKinds.DisplayName(entry.Kind),
            isSeparator ? 12 : 14,
            secondary: isSeparator,
            wrap: true);
        texts.Children.Add(title);

        var hint = DescribeEntry(entry.Kind);
        if (!string.IsNullOrEmpty(hint))
        {
            texts.Children.Add(ThemedText(hint, 12, secondary: true, wrap: true, topMargin: 2));
        }

        // Under the texts rather than beside them: the default label is the placeholder and must fit in every language.
        if (ContextMenuItemKinds.SupportsLabel(entry.Kind))
        {
            var labelBox = new WpfTextBox
            {
                Margin = new Thickness(0, 8, 0, 0),
                PlaceholderText = ContextMenuItemKinds.DefaultLabel(entry.Kind),
                Text = entry.Label ?? string.Empty,
                ToolTip = UiText.Get("CustomLabelEmpty"),
            };
            labelBox.TextChanged += (_, _) =>
            {
                entry.Label = string.IsNullOrWhiteSpace(labelBox.Text) ? null : labelBox.Text.Trim();
                SaveLayout($"label {entry.Kind}");
            };
            texts.Children.Add(labelBox);
        }

        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(MakeIconButton(SymbolRegular.ArrowUp24, UiText.Get("MoveUp"), index > 0, () => Move(index, -1)));
        buttons.Children.Add(MakeIconButton(SymbolRegular.ArrowDown24, UiText.Get("MoveDown"), index < _layout.Entries.Count - 1, () => Move(index, +1)));
        if (isSeparator)
        {
            buttons.Children.Add(MakeIconButton(SymbolRegular.Delete24, UiText.Get("DeleteSeparator"), true, () =>
            {
                _layout.Entries.RemoveAt(index);
                CommitLayout("remove separator");
            }));
        }

        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        card.Child = grid;
        return card;
    }

    static WpfTextBlock ThemedText(string text, double size, bool secondary = false, bool wrap = false, double topMargin = 0)
    {
        var tb = new WpfTextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Margin = new Thickness(0, topMargin, 0, 0),
        };
        // Explicit theme brush — code-created TextBlocks otherwise keep SystemColors.ControlText (black).
        tb.SetResourceReference(
            WpfTextBlock.ForegroundProperty,
            secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        return tb;
    }

    static WpfButton MakeIconButton(SymbolRegular symbol, string tip, bool enabled, Action onClick)
    {
        var b = new WpfButton
        {
            Icon = new SymbolIcon { Symbol = symbol },
            Appearance = ControlAppearance.Transparent,
            ToolTip = tip,
            IsEnabled = enabled,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(6),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    static string DescribeEntry(string kind)
    {
        var doc = CompanionState.Document;
        return kind switch
        {
            ContextMenuItemKinds.Pin => UiText.Get("NotePin"),
            ContextMenuItemKinds.QuickColor => UiText.Get("NoteQuick"),
            ContextMenuItemKinds.ColorPresets => PresetRows(doc).Count switch
            {
                0 => UiText.Get("NotePresetsEmpty"),
                var n => UiText.Format("NotePresetsCount", n),
            },
            ContextMenuItemKinds.RecentColors => UiText.Get("NoteRecent"),
            ContextMenuItemKinds.SystemPalette => UiText.Get("NotePalette"),
            ContextMenuItemKinds.Reset => UiText.Get("NoteReset"),
            ContextMenuItemKinds.Tags => TagRows(doc).Count switch
            {
                0 => UiText.Get("NoteTagsEmpty"),
                var n => UiText.Format("NoteTagsCount", n),
            },
            ContextMenuItemKinds.Settings => UiText.Get("NoteSettings"),
            ContextMenuItemKinds.Separator => UiText.Get("NoteSeparator"),
            _ => string.Empty,
        };
    }

    void RefreshPreview()
    {
        PreviewRootTitle.Text = $"{_layout.RootTitle}  ›";
        PreviewRootTitle.SetResourceReference(WpfTextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var lines = new List<(string? Text, bool Dim)>();
        var doc = CompanionState.Document;

        foreach (var entry in _layout.Entries.Where(e => e.Visible || e.Kind == ContextMenuItemKinds.Separator))
        {
            if (!entry.Visible && entry.Kind != ContextMenuItemKinds.Separator)
                continue;

            var label = string.IsNullOrWhiteSpace(entry.Label) ? ContextMenuItemKinds.DefaultLabel(entry.Kind) : entry.Label;
            switch (entry.Kind)
            {
                case ContextMenuItemKinds.Separator:
                    lines.Add(("──────────────", true));
                    break;
                case ContextMenuItemKinds.Pin:
                    lines.Add((UiText.Get("Pin") + " / " + UiText.Get("Unpin"), false));
                    break;
                case ContextMenuItemKinds.ColorPresets:
                    foreach (var r in PresetRows(doc))
                    {
                        var t = !string.IsNullOrWhiteSpace(r.Label)
                            ? UiText.Format("ColorLine", r.Label)
                            : UiText.Format("ColorHex", r.HexRgb?.Trim().TrimStart('#').ToUpperInvariant() ?? "");
                        lines.Add((t, false));
                    }
                    break;
                case ContextMenuItemKinds.RecentColors:
                    foreach (var name in RecentNames(doc))
                        lines.Add((UiText.Format("RecentLine", name), false));
                    break;
                case ContextMenuItemKinds.SystemPalette:
                    foreach (var key in new[] { "PalRed", "PalOrange", "PalYellow", "PalLime", "PalGreen", "PalTeal", "PalCyan", "PalBlue", "PalIndigo", "PalPurple", "PalPink", "PalBrown", "PalGray" })
                        lines.Add((UiText.Format("ColorLine", UiText.Get(key)), false));
                    break;
                case ContextMenuItemKinds.Tags:
                    foreach (var t in TagRows(doc))
                        lines.Add((UiText.Format("TagLine", t.Label), false));
                    break;
                default:
                    lines.Add((label, false));
                    break;
            }
        }

        PreviewPanel.Children.Clear();
        foreach (var (text, dim) in lines)
        {
            if (text is null)
                continue;
            PreviewPanel.Children.Add(ThemedText(text, 13, secondary: dim, wrap: true, topMargin: 4));
        }

        var count = lines.Count(l => l.Text is not null && !l.Dim);
        PreviewCount.Text = count == 0
            ? UiText.Get("MenuEmpty")
            : UiText.Format("MenuLineCount", count);
        PreviewCount.SetResourceReference(WpfTextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
    }

    /// <summary>The names the Explorer menu shows for the last three colors (same rules as the shell's menu builder).</summary>
    static IEnumerable<string> RecentNames(AppStateDocument doc)
    {
        foreach (var token in (doc.RecentColors ?? []).Take(3))
        {
            if (token.StartsWith("rgb-", StringComparison.OrdinalIgnoreCase))
            {
                if (MenuColorHex.TryParse(token[4..], out var rgb))
                    yield return "#" + MenuColorHex.ToHexRgb(rgb);
                continue;
            }

            var preset = doc.MenuColors.FirstOrDefault(c => string.Equals(c.Id, token, StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
                yield return string.IsNullOrWhiteSpace(preset.Label) ? "#" + preset.HexRgb.Trim().TrimStart('#').ToUpperInvariant() : preset.Label;
        }
    }

    static List<MenuColorPreset> PresetRows(AppStateDocument doc) =>
        doc.MenuColors.Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Id)).ToList();

    static List<MenuTagPreset> TagRows(AppStateDocument doc) =>
        doc.MenuTags.Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Id)).ToList();
}
