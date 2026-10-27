// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

#if FM_DEV
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FolderManaging.Companion.Diagnostics;

/// <summary>
/// Developer builds only: PNG of what WPF draws in a window, so a tour can keep its windows off screen.
/// The system backdrop is not part of that drawing; the theme's background stands in for it.
/// </summary>
internal static class OffScreenShot
{
    internal static void Save(Window window, string path)
    {
        if (window.ActualWidth < 1 || window.ActualHeight < 1)
            return;

        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);

        var background = new DrawingVisual();
        using (var context = background.RenderOpen())
        {
            var brush = window.TryFindResource("ApplicationBackgroundBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.White;
            context.DrawRectangle(brush, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        }
        bitmap.Render(background);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
#endif
