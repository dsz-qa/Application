using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
                var white = new SolidColorPaint(new SKColor(255, 255, 255));

                if (DataContext is ChartsViewModel vm)
                {
                    if (vm.CategoriesSeries != null)
                        foreach (var series in vm.CategoriesSeries)
                            series.DataLabelsPaint = white;

                    if (vm.BankAccountsSeries != null)
                        foreach (var series in vm.BankAccountsSeries)
                            series.DataLabelsPaint = white;

                    if (vm.TrendSeries != null)
                        foreach (var series in vm.TrendSeries)
                            series.DataLabelsPaint = white;

                    if (vm.WeekdaySeries != null)
                        foreach (var series in vm.WeekdaySeries)
                            series.DataLabelsPaint = white;
                }
            }
            catch
            {
                // ignorujemy błędy z kosmetyki etykiet
            }

            // domyślny tryb: wydatki
            if (ModeExpensesBtn != null && DataContext is ChartsViewModel vm2)
            {
                ApplyPrimary(ModeExpensesBtn);
                vm2.SetMode("Expenses");
            }

            HookPeriodBar();
        }

        private void HookPeriodBar()
        {
            if (PeriodBar == null) return;

            PeriodBar.RangeChanged += (sender, args) =>
            {
                if (DataContext is ChartsViewModel vm)
                    vm.SetCustomRange(PeriodBar.StartDate, PeriodBar.EndDate);
            };

            PeriodBar.SearchClicked += (sender, args) =>
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

            // aktywny tryb → pomarańczowa ramka
            active.BorderThickness = new Thickness(2);
            active.BorderBrush = (Brush)Application.Current.Resources["Brand.Orange"];
        }

        private void ResetModeButtons()
        {
            ClearBorder(ModeExpensesBtn);
            ClearBorder(ModeIncomesBtn);
            ClearBorder(ModeTransferBtn);
            ClearBorder(ModeCashflowBtn);
        }

        private void ClearBorder(Button? btn)
        {
            if (btn == null) return;

            btn.BorderThickness = new Thickness(1);
            btn.BorderBrush = (Brush)Application.Current.Resources["Brand.Blue"]; // domyślna rama
        }

        private static void ApplyPrimary(Button btn)
        {
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
