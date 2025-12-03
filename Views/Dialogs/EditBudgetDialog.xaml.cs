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
        public BudgetDialogViewModel Budget { get; }

        public EditBudgetDialog()
        {
            InitializeComponent();

            // Domyślne wartości
            Budget = new BudgetDialogViewModel
            {
                StartDate = DateTime.Today,
                EndDate = DateTime.Today
            };

            // Ustawiamy DataContext na VM – XAML binda do Name, StartDate, EndDate, PlannedAmount itd.
            DataContext = Budget;
        }

        /// <summary>
        /// Wczytuje istniejący budżet do dialogu (edycja).
        /// Wołane z BudgetsPage: dialog.LoadBudget(budget);
        /// </summary>
        public void LoadBudget(BudgetRow row)
        {
            if (row == null)
                return;

            Budget.Name = row.Name;
            Budget.Type = row.Type;              // np. "Budżet", "Wydatek", "Przychód"
            Budget.TypeDisplay = row.TypeDisplay;

            Budget.StartDate = row.StartDate;
            Budget.EndDate = row.EndDate;

            Budget.PlannedAmount = row.PlannedAmount;
            Budget.SpentAmount = row.SpentAmount;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
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