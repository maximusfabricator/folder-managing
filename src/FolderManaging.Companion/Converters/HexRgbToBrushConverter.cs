// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FolderManaging.Core;
using FolderManaging.Core.Storage;

namespace FolderManaging.Companion.Converters;

internal sealed class HexRgbToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && MenuColorHex.TryParse(hex, out var c))
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));

        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows Hide on a scheme color, and Delete on a color from My colors.</summary>
internal sealed class ColorActionVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var id = value as string;
        var hide = string.Equals(parameter as string, "hide", StringComparison.OrdinalIgnoreCase);
        var show = hide ? HiddenColors.CanHide(id) : HiddenColors.CanDelete(id);
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
