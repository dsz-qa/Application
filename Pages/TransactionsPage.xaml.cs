using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Finly.Services;
using Finly.Services.Features;
using Finly.ViewModels;
using Finly.Views.Controls;
using TransactionCardVm = Finly.ViewModels.TransactionsViewModel.TransactionCardVm;
using TransactionKind = Finly.ViewModels.TransactionsViewModel.TransactionKind;

namespace Finly.Pages
{
    public partial class TransactionsPage : UserControl
    {
        private readonly TransactionsViewModel _vm;
        private PeriodBarControl? _periodBar;
        private int _uid;
        private bool _isAlive;

        public TransactionsPage()
        {
            InitializeComponent();

            _vm = new TransactionsViewModel();
            DataContext = _vm;

            Loaded += TransactionsPage_Loaded;
            Unloaded += TransactionsPage_Unloaded;
        }

        // ================== INIT ==================

        private void TransactionsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isAlive) return;
            _isAlive = true;

            // zawsze czyścimy stan na starcie
            _uid = 0;

            int uid = 0;
            try
            {
                uid = UserService.GetCurrentUserId();
            }
            catch
            {
                uid = 0;
            }

            // Jeśli nie ma usera -> NIE zostawiaj strony "Alive", bo potem już nie zainicjalizuje się poprawnie.
            if (uid <= 0)
            {
                _isAlive = false;
                return;
            }

            _uid = uid;

            // VM init – jeśli poleci wyjątek, cofamy Alive żeby kolejny Load mógł spróbować ponownie
            try
            {
                _vm.Initialize(uid);
            }
            catch
            {
                _uid = 0;
                _isAlive = false;
                throw;
            }

            // PeriodBar
            _periodBar = FindName("PeriodBar") as PeriodBarControl;
            if (_periodBar != null)
            {
                _periodBar.RangeChanged -= PeriodBar_RangeChanged;
                _periodBar.RangeChanged += PeriodBar_RangeChanged;

                // ustaw okres startowy po Initialize(uid)
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
            }

            // zasoby/summary – defensywnie (żeby UI nie wywracał się przez błąd w helperach)
            try { LoadEditResources(uid); } catch { /* opcjonalnie log */ }
            try { RefreshMoneySummary(); } catch { /* opcjonalnie log */ }

            // DataChanged – zawsze podpinamy jednokrotnie
            DatabaseService.DataChanged -= DatabaseService_DataChanged;
            DatabaseService.DataChanged += DatabaseService_DataChanged;
        }


        private void TransactionsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isAlive = false;

            try { DatabaseService.DataChanged -= DatabaseService_DataChanged; } catch { }
            try
            {
                if (_periodBar != null)
                    _periodBar.RangeChanged -= PeriodBar_RangeChanged;
            }
            catch { }

            _periodBar = null;
            _uid = 0;
        }

        private void DatabaseService_DataChanged(object? sender, EventArgs e)
        {
            if (!_isAlive) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isAlive) return;

                try
                {
                    _vm.ReloadAll();

                    if (_periodBar != null)
                        _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);

                    RefreshMoneySummary();
                }
                catch
                {
                    // nie blokuj UI
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void LoadEditResources(int uid)
        {
            try
            {
                var cats = DatabaseService.GetCategoriesByUser(uid)
                           ?? new System.Collections.Generic.List<string>();
                Resources["CategoriesForEditRes"] = cats.ToArray();
            }
            catch
            {
                Resources["CategoriesForEditRes"] = Array.Empty<string>();
            }

            try
            {
                var accs = DatabaseService.GetAccounts(uid)?
                               .Select(a => a.AccountName)
                               .ToList()
                           ?? new System.Collections.Generic.List<string>();

                if (!accs.Contains("Wolna gotówka")) accs.Add("Wolna gotówka");
                if (!accs.Contains("Odłożona gotówka")) accs.Add("Odłożona gotówka");

                Resources["AccountsForEditRes"] = accs.ToArray();
            }
            catch
            {
                Resources["AccountsForEditRes"] = new[] { "Wolna gotówka", "Odłożona gotówka" };
            }
        }

        // ================== KPI ==================

        private void SetKpiText(string name, decimal value)
        {
            if (FindName(name) is TextBlock tb)
                tb.Text = value.ToString("N2", CultureInfo.CurrentCulture) + " zł";
        }

        private void RefreshMoneySummary()
        {
            if (_uid <= 0) return;

            try
            {
                var snap = DatabaseService.GetMoneySnapshot(_uid);

                SetKpiText("TotalWealthText", snap.Total);
                SetKpiText("BanksText", snap.Banks);
                SetKpiText("FreeCashDashboardText", snap.Cash);
                SetKpiText("SavedToAllocateText", snap.SavedUnallocated);
                SetKpiText("EnvelopesDashboardText", snap.Envelopes);
                SetKpiText("InvestmentsText", snap.Investments);
            }
            catch
            {
                // nie blokuj UI
            }
        }

        // ================== PERIOD ==================

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            if (_periodBar == null) return;

            try
            {
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
            }
            catch
            {
                // nie blokuj UI
            }
        }

        // ================== DRZEWO WIZUALNE – HELPERY ==================

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            var cur = start;
            while (cur != null && cur is not T)
                cur = VisualTreeHelper.GetParent(cur);

            return cur as T;
        }

        // ================== EDYCJA INLINE ==================

        private void StartEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not TransactionCardVm vm) return;

            try
            {
                vm.IsDeleteConfirmationVisible = false;
                _vm.StartEdit(vm);
            }
            catch { }
        }

        private void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not TransactionCardVm vm) return;

            try
            {
                _vm.SaveEdit(vm);
                RefreshMoneySummary();
            }
            catch { }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TransactionCardVm vm)
            {
                try
                {
                    vm.IsEditing = false;
                    vm.IsDeleteConfirmationVisible = false;
                }
                catch { }
            }
        }

        private void EditDescription_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.SelectAll();
        }

        // ================== BLOKADA AUTO-SCROLL (SKAKANIE) ==================

        private void Root_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            // Blokuj auto-scroll, ale tylko gdy pochodzi z elementów wewnątrz kart transakcji
            if (e.OriginalSource is not DependencyObject src) return;

            // jeśli fokus/próba bring-into-view pochodzi z list transakcji (lewa lub prawa) -> blokuj
            var items = FindAncestor<ItemsControl>(src);
            if (items == RealizedItems || items == PlannedItemsList)
            {
                e.Handled = true;
            }
        }

        // ================== PRZYCISKI "POKAŻ WSZYSTKO" ==================

        private void ShowAllTypes_Click(object sender, RoutedEventArgs e)
        {
            _vm.ShowExpenses = true;
            _vm.ShowIncomes = true;
            _vm.ShowTransfers = true;

            _vm.RefreshData();
        }

        private void ShowAllCategories_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in _vm.Categories)
                c.IsSelected = true;

            _vm.RefreshData();
        }

        private void ShowAllAccounts_Click(object sender, RoutedEventArgs e)
        {
            foreach (var a in _vm.Accounts)
                a.IsSelected = true;

            _vm.RefreshData();
        }

        // ================== WYSZUKIWARKA ==================

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            _vm.SearchQuery = string.Empty;

            if (FindName("SearchBox") is TextBox tb)
                tb.Focus();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _vm.SearchQuery = string.Empty;
                e.Handled = true;

                if (sender is TextBox tb)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
            }
        }
    }

    /// <summary>
    /// Konwerter używany w XAML – string daty yyyy-MM-dd → dd.MM.yyyy.
    /// </summary>
    public sealed class DateStringToPLConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString("dd.MM.yyyy");

            if (DateTime.TryParse(s, out dt))
                return dt.ToString("dd.MM.yyyy");

            return s;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
