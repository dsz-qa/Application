using System;
using System.Windows;
using System.Windows.Controls;
using Finly.Models;
using Finly.Services;

namespace Finly.Pages
{
    public partial class AddExpensePage : UserControl
    {
        private readonly int _userId;

        public AddExpensePage() : this(SafeGetUserId()) { }

        public AddExpensePage(int userId)
        {
            InitializeComponent();
            _userId = userId;
        }

        private static int SafeGetUserId()
        {
            try { return UserService.GetCurrentUserId(); }
            catch { return 0; }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AmountBox.Text) ||
                string.IsNullOrWhiteSpace(CategoryBox.Text) ||
                !DateBox.SelectedDate.HasValue ||
                string.IsNullOrWhiteSpace(DescriptionBox.Text))
            {
                ToastService.Warning("Uzupełnij wszystkie pola.");
                return;
            }

            if (!double.TryParse(AmountBox.Text, out double amount))
            {
                ToastService.Error("Wprowadź poprawną kwotę.");
                return;
            }

            int categoryId = DatabaseService.GetOrCreateCategoryId(CategoryBox.Text.Trim(), _userId);

            var expense = new Expense
            {
                Amount = amount,
                CategoryId = categoryId,
                Date = DateBox.SelectedDate!.Value,
                Description = DescriptionBox.Text,
                UserId = _userId
            };

            DatabaseService.AddExpense(expense);
            ToastService.Success("Dodano wydatek.");

            (Window.GetWindow(this) as Finly.Views.ShellWindow)?.NavigateTo("Dashboard");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            (Window.GetWindow(this) as Finly.Views.ShellWindow)?.NavigateTo("Dashboard");
        }
    }
}
