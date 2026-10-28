// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using FolderManaging.Companion.Localization;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Storage;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MediaColor = System.Windows.Media.Color;

namespace FolderManaging.Companion;

public partial class QuickColorWindow : FluentWindow
{
    /// <summary>Swatches in the "Recent colors" row.</summary>
    const int RecentSwatches = 5;

    List<string> _folderPaths;
    bool _syncing;

    /// <summary>
    /// The color id of the last swatch clicked (a scheme color or one of My colors). While the wheel still shows its
    /// color, Color applies that id, so the folder keeps the scheme color's name and sorting like a menu click.
    /// </summary>
    (string Id, MediaColor Color)? _picked;

    public QuickColorWindow(IReadOnlyList<string>? folderPaths = null)
    {
        InitializeComponent();
        UiDirection.Apply(this);
        _folderPaths = folderPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShowFolders();
        var recent = RecentColors();
        Wheel.SelectedColor = recent.Count > 0 ? recent[0].Color : MediaColor.FromRgb(0x00, 0x7A, 0xCC);
        SyncFromWheel();
        FillRecent(recent);
        FillSchemes();
    }

    void ShowFolders()
    {
        if (_folderPaths.Count == 0)
            return;
        if (_folderPaths.Count == 1)
        {
            UiDirection.SetText(FolderPathText, UiText.LeftToRight(_folderPaths[0]));
            FolderPathText.ToolTip = null;
            return;
        }

        FolderPathText.Text = UiText.Format("FoldersSelected", _folderPaths.Count);
        FolderPathText.ToolTip = string.Join("\n", _folderPaths.Select(UiText.LeftToRight));
    }

    /// <summary>Colors last used on folders, newest first; <c>Id</c> is <c>null</c> for a color from the wheel.</summary>
    static List<(string? Id, MediaColor Color)> RecentColors()
    {
        var doc = CompanionState.Document;
        var colors = new List<(string?, MediaColor)>();
        foreach (var token in doc.RecentColors ?? [])
        {
            var isRgb = token.StartsWith("rgb-", StringComparison.OrdinalIgnoreCase);
            System.Drawing.Color c;
            var found = isRgb
                ? MenuColorHex.TryParse(token[4..], out c)
                : MenuStateReader.TryGetMenuTint(token, out c);
            if (found)
                colors.Add((isRgb ? null : token, MediaColor.FromRgb(c.R, c.G, c.B)));
            if (colors.Count == RecentSwatches)
                break;
        }

        return colors;
    }

    void FillRecent(IReadOnlyList<(string? Id, MediaColor Color)> colors)
    {
        RecentPanel.Visibility = colors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentList.ItemsSource = colors.Select(c => Swatch(c.Id, c.Color, null)).ToList();
    }

    /// <summary>One row per color scheme that is turned on: its name, then its colors.</summary>
    void FillSchemes()
    {
        var schemes = ColorSchemes.Enabled(CompanionState.Document);
        SchemesPanel.Visibility = schemes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SchemeList.Children.Clear();
        foreach (var scheme in schemes)
        {
            var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = new System.Windows.Controls.TextBlock
            {
                Text = scheme.Name,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            name.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            row.Children.Add(name);

            var swatches = new WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            foreach (var color in scheme.Colors)
            {
                var c = color.Color;
                swatches.Children.Add(Swatch(color.Id, MediaColor.FromRgb(c.R, c.G, c.B), ColorSchemes.DisplayName(scheme, color)));
            }

            System.Windows.Controls.Grid.SetColumn(swatches, 1);
            row.Children.Add(swatches);
            SchemeList.Children.Add(row);
        }
    }

    /// <param name="id">Color id applied when this swatch was the last pick; <c>null</c> applies the RGB value.</param>
    Button Swatch(string? id, MediaColor c, string? name)
    {
        var hex = $"{c.R:X2}{c.G:X2}{c.B:X2}";
        var tip = name is null ? "#" + hex : $"{name} · #{hex}";
        var swatch = new Button
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tip,
            Background = new SolidColorBrush(c),
            BorderBrush = (System.Windows.Media.Brush)FindResource("ControlStrongStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Template = SwatchTemplate(),
        };
        System.Windows.Automation.AutomationProperties.SetName(swatch, tip);
        swatch.Click += (_, _) =>
        {
            Wheel.SelectedColor = c;
            SyncFromWheel();
            _picked = id is null ? null : (id, c);
        };
        return swatch;
    }

    static ControlTemplate SwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    void OnWheelColorChanged(object? sender, EventArgs e) => SyncFromWheel();

    void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _syncing)
            return;
        Wheel.Value = BrightnessSlider.Value / 100.0;
        SyncFromWheel();
    }

    void SyncFromWheel()
    {
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            var c = Wheel.SelectedColor;
            PreviewSwatch.Background = new SolidColorBrush(c);
            var hex = $"{c.R:X2}{c.G:X2}{c.B:X2}";
            if (!HexBox.IsKeyboardFocusWithin && !string.Equals(HexBox.Text, hex, StringComparison.OrdinalIgnoreCase))
                HexBox.Text = hex;
            if (Math.Abs(BrightnessSlider.Value - Wheel.Value * 100) > 0.5)
                BrightnessSlider.Value = Wheel.Value * 100;
            ResetSaveButton();
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Typing a valid HEX (#RRGGBB, RRGGBB or #RGB) moves the wheel; anything else waits for more input.</summary>
    void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || !IsLoaded || !HexBox.IsKeyboardFocusWithin)
            return;
        var text = HexBox.Text.Trim();
        if (text.TrimStart('#').Length is not (3 or 6) || !MenuColorHex.TryParse(text, out var c))
            return;
        _syncing = true;
        try
        {
            Wheel.SelectedColor = MediaColor.FromRgb(c.R, c.G, c.B);
            PreviewSwatch.Background = new SolidColorBrush(Wheel.SelectedColor);
            if (Math.Abs(BrightnessSlider.Value - Wheel.Value * 100) > 0.5)
                BrightnessSlider.Value = Wheel.Value * 100;
            ResetSaveButton();
        }
        finally
        {
            _syncing = false;
        }
    }

    void OnHexLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var c = Wheel.SelectedColor;
        HexBox.Text = $"{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    string CurrentHex()
    {
        var c = Wheel.SelectedColor;
        return $"{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    void OnSaveColorClick(object sender, RoutedEventArgs e)
    {
        var hex = CurrentHex();
        using var scope = ClickTrace.Begin("companion", "save-to-my-colors", detail: hex);
        CompanionState.ReloadFromDiskIfStale();
        var doc = CompanionState.Document;
        var id = ColorSchemes.SaveToMyColors(doc, hex);
        CompanionState.Save();
        if (MenuColorHex.TryParse(hex, out var tint))
        {
            try
            {
                FolderIconManager.WritePresetIcon(id, tint);
            }
            catch (Exception ex)
            {
                ClickTrace.Step($"WritePresetIcon failed: {ex.Message}");
            }
        }

        SaveColorButton.Content = UiText.Get("SavedToMyColors");
        SaveColorButton.IsEnabled = false;
        ClickTrace.Ok($"id={id}");
        scope.MarkFinished();
    }

    void ResetSaveButton()
    {
        if (SaveColorButton.IsEnabled)
            return;
        SaveColorButton.Content = UiText.Get("SaveToMyColors");
        SaveColorButton.IsEnabled = true;
    }

    void OnPickFolderClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = UiText.Get("PickPaintFolder"),
            UseDescriptionForTitle = true,
            SelectedPath = _folderPaths.FirstOrDefault() ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;
        _folderPaths = [dlg.SelectedPath];
        ShowFolders();
    }

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (_folderPaths.Count == 0)
        {
            OnPickFolderClick(sender, e);
            if (_folderPaths.Count == 0)
                return;
        }

        var hex = CurrentHex();
        using var scope = ClickTrace.Begin("tray", "quick-color", detail: hex, path: _folderPaths[0]);
        SetBusy(true);
        try
        {
            var actions = _picked is { } picked && picked.Color == Wheel.SelectedColor
                ? _folderPaths.Select(p => new FolderAction(FolderActionKind.Color, p, picked.Id)).ToList()
                : _folderPaths.Select(p => new FolderAction(FolderActionKind.Rgb, p, hex)).ToList();
            var result = await FolderWriteGuard.RunAsync(this, actions);
            if (result.Done.Count > 0)
                ColorSortTip.TryShowOnce();
            if (result.AllDone)
            {
                ClickTrace.Ok($"rgb=#{hex} x{result.Done.Count}");
                scope.MarkFinished();
                Close();
                return;
            }

            ClickTrace.Fail($"{result.Failed.Count} folder(s) not colored");
            scope.MarkFinished();
            FolderWriteGuard.Report(this, result);
        }
        catch (Exception ex)
        {
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            FmMessageBox.Show(
                this,
                UiText.Format("PaintFail", ex.Message),
                "Folder Managing",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            if (IsLoaded)
                SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        ApplyButton.IsEnabled = !busy;
        ApplyButton.Content = busy ? UiText.Get("Painting") : UiText.Get("Paint");
        Wheel.IsEnabled = !busy;
        BrightnessSlider.IsEnabled = !busy;
        HexBox.IsEnabled = !busy;
    }
}
