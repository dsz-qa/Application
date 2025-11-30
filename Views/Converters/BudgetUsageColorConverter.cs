using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Finly.Views.Converters
{
 // Returns a Brush based on usage percent (spent/limit)
 public class BudgetUsageColorConverter : IMultiValueConverter
 {
 public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
 {
 try
 {
 if (values == null || values.Length <2)
 return Brushes.Green;
 double spent = ToDouble(values[0]);
 double limit = ToDouble(values[1]);
 if (limit <=0) return Brushes.Green;
 double p = spent / limit; //1.0 ==100%
 if (p <0.70) return new SolidColorBrush(ColorFromHex("#2ECC71")); // green
 if (p <=1.0) return new SolidColorBrush(ColorFromHex("#F1C40F")); // yellow
 return new SolidColorBrush(ColorFromHex("#E74C3C")); // red
 }
 catch { return Brushes.Green; }
 }

 public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
 {
 throw new NotImplementedException();
 }

 private static double ToDouble(object o)
 {
 if (o == null) return 0d;
 if (o is double d) return d;
 if (o is float f) return f;
 if (o is decimal m) return (double)m;
 if (o is int i) return i;
 if (o is string s)
 {
 // try current culture then invariant
 if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var val)) return val;
 if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var val2)) return val2;
 return 0d;
 }
 double.TryParse(o.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v);
 return v;
 }

 private static Color ColorFromHex(string hex)
 {
 return (Color)ColorConverter.ConvertFromString(hex);
 }
 }
}
