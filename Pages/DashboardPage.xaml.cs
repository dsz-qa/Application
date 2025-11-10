using Finly.Models;
using Finly.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Finly.Views.Controls;

namespace Finly.Pages
{
    public partial class DashboardPage : UserControl
    {
        private readonly int _uid;

        public DashboardPage(int userId)
        {
            InitializeComponent();
            _uid = userId;

            Loaded += (_, __) =>
            {
                RefreshKpis();
                LoadBanks();
                // startowy zakres z kontrolki – możesz zostawić jak jest
            };
        }

        private void RefreshKpis()
        {
            var s = DatabaseService.GetMoneySnapshot(_uid);
            var freeCash = Math.Max(0m, s.Cash - s.Envelopes);
            var total = s.Banks + freeCash + s.Envelopes;

            KpiTotal.Text = total.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            KpiBanks.Text = s.Banks.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            KpiFreeCash.Text = freeCash.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            KpiEnvelopes.Text = s.Envelopes.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private void LoadBanks()
        {
            var list = DatabaseService.GetAccounts(_uid) ?? [];
            BanksList.ItemsSource = list.Select(a => new
            {
                a.AccountName,
                a.Balance
            }).ToList();
        }

        // Event z niebieskiego paska
        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            ReloadDashboard(PeriodBar.StartDate, PeriodBar.EndDate, PeriodBar.Mode);
        }

        // Na razie „stub” – wołaj Refresh/ładowanie wykresów pod wybrany zakres
        private void ReloadDashboard(DateTime start, DateTime end, DateRangeMode mode)
        {
            // TODO: filtrowanie wykresów/list po dacie
            RefreshKpis();
            LoadBanks();
        }

        private void AddExpense_Click(object sender, RoutedEventArgs e)
        {
            var shell = Window.GetWindow(this) as Finly.Views.ShellWindow;
            shell?.NavigateTo("addexpense");
        }
    }
}





