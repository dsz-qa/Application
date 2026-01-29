using System;
using System.Globalization;
using System.Windows.Data;

namespace Finly.Helpers.Converters
{
    public sealed class BankLogoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var bankName = (value as string ?? string.Empty).Trim().ToLowerInvariant();

            // identyczna logika jak w BanksPage (stabilnie i przewidywalnie)
            if (bankName.Contains("revolut")) return "pack://application:,,,/Assets/Banks/revolutlogo.png";
            if (bankName.Contains("mbank")) return "pack://application:,,,/Assets/Banks/mbanklogo.jpg";
            if (bankName.Contains("pko")) return "pack://application:,,,/Assets/Banks/pkobplogo.jpg";
            if (bankName.Contains("pekao")) return "pack://application:,,,/Assets/Banks/pekaologo.jpg";
            if (bankName.Contains("ing")) return "pack://application:,,,/Assets/Banks/inglogo.png";
            if (bankName.Contains("credit agricole") || bankName.Contains("creditagricole")) return "pack://application:,,,/Assets/Banks/creditagricolelogo.png";
            if (bankName.Contains("santander")) return "pack://application:,,,/Assets/Banks/santanderlogo.png";
            if (bankName.Contains("alior")) return "pack://application:,,,/Assets/Banks/aliorbanklogo.png";
            if (bankName.Contains("millennium") || bankName.Contains("milenium")) return "pack://application:,,,/Assets/Banks/milleniumlogo.png";
            if (bankName.Contains("xtb")) return "pack://application:,,,/Assets/Banks/xtblogo.png";

            return "pack://application:,,,/Assets/Banks/innybank.png";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
