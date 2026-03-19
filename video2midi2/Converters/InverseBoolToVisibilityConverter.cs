// (WPF has BooleanToVisibilityConverter built in but this gives you inverse support)
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Video2Midi2.Converters;

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}