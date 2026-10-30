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
        BindSchemes();
        BindHidden();
    }

    void BindSchemes()
    {
        var doc = CompanionState.Document;
        SchemeItems.ItemsSource = ColorSchemes.All
            .Select(s => new SchemeRow(s, ColorSchemes.IsEnabled(doc, s.Id), doc))
            .ToList();
    }

    void BindHidden()
    {
        var doc = CompanionState.Document;
        HiddenColors.Normalize(doc);
        var rows = doc.HiddenColorIds
            .Select(id => new HiddenColorRow(id, HiddenColors.Title(doc, id), HiddenColors.Hex(doc, id)))
            .ToList();
        HiddenItems.ItemsSource = rows;
        HiddenSection.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HiddenHeader.Text = UiText.Format("HiddenColorsTitle", rows.Count);
    }

    async void OnSchemeToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.ToggleSwitch { Tag: SchemeRow row } toggle)
            return;

        var on = toggle.IsChecked == true;
        using var scope = ClickTrace.Begin("companion", "color-scheme", detail: $"{row.Scheme.Id}={on}");
        ColorSchemes.SetEnabled(CompanionState.Document, row.Scheme.Id, on);
        CompanionState.Save();
        ClickTrace.Ok($"enabled={string.Join(",", CompanionState.Document.EnabledColorSchemes)}");
        scope.MarkFinished();
        if (!on)
            return;

        // Written now so the first right-click after turning a scheme on does not wait for its icons.
        try
        {
            await Task.Run(() => FolderIconManager.EnsureSchemeMenuIcons(row.Scheme));
        }
        catch (Exception ex)
        {
            ClickTrace.Step($"scheme icons failed: {ex.Message}");
        }
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
        BindSchemes();
        BindHidden();
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
        _colors = new ObservableCollection<MenuColorPreset>(HiddenColors.OfferedPresets(doc));
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

        if (!HiddenColors.CanDelete(preset.Id))
            return;

        using var scope = ClickTrace.Begin("companion", "delete-color", detail: preset.Id);
        var doc = CompanionState.Document;
        HiddenColors.Restore(doc, preset.Id);
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

    void OnHideColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not MenuColorPreset preset)
            return;
        HideColor(preset.Id);
    }

    void OnHideSchemeColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
            return;
        HideColor(id);
    }

    void HideColor(string id)
    {
        using var scope = ClickTrace.Begin("companion", "hide-color", detail: id);
        if (!HiddenColors.CanHide(id))
        {
            scope.MarkFinished();
            return;
        }

        HiddenColors.Hide(CompanionState.Document, id);
        if (_editing is not null && string.Equals(_editing.Id, id, StringComparison.OrdinalIgnoreCase))
            CloseEditor();
        CompanionState.Save();
        BindColors();
        BindSchemes();
        BindHidden();
        ClickTrace.Ok($"hidden id={id}");
        scope.MarkFinished();
    }

    void OnRestoreColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
            return;

        using var scope = ClickTrace.Begin("companion", "restore-color", detail: id);
        HiddenColors.Restore(CompanionState.Document, id);
        CompanionState.Save();
        BindColors();
        BindSchemes();
        BindHidden();
        ClickTrace.Ok($"restored id={id}");
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

/// <summary>A built-in color scheme on the Colors page: name, swatches still offered, and whether it is on.</summary>
public sealed class SchemeRow
{
    public SchemeRow(ColorScheme scheme, bool isOn, AppStateDocument doc)
    {
        Scheme = scheme;
        IsOn = isOn;
        Swatches = HiddenColors.OfferedSchemeColors(doc, scheme)
            .Select(c => new SchemeSwatch(c.Id, UiText.Get(c.NameKey), c.Color))
            .ToList();
    }

    public ColorScheme Scheme { get; }

    public string Name => Scheme.Name;

    public bool IsOn { get; }

    public IReadOnlyList<SchemeSwatch> Swatches { get; }
}

/// <summary>One hidden color, with Restore.</summary>
public sealed class HiddenColorRow(string id, string title, string hexRgb)
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public bool TitleIsHex => Title.StartsWith('#');

    public string HexRgb { get; } = hexRgb;
}

public sealed class SchemeSwatch(string id, string name, Color color)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public System.Windows.Media.Brush Brush { get; } =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
}
