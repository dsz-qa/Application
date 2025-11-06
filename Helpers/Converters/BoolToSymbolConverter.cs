using System;
using System.Globalization;
using System.Windows.Data;

namespace Finly.Helpers.Converters
{
    /// Zwraca „✓”/„✗” dla bool.
    public sealed class BoolToSymbolConverter : IValueConverter
    {
        public string TrueSymbol { get; set; } = "✓";
        public string FalseSymbol { get; set; } = "✗";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? TrueSymbol : FalseSymbol;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
