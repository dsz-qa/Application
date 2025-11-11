using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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
            };
        }

        private void RefreshKpis()
        {
            var s = DatabaseService.GetMoneySnapshot(_uid);

            var freeCash = Math.Max(0m, s.Cash - s.Envelopes);
            var total = s.Banks + freeCash + s.Envelopes;

            KpiTotal.Text = total.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            KpiFreeCash.Text = freeCash.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            KpiEnvelopes.Text = s.Envelopes.ToString("N2", CultureInfo.CurrentCulture) + " zł";

            // Nagłówek expandera
            BanksExpander.Header = s.Banks.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private void LoadBanks()
        {
            var list = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();
            BanksList.ItemsSource = list.Select(a => new { a.AccountName, a.Balance }).ToList();
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            // Tu w przyszłości filtruj dane po zakresie
            RefreshKpis();
            LoadBanks();
        }

        // Presety
        private void QuickToday_Click(object sender, RoutedEventArgs e)
        {
            var d = DateTime.Today;
            PeriodBar.Mode = DateRangeMode.Day;
            PeriodBar.StartDate = d;
            PeriodBar.EndDate = d;
        }

        private void QuickMonth_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Today;
            PeriodBar.Mode = DateRangeMode.Month;
            PeriodBar.StartDate = new DateTime(now.Year, now.Month, 1);
            PeriodBar.EndDate = PeriodBar.StartDate.AddMonths(1).AddDays(-1);
        }

        private void QuickQuarter_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Today;
            int qStartMonth = ((now.Month - 1) / 3) * 3 + 1;
            PeriodBar.Mode = DateRangeMode.Quarter;
            PeriodBar.StartDate = new DateTime(now.Year, qStartMonth, 1);
            PeriodBar.EndDate = PeriodBar.StartDate.AddMonths(3).AddDays(-1);
        }

        private void QuickYear_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Today;
            PeriodBar.Mode = DateRangeMode.Year;
            PeriodBar.StartDate = new DateTime(now.Year, 1, 1);
            PeriodBar.EndDate = new DateTime(now.Year, 12, 31);
        }
    }
}









