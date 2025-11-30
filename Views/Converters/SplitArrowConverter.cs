using System;
using System.Globalization;
using System.Windows.Data;

namespace Finly.Views.Converters
{
 // Splits strings of the form "from?to" and returns requested part
 public sealed class SplitArrowConverter : IValueConverter
 {
 public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
 {
 var s = value?.ToString() ?? string.Empty;
 var parts = s.Split('?');
 var want = (parameter?.ToString() ?? string.Empty).ToLowerInvariant();
 if (want == "from") return parts.Length >0 ? parts[0].Trim() : string.Empty;
 if (want == "to") return parts.Length >1 ? parts[1].Trim() : string.Empty;
 return s;
 }

 public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
 => throw new NotSupportedException();
 }
}
