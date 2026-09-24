using System.Windows;
using System.Windows.Controls;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;
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
        _layout.Entries.Add(new ContextMenuEntry { Kind = ContextMenuItemKinds.Separator });
        CommitLayout("add separator");
    }

    void OnResetMenuClick(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(
            Window.GetWindow(this),
            "Вернуть контекстное меню к стандартному виду?",
            "Контекстное меню",
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (!isSeparator)
        {
            var toggle = new ToggleSwitch
            {
                IsChecked = entry.Visible,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = "Показывать в меню",
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
            secondary: isSeparator);
        texts.Children.Add(title);

        var hint = DescribeEntry(entry.Kind);
        if (!string.IsNullOrEmpty(hint))
        {
            texts.Children.Add(ThemedText(hint, 12, secondary: true, wrap: true, topMargin: 2));
        }

        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        if (ContextMenuItemKinds.SupportsLabel(entry.Kind))
        {
            var labelBox = new WpfTextBox
            {
                Width = 230,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                PlaceholderText = ContextMenuItemKinds.DefaultLabel(entry.Kind),
                Text = entry.Label ?? string.Empty,
                ToolTip = "Своё название (пусто — стандартное)",
            };
            labelBox.TextChanged += (_, _) =>
            {
                entry.Label = string.IsNullOrWhiteSpace(labelBox.Text) ? null : labelBox.Text.Trim();
                SaveLayout($"label {entry.Kind}");
            };
            Grid.SetColumn(labelBox, 2);
            grid.Children.Add(labelBox);
        }

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(MakeIconButton(SymbolRegular.ArrowUp24, "Выше", index > 0, () => Move(index, -1)));
        buttons.Children.Add(MakeIconButton(SymbolRegular.ArrowDown24, "Ниже", index < _layout.Entries.Count - 1, () => Move(index, +1)));
        if (isSeparator)
        {
            buttons.Children.Add(MakeIconButton(SymbolRegular.Delete24, "Удалить разделитель", true, () =>
            {
                _layout.Entries.RemoveAt(index);
                CommitLayout("remove separator");
            }));
        }

        Grid.SetColumn(buttons, 3);
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
            ContextMenuItemKinds.Pin => "Показывается нужный из двух: Pin или Unpin.",
            ContextMenuItemKinds.QuickColor => "Открывает окно с кругом — любой цвет точкой.",
            ContextMenuItemKinds.ColorPresets => PresetRows(doc).Count switch
            {
                0 => "Сейчас цветов нет — в меню ничего не появится. Добавь на странице «Цвета папок».",
                var n => $"Сейчас: {n} — каждый отдельной строкой.",
            },
            ContextMenuItemKinds.SystemPalette => "Red, Orange, Yellow, … Gray — фиксированный набор, 13 строк.",
            ContextMenuItemKinds.Reset => "Возвращает обычный жёлтый значок.",
            ContextMenuItemKinds.Tags => TagRows(doc).Count switch
            {
                0 => "Сейчас меток нет — в меню ничего не появится.",
                var n => $"Сейчас: {n} — каждая отдельной строкой.",
            },
            ContextMenuItemKinds.Settings => "Открывает это приложение.",
            ContextMenuItemKinds.Separator => "В новом меню Windows 11 разделители в подменю не поддерживаются (и могут ронять Проводник) — в ПКМ не показываются. Порядок групп всё равно сохраняется.",
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
                    lines.Add(("📌 Pin folder / Unpin folder", false));
                    break;
                case ContextMenuItemKinds.ColorPresets:
                    foreach (var r in PresetRows(doc))
                    {
                        var t = !string.IsNullOrWhiteSpace(r.Label)
                            ? $"🎨 Color — {r.Label}"
                            : $"🎨 Color #{r.HexRgb?.Trim().TrimStart('#').ToUpperInvariant()}";
                        lines.Add((t, false));
                    }
                    break;
                case ContextMenuItemKinds.SystemPalette:
                    foreach (var n in new[] { "Red", "Orange", "Yellow", "Lime", "Green", "Teal", "Cyan", "Blue", "Indigo", "Purple", "Pink", "Brown", "Gray" })
                        lines.Add(($"🎨 {n}", false));
                    break;
                case ContextMenuItemKinds.Tags:
                    foreach (var t in TagRows(doc))
                        lines.Add(($"🏷️ Tag — {t.Label}", false));
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
            PreviewPanel.Children.Add(ThemedText(text, 13, secondary: dim, topMargin: 4));
        }

        var count = lines.Count(l => l.Text is not null && !l.Dim);
        PreviewCount.Text = count == 0
            ? "Меню пустое — включи хотя бы один пункт."
            : $"Строк в меню: {count} (+ линии-разделители).";
        PreviewCount.SetResourceReference(WpfTextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
    }

    static List<MenuColorPreset> PresetRows(AppStateDocument doc) =>
        doc.MenuColors.Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Id)).ToList();

    static List<MenuTagPreset> TagRows(AppStateDocument doc) =>
        doc.MenuTags.Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Id)).ToList();
}
