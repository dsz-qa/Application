using System;
using System.Globalization;
using System.Windows.Data;

namespace Finly.Helpers.Converters
{
    // Converts a share percent (0..100) to a pixel width based on ConverterParameter (max width).
    public sealed class ShareToWidthConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double maxWidth = 100.0;
            if (parameter != null)
            {
                var p = parameter.ToString();
                if (!string.IsNullOrWhiteSpace(p))
                {
                    // try invariant then culture parse
                    if (!double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out maxWidth))
                        double.TryParse(p, NumberStyles.Any, culture, out maxWidth);
                }
            }

            double share = 0.0;
            if (value is double d) share = d;
            else if (value is float f) share = f;
            else if (value is decimal m) share = (double)m;
            else if (value is int i) share = i;
            else if (value != null)
            {
                double.TryParse(value.ToString(), NumberStyles.Any, culture, out share);
            }

            // clamp share to 0..100
            if (double.IsNaN(share) || double.IsInfinity(share)) share = 0.0;
            share = Math.Max(0.0, Math.Min(100.0, share));

            var width = (share / 100.0) * maxWidth;
            return width;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
