using System;
using System.Globalization;
using System.Windows.Data;

namespace Finly.Helpers.Converters
{
    // Simple mapping from category name to emoji string
    public class CategoryToEmojiConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var name = (value?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            return name switch
            {
                "jedzenie" => "🍽️",
                "transport" => "🚗",
                "mieszkanie" => "🏡",
                "rachunki" => "💳",
                "rozrywka" => "🎉",
                "zdrowie" => "💊",
                "ubrania" => "👗",
                "wynagrodzenie" => "💰",
                "prezent" => "🎁",
                _ => "❓"
            };
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value;
    }
}
