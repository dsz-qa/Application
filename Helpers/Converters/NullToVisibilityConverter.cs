using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Finly.Helpers.Converters
{
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Gdy null albo pusty string → ukryj
            if (value == null) return Visibility.Collapsed;
            if (value is string s && string.IsNullOrWhiteSpace(s))
                return Visibility.Collapsed;

            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

