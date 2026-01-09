using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Finly.Helpers.Converters
{
    /// <summary>
    /// WPF IValueConverter + bezpieczne parsowanie kwot:
    /// - akceptuje 200,50 i 200.50
    /// - toleruje spacje / NBSP / NNBSP / apostrof jako separatory tysięcy
    /// - gdy występują ',' i '.', ostatni z nich traktuje jako separator dziesiętny
    /// </summary>
    public sealed class FlexibleDecimalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "0,00";

            if (value is decimal dec)
                return dec.ToString("0.00", culture);

            if (value is double dbl)
                return System.Convert.ToDecimal(dbl, CultureInfo.InvariantCulture).ToString("0.00", culture);

            if (value is float fl)
                return System.Convert.ToDecimal(fl, CultureInfo.InvariantCulture).ToString("0.00", culture);

            if (value is int i)
                return ((decimal)i).ToString("0.00", culture);

            if (value is long l)
                return ((decimal)l).ToString("0.00", culture);

            // fallback
            if (decimal.TryParse(value.ToString(), NumberStyles.Number, culture, out var v))
                return v.ToString("0.00", culture);

            return "0,00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value?.ToString() ?? string.Empty).Trim();

            if (TryParseFlexibleDecimal(s, out var dec))
                return dec;

            return 0m;
        }

        public static bool TryParseFlexibleDecimal(string input, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var s = input.Trim();

            // usuń typowe separatory tysięcy
            s = s.Replace(" ", "")
                 .Replace("\u00A0", "")  // NBSP
                 .Replace("\u202F", "")  // NNBSP
                 .Replace("'", "");

            // zostaw tylko cyfry, +/-, kropkę i przecinek
            s = new string(s.Where(ch => char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-' || ch == '+').ToArray());
            if (string.IsNullOrWhiteSpace(s)) return false;

            // znak na początku
            int sign = 1;
            if (s[0] == '-')
            {
                sign = -1;
                s = s.Substring(1);
            }
            else if (s[0] == '+')
            {
                s = s.Substring(1);
            }

            if (string.IsNullOrWhiteSpace(s)) return false;

            int lastDot = s.LastIndexOf('.');
            int lastComma = s.LastIndexOf(',');

            char? decimalSep = null;

            if (lastDot >= 0 && lastComma >= 0)
            {
                // oba występują -> ostatni to separator dziesiętny
                decimalSep = lastDot > lastComma ? '.' : ',';
            }
            else if (lastDot >= 0)
            {
                // pojedyncza kropka => dziesiętny (żeby 200.00 nie robiło 20000)
                decimalSep = '.';
            }
            else if (lastComma >= 0)
            {
                decimalSep = ',';
            }

            string normalized;

            if (decimalSep == null)
            {
                normalized = s; // same cyfry
            }
            else
            {
                var other = decimalSep == '.' ? ',' : '.';
                var tmp = s.Replace(other.ToString(), "");   // usuń drugi separator jako tysięczny
                normalized = tmp.Replace(decimalSep.Value, '.'); // ujednolić na '.'
            }

            if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return false;

            value = parsed * sign;
            return true;
        }
    }
}
