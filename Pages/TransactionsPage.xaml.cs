using System;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Finly.Services;
using Finly.Views;
using Finly.Views.Dialogs;

namespace Finly.Pages
{
    public partial class TransactionsPage : UserControl
    {
        private int _uid => UserService.GetCurrentUserId();

        public TransactionsPage()
        {
            InitializeComponent();
            LoadCategories();
            LoadExpenses();
        }

        private void LoadCategories()
        {
            var dt = DatabaseService.GetCategories(_uid);
            CategoryFilter.ItemsSource = dt.DefaultView;
            CategoryFilter.DisplayMemberPath = "Name";
            CategoryFilter.SelectedValuePath = "Id";
            CategoryFilter.SelectedIndex = -1;
        }

        private void LoadExpenses()
        {
            int? cat = CategoryFilter.SelectedValue is int v ? v : (int?)null;
            DateTime? from = FromDate.SelectedDate;
            DateTime? to = ToDate.SelectedDate;
            string? q = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim();

            var dt = DatabaseService.GetExpenses(_uid, from, to, cat, q);
            TransactionsGrid.ItemsSource = dt.DefaultView;

            // licznik w stopce
            EntriesCountText.Text = dt.Rows.Count.ToString();

            // suma bieżącego widoku (po filtrach)
            var sum = dt.AsEnumerable().Sum(r => Convert.ToDouble(r["Amount"]));
            SumMonthText.Text = $"{sum:N2} zł";
        }

        private void ApplyFilters_Click(object s, RoutedEventArgs e) => LoadExpenses();

        private void ClearFilters_Click(object s, RoutedEventArgs e)
        {
            FromDate.SelectedDate = null;
            ToDate.SelectedDate = null;
            CategoryFilter.SelectedIndex = -1;
            PresetRangeCombo.SelectedIndex = 0; // (brak)
            SearchBox.Text = "";
            LoadExpenses();
        }

        private void PresetRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string? label = (PresetRangeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            RangePreset preset = label switch
            {
                "Dzisiaj" => RangePreset.Dzisiaj,
                "Ten tydzień" => RangePreset.TenTydzien,
                "Ten miesiąc" => RangePreset.TenMiesiac,
                "Ten rok" => RangePreset.TenRok,
                _ => RangePreset.Brak
            };

            DateRangeService.GetRange(preset, out var from, out var to);
            FromDate.SelectedDate = from;
            ToDate.SelectedDate = to;

            LoadExpenses();
        }

        private void AddExpense_Click(object sender, RoutedEventArgs e)
        {
            (Window.GetWindow(this) as ShellWindow)?.NavigateTo("AddExpense");
        }

        private void EditExpense_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: { } tag })
            {
                int id = Convert.ToInt32(tag);
                var exp = DatabaseService.GetExpenseById(id);
                if (exp == null)
                {
                    ToastService.Info("Nie znaleziono wydatku do edycji.");
                    return;
                }

                var w = new Finly.Views.EditExpenseWindow(exp, _uid)
                {
                    Owner = Window.GetWindow(this)
                };

                if (w.ShowDialog() == true)
                {
                    ToastService.Success("Zaktualizowano wydatek.");
                    LoadExpenses();
                }
            }
        }

        private void DeleteExpense_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: { } tag })
            {
                var id = Convert.ToInt32(tag);
                var dlg = new ConfirmDialog("Czy na pewno chcesz usunąć ten wydatek?")
                { Owner = Window.GetWindow(this) };

                if (dlg.ShowDialog() == true)
                {
                    DatabaseService.DeleteExpense(id);
                    ToastService.Success("Usunięto wydatek.");
                    LoadExpenses();
                }
            }
            else
            {
                ToastService.Info("Nie udało się odczytać identyfikatora wiersza.");
            }
        }
    }
}






