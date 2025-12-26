using System;
using System.Windows;
using Finly.Pages;   // żeby widzieć BudgetRow (jest w BudgetsPage.xaml.cs w namespace Finly.Pages)

namespace Finly.Views.Dialogs
{
    public partial class EditBudgetDialog : Window
    {
        /// <summary>
        /// ViewModel dialogu – zawiera dane budżetu wprowadzane przez użytkownika.
        /// </summary>
        public BudgetDialogViewModel Budget { get; private set; }

        public EditBudgetDialog()
        {
            InitializeComponent();
            Budget = new BudgetDialogViewModel();
            DataContext = Budget;
        }

        public void LoadBudget(BudgetRow row)
        {
            Budget.Name = row.Name;
            Budget.Type = row.Type;          // <-- ważne
            Budget.StartDate = row.StartDate;
            Budget.PlannedAmount = row.PlannedAmount;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BudgetDialogViewModel vm)
                return;

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                MessageBox.Show("Podaj nazwę budżetu.", "Walidacja", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (vm.StartDate == null)
            {
                MessageBox.Show("Wybierz datę startu.", "Walidacja", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (vm.PlannedAmount < 0)
            {
                MessageBox.Show("Kwota planowana nie może być ujemna.", "Walidacja", MessageBoxButton.OK, MessageBoxImage.Warning);
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