// Finly/Controls/ModeToBoolConv.cs
using System;
using System.Globalization;
using System.Windows.Data;
using Finly.Models;

namespace Finly.Helpers.Converters
{
    public sealed class ModeToBoolConv : IValueConverter
    {
        public static readonly ModeToBoolConv Instance = new();
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is DateRangeMode m && Enum.TryParse(p?.ToString(), out DateRangeMode want) && m == want;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }
}


