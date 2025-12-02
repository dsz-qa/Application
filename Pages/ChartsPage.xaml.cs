using System;
using System.Windows;
using System.Windows.Controls;
using Finly.ViewModels;

namespace Finly.Pages
{
    public partial class ChartsPage : UserControl
    {
        public ChartsPage()
        {
            InitializeComponent();

            DataContext = new ChartsViewModel();

            // domyślny tryb: wydatki
            if (ModeExpensesBtn != null)
                ApplyPrimary(ModeExpensesBtn);

            HookPeriodBar();
        }

        private void HookPeriodBar()
        {
            if (PeriodBar == null) return;

            PeriodBar.RangeChanged += (_, __) =>
            {
                if (DataContext is ChartsViewModel vm)
                    vm.SetCustomRange(PeriodBar.StartDate, PeriodBar.EndDate);
            };

            PeriodBar.SearchClicked += (_, __) =>
            {
                if (DataContext is ChartsViewModel vm)
                    vm.SetCustomRange(PeriodBar.StartDate, PeriodBar.EndDate);
            };
        }

        // ===== Tryb (wydatki / przychody / transfer / cashflow) =====

        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (DataContext is not ChartsViewModel vm) return;

            SetActiveMode(btn);

            switch (btn.Content?.ToString())
            {
                case "Wydatki":
                    vm.SetMode("Expenses");
                    break;
                case "Przychody":
                    vm.SetMode("Incomes");
                    break;
                case "Transfer":
                    vm.SetMode("Transfer");
                    break;
                case "Cashflow":
                    vm.SetMode("Cashflow");
                    break;
            }
        }

        private void SetActiveMode(Button? active)
        {
            if (active == null) return;

            ResetModeButtons();
            ApplyPrimary(active);
        }

        private void ResetModeButtons()
        {
            if (ModeExpensesBtn != null) ClearPrimary(ModeExpensesBtn);
            if (ModeIncomesBtn != null) ClearPrimary(ModeIncomesBtn);
            if (ModeTransferBtn != null) ClearPrimary(ModeTransferBtn);
            if (ModeCashflowBtn != null) ClearPrimary(ModeCashflowBtn);
        }

        private static void ApplyPrimary(Button btn)
        {
            // korzystamy ze stylu PrimaryButton, który masz już w zasobach
            if (Application.Current.Resources["PrimaryButton"] is Style s)
                btn.Style = s;
        }

        private static void ClearPrimary(Button btn)
        {
            btn.ClearValue(StyleProperty);
        }

        // ===== Eksport =====

        private async void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChartsViewModel vm)
                await vm.ExportToPdfAsync();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChartsViewModel vm)
                vm.ExportToCsv();
        }
    }
}
