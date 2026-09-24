using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
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
