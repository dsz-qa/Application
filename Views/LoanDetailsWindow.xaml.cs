using System;
using System.Collections.Generic;
using System.Windows;
using Finly.Models;

namespace Finly.Views
{
    public partial class LoanDetailsWindow : Window
    {
        public DetailsVm Vm { get; }

        public LoanDetailsWindow(DetailsVm vm)
        {
            InitializeComponent();
            Vm = vm ?? throw new ArgumentNullException(nameof(vm));

            LoanTitle.Text = Vm.Name;
            ParamsText.Text =
                $"Kwota: {Vm.Principal:N0} zł\n" +
                $"Oprocentowanie: {Vm.InterestRate:N2}%\n" +
                $"Okres: {Vm.TermMonths} mies.";

            PayoffInfo.Text =
                $"Jeśli będziesz płacić normalnie, spłacisz do: {Vm.StartDate.AddMonths(Vm.TermMonths):dd.MM.yyyy}";

            if (Vm.Schedule != null && Vm.Schedule.Count > 0)
                ScheduleItems.ItemsSource = Vm.Schedule;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Simulate_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(ExtraAmountBox.Text, out var extra) || extra <= 0)
            {
                SimResult.Text = "Podaj poprawną kwotę.";
                return;
            }

            var saved = Math.Round(extra * 0.05m, 2);
            var months = (int)(extra / Math.Max(1, Vm.Principal));
            SimResult.Text = $"Oszczędzisz ~{saved:N2} zł na odsetkach i skrócisz kredyt o ~{months} miesięcy (szac.).";
        }

        public class DetailsVm
        {
            public string Name { get; set; } = "";
            public decimal Principal { get; set; }
            public decimal InterestRate { get; set; }
            public DateTime StartDate { get; set; }
            public int TermMonths { get; set; }

            public List<LoanInstallmentRow> Schedule { get; set; } = new();
        }
    }
}
