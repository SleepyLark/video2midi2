// Converters/DoubleToIntConverter.cs
using System.Globalization;
using System.Windows.Data;

namespace Video2Midi2.Converters;

public class DoubleToIntConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? (int)d : 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i ? (double)i : 0.0;
}