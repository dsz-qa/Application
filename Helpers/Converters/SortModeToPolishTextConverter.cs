using System;
using System.Globalization;
using System.Windows.Data;
using Finly.ViewModels;

namespace Finly.Pages
{
    public sealed class SortModeToPolishTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TransactionsViewModel.TransactionSortMode m)
            {
                return m switch
                {
                    TransactionsViewModel.TransactionSortMode.DateDesc => "Od najnowszych",
                    TransactionsViewModel.TransactionSortMode.DateAsc => "Od najstarszych",
                    _ => value.ToString()
                };
            }

            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value?.ToString() ?? "").Trim();

            return s switch
            {
                "Od najnowszych" => TransactionsViewModel.TransactionSortMode.DateDesc,
                "Od najstarszych" => TransactionsViewModel.TransactionSortMode.DateAsc,
                _ => Binding.DoNothing
            };
        }
    }
}
