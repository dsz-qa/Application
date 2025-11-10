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

            // W nagłówku Expandera pokaż kwotę łączną kont bankowych:
            var banksHeader = s.Banks.ToString("N2", CultureInfo.CurrentCulture) + " zł";
            BanksExpander.Header = banksHeader;
        }

        private void LoadBanks()
        {
            // Jeżeli metoda zwraca List<BankAccountModel> – użyj wprost:
            var list = DatabaseService.GetAccounts(_uid) ?? new List<BankAccountModel>();

            BanksList.ItemsSource = list.Select(a => new
            {
                a.AccountName,
                a.Balance
            }).ToList();
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            // Tu kiedyś: filtrowanie po dacie
            RefreshKpis();
            LoadBanks();
        }
    }
}







