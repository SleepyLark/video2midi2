// Converters/RgbColorToBrushConverter.cs
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Video2Midi2.Models;

namespace Video2Midi2.Converters;

public class RgbColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RgbColor c)
            return new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}