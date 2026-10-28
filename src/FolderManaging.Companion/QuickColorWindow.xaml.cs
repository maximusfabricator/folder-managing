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
        Wheel.SelectedColor = recent.Count > 0 ? recent[0] : MediaColor.FromRgb(0x00, 0x7A, 0xCC);
        SyncFromWheel();
        FillRecent(recent);
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

    /// <summary>Colors last used on folders, newest first.</summary>
    static List<MediaColor> RecentColors()
    {
        var doc = CompanionState.Document;
        var colors = new List<MediaColor>();
        foreach (var token in doc.RecentColors ?? [])
        {
            var hex = token.StartsWith("rgb-", StringComparison.OrdinalIgnoreCase)
                ? token[4..]
                : doc.MenuColors.FirstOrDefault(c => string.Equals(c.Id, token, StringComparison.OrdinalIgnoreCase))?.HexRgb;
            if (MenuColorHex.TryParse(hex, out var c))
                colors.Add(MediaColor.FromRgb(c.R, c.G, c.B));
            if (colors.Count == RecentSwatches)
                break;
        }

        return colors;
    }

    void FillRecent(IReadOnlyList<MediaColor> colors)
    {
        RecentPanel.Visibility = colors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentList.ItemsSource = colors.Select(c =>
        {
            var hex = $"{c.R:X2}{c.G:X2}{c.B:X2}";
            var swatch = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "#" + hex,
                Background = new SolidColorBrush(c),
                BorderBrush = (System.Windows.Media.Brush)FindResource("ControlStrongStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                Template = SwatchTemplate(),
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, "#" + hex);
            swatch.Click += (_, _) =>
            {
                Wheel.SelectedColor = c;
                SyncFromWheel();
            };
            return swatch;
        }).ToList();
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
            var actions = _folderPaths.Select(p => new FolderAction(FolderActionKind.Rgb, p, hex)).ToList();
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
