using System;
using System.Windows;

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

        public class DetailsVm
        {
            public string Name { get; set; } = "";
            public decimal Principal { get; set; }
            public decimal InterestRate { get; set; }
            public DateTime StartDate { get; set; }
            public int TermMonths { get; set; }
        }
    }
}