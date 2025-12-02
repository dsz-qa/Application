using System;
using System.Windows;
using System.Windows.Controls;
using Finly.ViewModels;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Finly.Pages
{
    public partial class ChartsPage : UserControl
    {
        public ChartsPage()
        {
            InitializeComponent();

            DataContext = new ChartsViewModel();

            // Ustaw legendę/teksty na biało, jeśli dostępne
            try
            {
                var white = new SolidColorPaint(new SKColor(255,255,255));
                if (DataContext is ChartsViewModel vm)
                {
                    if (vm.CategoriesSeries != null)
                        foreach (var series in vm.CategoriesSeries) series.DataLabelsPaint = white;
                    if (vm.AccountsSeries != null)
                        foreach (var series in vm.AccountsSeries) series.DataLabelsPaint = white;
                    if (vm.TrendSeries != null)
                        foreach (var series in vm.TrendSeries) series.DataLabelsPaint = white;
                    if (vm.WeekdaySeries != null)
                        foreach (var series in vm.WeekdaySeries) series.DataLabelsPaint = white;
                }
            }
            catch { }

            // domyślny tryb: wydatki
            if (ModeExpensesBtn != null)
            {
                ApplyPrimary(ModeExpensesBtn);
                if (DataContext is ChartsViewModel vm)
                    vm.SetMode("Expenses");
            }

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

            var tag = btn.Tag as string;
            switch (tag)
            {
                case "Expenses":
                    vm.SetMode("Expenses");
                    break;
                case "Incomes":
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
