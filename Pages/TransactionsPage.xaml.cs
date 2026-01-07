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

        // chroni przed callbackami po Unloaded
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
            if (_isAlive) return; // zabezpieczenie przed podwójnym loadem
            _isAlive = true;

            _uid = 0;

            int uid;
            try { uid = UserService.GetCurrentUserId(); }
            catch { uid = 0; }

            if (uid <= 0)
                return;

            _uid = uid;

            // Inicjalizacja VM
            _vm.Initialize(uid);

            // PeriodBar
            _periodBar = FindName("PeriodBar") as PeriodBarControl;
            if (_periodBar != null)
            {
                _periodBar.RangeChanged -= PeriodBar_RangeChanged; // ważne jeśli WPF z jakiegoś powodu podwiesił
                _periodBar.RangeChanged += PeriodBar_RangeChanged;

                // ustaw okres startowy
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
            }

            // źródła dla ComboBoxów (jeśli nadal trzymasz je w Resources; w nowym XAML i tak bazujesz na VM.Available*)
            LoadEditResources(uid);

            // KPI
            RefreshMoneySummary();

            // auto-refresh po zmianach w bazie
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
            // UWAGA:
            // W Twoim aktualnym XAML ComboBoxy biorą ItemsSource z:
            // DataContext.AvailableCategories / AvailableAccounts,
            // więc te Resources są opcjonalne. Zostawiam je kompatybilnie.
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

        private static T? FindDescendantByName<T>(DependencyObject? start, string name)
            where T : FrameworkElement
        {
            if (start == null) return null;

            int cnt = VisualTreeHelper.GetChildrenCount(start);
            for (int i = 0; i < cnt; i++)
            {
                var child = VisualTreeHelper.GetChild(start, i);

                if (child is FrameworkElement fe)
                {
                    if (fe is T t && t.Name == name)
                        return t;

                    var deeper = FindDescendantByName<T>(fe, name);
                    if (deeper != null)
                        return deeper;
                }
                else
                {
                    var deeper = FindDescendantByName<T>(child, name);
                    if (deeper != null)
                        return deeper;
                }
            }

            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            var cur = start;
            while (cur != null && cur is not T)
                cur = VisualTreeHelper.GetParent(cur);

            return cur as T;
        }

        // ================== USUWANIE ==================
        // W nowym XAML usuwanie jest przez IsDeleteConfirmationVisible + Commandy w VM.
        // Zostawiamy kompatybilnie metody oparte o "DeleteConfirmPanel",
        // ale jeśli panel nie istnieje, to nic się nie stanie.

        private void HideAllDeletePanels()
        {
            void CollapseInside(ItemsControl? ic)
            {
                if (ic == null) return;

                foreach (var item in ic.Items)
                {
                    var container = ic.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                    if (container == null) continue;

                    var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
                    if (panel != null)
                        panel.Visibility = Visibility.Collapsed;
                }
            }

            CollapseInside(FindName("RealizedItems") as ItemsControl);
            CollapseInside(FindName("PlannedItemsList") as ItemsControl);
        }

        private void ShowDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            HideAllDeletePanels();

            FrameworkElement? container = fe;
            while (container != null &&
                   container is not ContentPresenter &&
                   container is not Border)
            {
                container = VisualTreeHelper.GetParent(container) as FrameworkElement;
            }

            if (container == null) return;

            var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
            if (panel != null)
                panel.Visibility = Visibility.Visible;
        }

        private void DeleteConfirmNo_Click(object sender, RoutedEventArgs e)
            => HideAllDeletePanels();

        private void DeleteConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe)
            {
                HideAllDeletePanels();
                return;
            }

            if (fe.DataContext is not TransactionCardVm vmItem)
            {
                HideAllDeletePanels();
                return;
            }

            try
            {
                // Jedyny właściciel księgowania/usuwania
                TransactionsFacadeService.DeleteTransaction(vmItem.Id);

                switch (vmItem.Kind)
                {
                    case TransactionKind.Expense:
                        ToastService.Success("Usunięto wydatek.");
                        break;
                    case TransactionKind.Income:
                        ToastService.Success("Usunięto przychód.");
                        break;
                    case TransactionKind.Transfer:
                        ToastService.Success("Usunięto transfer.");
                        break;
                }
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd usuwania.\n" + ex.Message);
            }
            finally
            {
                HideAllDeletePanels();

                try
                {
                    if (_periodBar != null)
                        _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);

                    // VM sam czyta z DB – odśwież listy
                    _vm.LoadFromDatabase();

                    RefreshMoneySummary();
                }
                catch
                {
                    // nie blokuj UI
                }
            }
        }

        // ================== EDYCJA INLINE ==================

        private void StartEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TransactionCardVm vm)
            {
                try
                {
                    // zamknij ewentualne potwierdzenia usuwania
                    vm.IsDeleteConfirmationVisible = false;

                    _vm.StartEdit(vm);
                }
                catch { }
            }
        }

        private void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TransactionCardVm vm)
            {
                try
                {
                    _vm.SaveEdit(vm);

                    // Jeżeli Update*/Ledger nie wywoła DataChanged, to KPI i tak się odświeży:
                    RefreshMoneySummary();
                }
                catch { }
            }
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

        private void EditAmount_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                // lepszy UX niż Clear(): pozwala nadpisać jednym wpisem,
                // ale nie kasuje wartości, jeśli user tylko kliknął.
                tb.SelectAll();

                // Jeśli jednak chcesz “zawsze czyść”, odkomentuj:
                // tb.Clear();
            }
        }

        private void EditDescription_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.SelectAll();
        }

        private void DateIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            var sp = FindAncestor<StackPanel>(fe) ?? FindAncestor<StackPanel>(VisualTreeHelper.GetParent(fe));
            if (sp == null) return;

            var dp = FindDescendantByName<DatePicker>(sp, "DateEditor");
            if (dp != null)
                dp.IsDropDownOpen = true;
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
        public object? Convert(object value,
                               Type targetType,
                               object parameter,
                               CultureInfo culture)
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

        public object ConvertBack(object value,
                                  Type targetType,
                                  object parameter,
                                  CultureInfo culture)
            => Binding.DoNothing;
    }
}
