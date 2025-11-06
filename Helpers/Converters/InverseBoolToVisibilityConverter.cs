using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Finly.Helpers.Converters
{
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public bool CollapseWhenFalse { get; set; } = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var b = !(value is bool v && v);
            if (b) return Visibility.Visible;
            return CollapseWhenFalse ? Visibility.Collapsed : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => !(value is Visibility v) || v != Visibility.Visible;
    }
}
