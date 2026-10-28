// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using FolderManaging.Companion.Localization;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;

namespace FolderManaging.Companion.Pages;

public partial class ColorsPage : System.Windows.Controls.UserControl
{
    MenuColorPreset? _editing;
    string? _editOriginalHex;
    bool _suspendEditorSync;
    ObservableCollection<MenuColorPreset>? _colors;

    bool _listening;

    public ColorsPage()
    {
        InitializeComponent();
        UiDirection.Apply(this);
        Unloaded += OnUnloaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_listening)
        {
            CompanionState.DocumentChanged += OnDocumentChanged;
            _listening = true;
        }

        BindColors();
        if (SchemeCombo.Items.Count == 0)
            FillSchemeCombo();
    }

    void FillSchemeCombo()
    {
        foreach (var scheme in ColorSchemes.All)
        {
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            foreach (var color in scheme.Colors)
            {
                MenuColorHex.TryParse(color.HexRgb, out var c);
                row.Children.Add(new Border
                {
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 3, 0),
                    CornerRadius = new CornerRadius(3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B)),
                    ToolTip = UiText.Get(color.NameKey),
                });
            }

            row.Children.Add(new TextBlock
            {
                Text = UiText.Get(scheme.NameKey),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            SchemeCombo.Items.Add(new ComboBoxItem { Content = row, Tag = scheme });
        }

        SchemeCombo.SelectedIndex = 0;
    }

    void OnAddSchemeClick(object sender, RoutedEventArgs e)
    {
        if (SchemeCombo.SelectedItem is not ComboBoxItem { Tag: ColorScheme scheme })
            return;

        using var scope = ClickTrace.Begin("companion", "add-scheme", detail: scheme.Id);
        var doc = CompanionState.Document;
        var before = doc.MenuColors.Count;
        var added = ColorSchemes.AddTo(doc, scheme,
            (schemeKey, colorKey) => UiText.Format("SchemeColorLabel", UiText.Get(schemeKey), UiText.Get(colorKey)));
        if (added > 0)
        {
            CompanionState.Save();
            foreach (var preset in doc.MenuColors.Skip(before))
                TryWritePresetIco(preset);
            BindColors();
        }

        ClickTrace.Ok($"added {added}");
        scope.MarkFinished();
        FmMessageBox.Show(
            Window.GetWindow(this),
            added > 0 ? UiText.Format("SchemeAdded", added, UiText.Get(scheme.NameKey)) : UiText.Get("SchemeAlreadyAdded"),
            "Folder Managing",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
        var editingId = _editing?.Id;
        BindColors();
        if (editingId is null)
            return;

        var live = CompanionState.Document.MenuColors.FirstOrDefault(c =>
            string.Equals(c.Id, editingId, StringComparison.OrdinalIgnoreCase));
        if (live is null)
        {
            CloseEditor();
            return;
        }

        _editing = live;
    }

    void BindColors()
    {
        var doc = CompanionState.Document;
        _colors = new ObservableCollection<MenuColorPreset>(doc.MenuColors);
        ColorItems.ItemsSource = _colors;
        ClickTrace.Step($"ColorsPage loaded count={_colors.Count}");
    }

    void OnAnyTextChanged(object sender, TextChangedEventArgs e)
    {
        // The binding also raises TextChanged when the page fills in saved names; only typing should write state.json.
        if (sender is UIElement { IsKeyboardFocusWithin: true })
            CompanionState.Save();
    }

    void OnAddColorClick(object sender, RoutedEventArgs e)
    {
        using var scope = ClickTrace.Begin("companion", "add-color");
        try
        {
            var doc = CompanionState.Document;
            var color = NextSeedColor(doc.MenuColors.Count);
            var preset = new MenuColorPreset
            {
                Id = GenerateUniquePresetId(doc.MenuColors),
                Label = "",
                HexRgb = MenuColorHex.ToHexRgb(color),
            };

            doc.MenuColors.Add(preset);
            _colors?.Add(preset);
            CompanionState.Save();
            TryWritePresetIco(preset);
            ClickTrace.Ok($"id={preset.Id} hex={preset.HexRgb}");
            scope.MarkFinished();
            BeginEdit(preset);
        }
        catch (Exception ex)
        {
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            throw;
        }
    }

    void OnDeleteColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not MenuColorPreset preset)
            return;

        using var scope = ClickTrace.Begin("companion", "delete-color", detail: preset.Id);
        if (FolderColors.PresetRgb.ContainsKey(preset.Id))
        {
            ClickTrace.Fail($"builtin color cannot be deleted id={preset.Id}");
            scope.MarkFinished();
            FmMessageBox.Show(
                Window.GetWindow(this),
                UiText.Get("BuiltInColor"),
                "Folder Managing",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var doc = CompanionState.Document;
        var id = preset.Id;
        doc.MenuColors.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (_colors is not null)
        {
            for (var i = _colors.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_colors[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    _colors.RemoveAt(i);
            }
        }

        if (_editing is not null && string.Equals(_editing.Id, id, StringComparison.OrdinalIgnoreCase))
            CloseEditor();
        CompanionState.Save();
        ClickTrace.Ok($"deleted id={preset.Id}");
        scope.MarkFinished();
    }

    void OnEditColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not MenuColorPreset preset)
            return;
        ClickTrace.Step($"edit-color open id={preset.Id} hex={preset.HexRgb}");
        BeginEdit(preset);
    }

    void BeginEdit(MenuColorPreset preset)
    {
        _editing = preset;
        _editOriginalHex = preset.HexRgb;
        ColorEditorTitle.Text = UiText.Format(
            "ColorNamed",
            string.IsNullOrWhiteSpace(preset.Label) ? preset.Id : preset.Label);

        _suspendEditorSync = true;
        if (MenuColorHex.TryParse(preset.HexRgb, out var c))
        {
            SliderR.Value = c.R;
            SliderG.Value = c.G;
            SliderB.Value = c.B;
            HexEditor.Text = MenuColorHex.ToHexRgb(c);
            UpdateEditorPreview(c);
        }

        _suspendEditorSync = false;
        ColorEditorCard.Visibility = Visibility.Visible;
        ColorsToolbar.Visibility = Visibility.Collapsed;
        ClickTrace.Step($"edit-color begin id={preset.Id} originalHex={_editOriginalHex}");
    }

    void OnEditorRgbChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suspendEditorSync || _editing is null)
            return;

        var r = (byte)SliderR.Value;
        var g = (byte)SliderG.Value;
        var b = (byte)SliderB.Value;
        _suspendEditorSync = true;
        HexEditor.Text = $"{r:X2}{g:X2}{b:X2}";
        _suspendEditorSync = false;
        UpdateEditorPreview(Color.FromArgb(255, r, g, b));
    }

    void OnEditorHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_suspendEditorSync || _editing is null)
            return;

        var t = HexEditor.Text.Trim().TrimStart('#');
        if (t.Length != 6 || !MenuColorHex.TryParse(t, out var c))
            return;

        _suspendEditorSync = true;
        SliderR.Value = c.R;
        SliderG.Value = c.G;
        SliderB.Value = c.B;
        _suspendEditorSync = false;
        UpdateEditorPreview(c);
    }

    void UpdateEditorPreview(Color c)
    {
        EditorPreview.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
    }

    void OnEditorApply(object sender, RoutedEventArgs e)
    {
        using var scope = ClickTrace.Begin("companion", "save-color", detail: _editing?.Id);
        try
        {
            if (_editing is null)
            {
                ClickTrace.Fail("no editing preset");
                scope.MarkFinished();
                return;
            }

            if (!MenuColorHex.TryParse(HexEditor.Text, out var c))
            {
                ClickTrace.Fail($"invalid hex '{HexEditor.Text}'");
                scope.MarkFinished();
                return;
            }

            var newHex = MenuColorHex.ToHexRgb(c);
            ClickTrace.Step($"commit hex {_editOriginalHex} -> {newHex}");
            _editing.HexRgb = newHex;
            CompanionState.Save();
            ColorItems.Items.Refresh();
            TryWritePresetIco(_editing);

            ClickTrace.Ok($"saved id={_editing.Id} hex={_editing.HexRgb}");
            scope.MarkFinished();
            CloseEditor();
        }
        catch (Exception ex)
        {
            ClickTrace.Fail(ex);
            scope.MarkFinished();
        }
    }

    void OnEditorCancel(object sender, RoutedEventArgs e)
    {
        using var scope = ClickTrace.Begin("companion", "cancel-color", detail: _editing?.Id);
        ClickTrace.Step($"discard draft; kept originalHex={_editOriginalHex}");
        ClickTrace.Ok("cancelled — model unchanged");
        scope.MarkFinished();
        CloseEditor();
    }

    void CloseEditor()
    {
        ColorEditorCard.Visibility = Visibility.Collapsed;
        ColorsToolbar.Visibility = Visibility.Visible;
        _editing = null;
        _editOriginalHex = null;
    }

    static void TryWritePresetIco(MenuColorPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Id))
            return;
        if (!MenuColorHex.TryParse(preset.HexRgb, out var c))
            return;
        try
        {
            FolderIconManager.WritePresetIcon(preset.Id, c);
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"WritePresetIcon failed: {ex.Message}");
        }
    }

    static string GenerateUniquePresetId(IEnumerable<MenuColorPreset> existing)
    {
        var taken = new HashSet<string>(
            existing.Where(p => !string.IsNullOrWhiteSpace(p.Id)).Select(p => p.Id),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = $"custom-{i:00}";
            if (!taken.Contains(candidate))
                return candidate;
        }

        return $"custom-{Guid.NewGuid():N}".Substring(0, 16);
    }

    static Color NextSeedColor(int existingCount)
    {
        var hue = (existingCount * 37) % 360;
        return HsvToRgb(hue, 0.55, 0.95);
    }

    static Color HsvToRgb(double hueDeg, double s, double v)
    {
        var c = v * s;
        var hp = hueDeg / 60.0;
        var x = c * (1 - Math.Abs(hp % 2 - 1));
        double r = 0, g = 0, b = 0;
        if (hp < 1) { r = c; g = x; }
        else if (hp < 2) { r = x; g = c; }
        else if (hp < 3) { g = c; b = x; }
        else if (hp < 4) { g = x; b = c; }
        else if (hp < 5) { r = x; b = c; }
        else { r = c; b = x; }

        var m = v - c;
        return Color.FromArgb(
            255,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
