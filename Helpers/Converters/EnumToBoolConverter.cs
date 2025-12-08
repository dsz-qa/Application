using System;
using System.Globalization;
using System.Windows.Data;

namespace Finly.Helpers.Converters
{
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            var enumString = parameter.ToString();
            if (string.IsNullOrEmpty(enumString)) return false;
            return string.Equals(value.ToString(), enumString, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null) return Binding.DoNothing;
            if (value is bool b && b)
            {
                // attempt to parse enum from parameter using targetType
                try
                {
                    if (targetType.IsEnum)
                        return Enum.Parse(targetType, parameter.ToString()!);
                }
                catch { }
            }
            return Binding.DoNothing;
        }
    }
}

