using System.Globalization;
using System.Windows.Data;

public class EqualsToStringConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => (value is bool b && b) ? parameter?.ToString() : Binding.DoNothing;
}
