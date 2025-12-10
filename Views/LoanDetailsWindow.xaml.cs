using System;
using System.Windows;
using System.Collections.Generic;

namespace Finly.Views
{
    public partial class LoanDetailsWindow : Window
    {
        public LoanDetailsWindow.DetailsVm Vm { get; }

        public LoanDetailsWindow(DetailsVm vm)
        {
            InitializeComponent();
            Vm = vm ?? throw new ArgumentNullException(nameof(vm));

            LoanTitle.Text = Vm.Name;
            ParamsText.Text = $"Kwota poczatkowa: {Vm.Principal:N0} zl\nOprocentowanie: {Vm.InterestRate}%\nOkres: {Vm.TermMonths} mies.";
            PayoffInfo.Text = $"Jesli bedziesz placic normalnie, splacisz do: {Vm.StartDate.AddMonths(Vm.TermMonths):dd.MM.yyyy}";

            // Harmonogram – jeśli przyszedł z VM, pokaż go
            if (Vm.Schedule != null && Vm.Schedule.Count > 0)
            {
                ScheduleItems.ItemsSource = Vm.Schedule;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Simulate_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(ExtraAmountBox.Text, out var extra) || extra <= 0)
            {
                SimResult.Text = "Podaj poprawna kwote.";
                return;
            }

            var saved = Math.Round(extra * 0.05m, 2);
            var months = (int)(extra / Math.Max(1, Vm.Principal));
            SimResult.Text = $"Oszczedzisz ~{saved:N2} zl na odsetkach i skrocisz kredyt o ~{months} miesiecy (szac.).";
        }

        public class ScheduleRow
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }

            public string Display => $"{Date:dd.MM.yyyy} — {Amount:N2} zł";
        }

        public class DetailsVm
        {
            public string Name { get; set; } = "";
            public decimal Principal { get; set; }
            public decimal InterestRate { get; set; }
            public DateTime StartDate { get; set; }
            public int TermMonths { get; set; }

            // nowość: harmonogram
            public System.Collections.Generic.List<ScheduleRow> Schedule { get; set; }
                = new System.Collections.Generic.List<ScheduleRow>();
        }
    }
}