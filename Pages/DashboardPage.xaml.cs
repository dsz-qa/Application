using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class DashboardPage : UserControl, INotifyPropertyChanged
    {
        private readonly int _uid;

        // Dane do wykresów
        public ObservableCollection<CategorySpend> CategorySpendingCurrent { get; } = new();
        public ObservableCollection<CategorySpend> CategorySpendingLast30 { get; } = new();
        private decimal _maxAmountCurrent;
        public decimal MaxAmountCurrent { get => _maxAmountCurrent; set { _maxAmountCurrent = value; OnPropertyChanged(); } }
        private decimal _maxAmountLast30;
        public decimal MaxAmountLast30 { get => _maxAmountLast30; set { _maxAmountLast30 = value; OnPropertyChanged(); } }

        public DashboardPage(int userId)
        {
            InitializeComponent();
            _uid = userId;
            DataContext = this;

            Loaded += (_, __) =>
            {
                // domyślnie: Dzisiaj
                PeriodBar.SetPreset(DateRangeMode.Day);

                RefreshKpis();
                LoadBanks();
                LoadCategoryCharts();   // wypełnij wykresy
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
            LoadCategoryCharts();
        }

        private void ManualDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PeriodBar.Mode != DateRangeMode.Custom)
                PeriodBar.Mode = DateRangeMode.Custom;
        }

        // ===== WYKRESY: prosta agregacja (na razie przykładowe dane) =====
        private void LoadCategoryCharts()
        {
            // TODO: Podmienić na realne sumy z bazy w wybranym zakresie PeriodBar.StartDate/EndDate
            var sampleNow = new[]
            {
                new CategorySpend("Jedzenie",     820.50m),
                new CategorySpend("Transport",    210.00m),
                new CategorySpend("Mieszkanie",  1450.00m),
                new CategorySpend("Zdrowie",      90.00m),
                new CategorySpend("Rozrywka",     160.00m),
            };

            var sample30 = new[]
            {
                new CategorySpend("Jedzenie",     970.10m),
                new CategorySpend("Transport",    260.00m),
                new CategorySpend("Mieszkanie",  1450.00m),
                new CategorySpend("Zdrowie",     120.00m),
                new CategorySpend("Inne",         80.00m),
            };

            CategorySpendingCurrent.Clear();
            foreach (var x in sampleNow) CategorySpendingCurrent.Add(x);
            MaxAmountCurrent = CategorySpendingCurrent.Any() ? CategorySpendingCurrent.Max(s => s.Amount) : 1m;

            CategorySpendingLast30.Clear();
            foreach (var x in sample30) CategorySpendingLast30.Add(x);
            MaxAmountLast30 = CategorySpendingLast30.Any() ? CategorySpendingLast30.Max(s => s.Amount) : 1m;
        }

        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class CategorySpend
    {
        public string Name { get; }
        public decimal Amount { get; }
        public CategorySpend(string name, decimal amount) { Name = name; Amount = amount; }
    }
}










