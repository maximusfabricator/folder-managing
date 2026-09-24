using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Storage;
using Wpf.Ui.Controls;
using MediaColor = System.Windows.Media.Color;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace FolderManaging.Companion;

public partial class QuickColorWindow : FluentWindow
{
    string? _folderPath;

    public QuickColorWindow(string? folderPath = null)
    {
        InitializeComponent();
        _folderPath = folderPath;
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_folderPath))
            FolderPathText.Text = _folderPath;
        Wheel.SelectedColor = MediaColor.FromRgb(0x00, 0x7A, 0xCC);
        SyncFromWheel();
    }

    void OnWheelColorChanged(object? sender, EventArgs e) => SyncFromWheel();

    void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
            return;
        Wheel.Value = BrightnessSlider.Value / 100.0;
        SyncFromWheel();
    }

    void SyncFromWheel()
    {
        var c = Wheel.SelectedColor;
        PreviewSwatch.Background = new SolidColorBrush(c);
        HexText.Text = $"{c.R:X2}{c.G:X2}{c.B:X2}";
        if (Math.Abs(BrightnessSlider.Value - Wheel.Value * 100) > 0.5)
            BrightnessSlider.Value = Wheel.Value * 100;
    }

    void OnPickFolderClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Выберите папку для покраски",
            UseDescriptionForTitle = true,
            SelectedPath = _folderPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;
        _folderPath = dlg.SelectedPath;
        FolderPathText.Text = _folderPath;
    }

    void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_folderPath))
        {
            OnPickFolderClick(sender, e);
            if (string.IsNullOrWhiteSpace(_folderPath))
                return;
        }

        var path = _folderPath!;
        var media = Wheel.SelectedColor;
        var tint = System.Drawing.Color.FromArgb(media.R, media.G, media.B);
        var hex = $"{media.R:X2}{media.G:X2}{media.B:X2}";
        using var scope = ClickTrace.Begin("tray", "quick-color", detail: hex, path: path);
        SetBusy(true);
        try
        {
            var denied = await Task.Run(() =>
            {
                try
                {
                    FolderIconManager.ApplyRgb(path, tint);
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }
            });

            if (denied)
            {
                if (!CompanionState.Document.AutoElevateProtectedFolders)
                {
                    var answer = System.Windows.MessageBox.Show(
                        this,
                        $"Windows защищает эту папку:\n{path}\n\nПовторить с правами администратора?",
                        "Folder Managing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (answer != MessageBoxResult.Yes)
                    {
                        ClickTrace.Fail("access denied, user declined elevation");
                        scope.MarkFinished();
                        return;
                    }
                }

                var (ok, err) = await Task.Run(() =>
                {
                    var r = ElevatedCliInvoker.TryInvoke("rgb", path, hex, out var e2);
                    return (r, e2);
                });
                if (!ok)
                    throw new UnauthorizedAccessException(string.IsNullOrWhiteSpace(err) ? $"Cannot write to folder '{path}'." : err);
            }
            else
            {
                await Task.Run(() => new FolderStateEditor(new JsonFolderStateStore())
                    .SetColorTokenAsync(path, $"rgb-{hex.ToLowerInvariant()}", CancellationToken.None));
            }

            ClickTrace.Ok($"rgb=#{hex} path='{path}'");
            scope.MarkFinished();
            Close();
        }
        catch (Exception ex)
        {
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            System.Windows.MessageBox.Show(
                this,
                $"Не удалось покрасить папку:\n{ex.Message}",
                "Folder Managing",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
        ApplyButton.Content = busy ? "Крашу…" : "Покрасить";
        Wheel.IsEnabled = !busy;
        BrightnessSlider.IsEnabled = !busy;
    }
}
