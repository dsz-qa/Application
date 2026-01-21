using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Finly.Helpers.Converters
{
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = 0;
            if (value is int i) count = i;
            else if (value is long l) count = (int)l;
            else
            {
                try { count = System.Convert.ToInt32(value); } catch { count = 0; }
            }

            bool invert = (parameter as string) == "Invert";

            if (invert)
            {
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}