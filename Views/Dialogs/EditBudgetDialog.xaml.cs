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
            // Budget.Type jest już ustawione przez binding z ComboBoxa
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}