using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using Finly.Models;
using Finly.Services;

namespace Finly.Views
{
    public partial class EditExpenseWindow : Window
    {
        private readonly int _userId;
        private readonly Expense _expense;

        public EditExpenseWindow(Expense expense, int userId)
        {
            InitializeComponent();
            _expense = expense ?? throw new ArgumentNullException(nameof(expense));
            _userId = userId;

            // Podpowiedzi kategorii
            try
            {
                var cats = DatabaseService.GetCategoriesByUser(_userId);
                CategoryBox.ItemsSource = cats;
            }
            catch { /* w razie czego – brak podpowiedzi nie blokuje okna */ }

            // Wypełnij pola
            AmountBox.Text = _expense.Amount.ToString("0.##", CultureInfo.CurrentCulture);
            DateBox.SelectedDate = _expense.Date;
            CategoryBox.Text = string.IsNullOrWhiteSpace(_expense.CategoryName) ? _expense.Category : _expense.CategoryName;
            DescriptionBox.Text = _expense.Description ?? string.Empty;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Walidacja kwoty
            var amountText = (AmountBox.Text ?? "").Trim();

            // Spróbuj wg kultury lokalnej, a potem invariant (kropka)
            bool parsed =
                double.TryParse(amountText, NumberStyles.Any, CultureInfo.CurrentCulture, out double amount)
             || double.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out amount);

            if (!parsed || amount <= 0)
            {
                ToastService.Error("Podaj poprawną kwotę większą od 0.");
                AmountBox.Focus();
                AmountBox.SelectAll();
                return;
            }

            var date = DateBox.SelectedDate ?? DateTime.Today;

            var categoryName = (CategoryBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(categoryName))
                categoryName = "Inne";

            int categoryId;
            try
            {
                categoryId = DatabaseService.GetOrCreateCategoryId(categoryName, _userId);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się odczytać/zapisać kategorii: " + ex.Message);
                return;
            }

            var updated = new Expense
            {
                Id = _expense.Id,
                Amount = amount,
                Date = date,
                Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                CategoryId = categoryId,
                UserId = _userId
            };

            try
            {
                DatabaseService.UpdateExpense(updated);
                ToastService.Success("Zapisano zmiany.");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd podczas zapisu: " + ex.Message);
            }
        }
    }
}
