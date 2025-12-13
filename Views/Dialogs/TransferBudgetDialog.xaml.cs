using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Finly.Pages; // zakładam, że BudgetRow jest w Finly.Pages (tak jak napisałaś)

namespace Finly.Views.Dialogs
{
    public partial class TransferBudgetDialog : Window
    {
        private readonly List<BudgetRow> _budgets;

        public BudgetRow? FromBudget => FromBudgetCombo.SelectedItem as BudgetRow;
        public BudgetRow? ToBudget => ToBudgetCombo.SelectedItem as BudgetRow;
        public decimal Amount { get; private set; }

        public TransferBudgetDialog(IEnumerable<BudgetRow> budgets, BudgetRow? current)
        {
            InitializeComponent();

            _budgets = budgets?.ToList() ?? new List<BudgetRow>();

            FromBudgetCombo.ItemsSource = _budgets;
            ToBudgetCombo.ItemsSource = _budgets;

            if (current != null)
                FromBudgetCombo.SelectedItem = _budgets.FirstOrDefault(b => b.Id == current.Id);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (FromBudget == null)
            {
                MessageBox.Show("Wybierz budżet źródłowy (z którego przenosisz środki).");
                return;
            }

            if (ToBudget == null)
            {
                MessageBox.Show("Wybierz budżet docelowy (do którego przenosisz środki).");
                return;
            }

            if (FromBudget.Id == ToBudget.Id)
            {
                MessageBox.Show("Budżet źródłowy i docelowy muszą być różne.");
                return;
            }

            var raw = (AmountBox.Text ?? string.Empty).Replace(" ", "").Trim();

            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
            {
                MessageBox.Show("Podaj poprawną kwotę większą od zera.");
                return;
            }

            Amount = amount;
            DialogResult = true;
            Close();
        }
    }
}
