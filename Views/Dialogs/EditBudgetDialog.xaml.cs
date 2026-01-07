using System;
using System.Windows;
using Finly.Pages; // BudgetRow

namespace Finly.Views.Dialogs
{
    public partial class EditBudgetDialog : Window
    {
        public BudgetDialogViewModel Budget { get; private set; }

        public EditBudgetDialog()
        {
            InitializeComponent();
            Budget = new BudgetDialogViewModel();
            DataContext = Budget;
        }

        public void LoadBudget(BudgetRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            Budget.Name = row.Name;
            Budget.Type = row.Type;
            Budget.StartDate = row.StartDate;
            Budget.PlannedAmount = row.PlannedAmount;

            // EndDate liczy się w VM po ustawieniu Type/StartDate.
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BudgetDialogViewModel vm)
                return;

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                MessageBox.Show("Podaj nazwę budżetu.", "Walidacja",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (vm.StartDate == null)
            {
                MessageBox.Show("Wybierz datę startu.", "Walidacja",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (vm.PlannedAmount < 0)
            {
                MessageBox.Show("Kwota planowana nie może być ujemna.", "Walidacja",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
