using System;
using System.Globalization;
using System.Windows;
using Finly.Models;
using Finly.Services;

namespace Finly.Views
{
    public partial class EditExpenseWindow : Window
    {
        private readonly int _userId;
        private Expense _expense;

        // DODAWANIE – pusty rekord
        public EditExpenseWindow(int userId)
        {
            InitializeComponent();
            _userId = userId;
            _expense = new Expense
            {
                Id = 0,
                UserId = userId,
                Date = DateTime.Today,
                Amount = 0
            };
            InitCategories();
            BindCurrent();
        }

        // EDYCJA – istniejący rekord
        public EditExpenseWindow(Expense expense, int userId)
        {
            InitializeComponent();
            _userId = userId;
            _expense = expense ?? throw new ArgumentNullException(nameof(expense));
            InitCategories();
            BindCurrent();
        }

        private void InitCategories()
        {
            try { CategoryBox.ItemsSource = DatabaseService.GetCategoriesByUser(_userId); }
            catch { /* brak podpowiedzi nie blokuje okna */ }
        }

        private void BindCurrent()
        {
            AmountBox.Text = _expense.Amount.ToString("0.##", CultureInfo.CurrentCulture);
            DateBox.SelectedDate = _expense.Date;
            var catName = _expense.CategoryName ?? _expense.Category ?? string.Empty;
            CategoryBox.Text = catName;
            DescriptionBox.Text = _expense.Description ?? string.Empty;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var amountText = (AmountBox.Text ?? "").Trim();

            if (!(double.TryParse(amountText, NumberStyles.Any, CultureInfo.CurrentCulture, out var amount) ||
                  double.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out amount)) ||
                amount <= 0)
            {
                ToastService.Error("Podaj poprawną kwotę większą od 0.");
                AmountBox.Focus(); AmountBox.SelectAll();
                return;
            }

            var date = DateBox.SelectedDate ?? DateTime.Today;
            var categoryName = (CategoryBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(categoryName)) categoryName = "Inne";

            int categoryId;
            try { categoryId = DatabaseService.GetOrCreateCategoryId(_userId, categoryName); }
            catch (Exception ex) { ToastService.Error("Nie udało się odczytać/zapisać kategorii: " + ex.Message); return; }

            _expense.Amount = amount;
            _expense.Date = date;
            _expense.Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
            _expense.CategoryId = categoryId;
            _expense.UserId = _userId;

            try
            {
                if (_expense.Id == 0) DatabaseService.AddExpense(_expense);
                else DatabaseService.UpdateExpense(_expense);

                ToastService.Success("Zapisano.");
                DialogResult = true;
                Close();
            }
            catch (Exception ex) { ToastService.Error("Błąd podczas zapisu: " + ex.Message); }
        }
    }
}


