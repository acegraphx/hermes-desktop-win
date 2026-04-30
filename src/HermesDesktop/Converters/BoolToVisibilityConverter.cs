using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HermesDesktop.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    // Treat as "visible" anything truthy: bool true, int > 0 (e.g. collection .Count),
    // a non-empty string, or any non-null reference. Bool false / 0 / null / empty string -> Collapsed.
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        int i => i > 0,
        long l => l > 0,
        double d => d > 0,
        string s => !string.IsNullOrEmpty(s),
        _ => true,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        IsTruthy(value) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        BoolToVisibilityConverter.IsTruthy(value) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
