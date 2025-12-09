using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Finly.Models;
using Finly.Services;
using Finly.ViewModels;
using Finly.Views.Controls;

namespace Finly.Pages
{
    public partial class TransactionsPage : UserControl
    {
        private readonly TransactionsViewModel _vm;
        private PeriodBarControl? _periodBar;

        public TransactionsPage()
        {
            InitializeComponent();

            _vm = new TransactionsViewModel();
            DataContext = _vm;

            Loaded += TransactionsPage_Loaded;
        }

        // ================== INIT ==================

        private void TransactionsPage_Loaded(object sender, RoutedEventArgs e)
        {
            int uid = UserService.GetCurrentUserId();

            _vm.Initialize(uid);

            _periodBar = FindName("PeriodBar") as PeriodBarControl;
            if (_periodBar != null)
            {
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
                _periodBar.RangeChanged += PeriodBar_RangeChanged;
            }

            // źródła dla ComboBoxów w trybie edycji
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

                accs.Add("Wolna gotówka");
                accs.Add("Odłożona gotówka");

                Resources["AccountsForEditRes"] = accs.ToArray();
            }
            catch
            {
                Resources["AccountsForEditRes"] = new[] { "Wolna gotówka", "Odłożona gotówka" };
            }
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            if (_periodBar == null) return;

            _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
        }

        // ================== DRZEWO WIZUALNE – HELPERY ==================

        private static T? FindDescendantByName<T>(DependencyObject? start, string name)
            where T : FrameworkElement
        {
            if (start == null) return null;

            int cnt = VisualTreeHelper.GetChildrenCount(start);
            for (int i = 0; i < cnt; i++)
            {
                if (VisualTreeHelper.GetChild(start, i) is FrameworkElement fe)
                {
                    if (fe is T t && t.Name == name)
                        return t;

                    var deeper = FindDescendantByName<T>(fe, name);
                    if (deeper != null)
                        return deeper;
                }
            }

            return null;
        }

        private T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            var cur = start;
            while (cur != null && cur is not T)
            {
                cur = VisualTreeHelper.GetParent(cur);
            }

            return cur as T;
        }

        // ================== USUWANIE ==================

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
            // Updated name to match XAML change
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
                switch (vmItem.Kind)
                {
                    case TransactionKind.Expense:
                        DatabaseService.DeleteExpense(vmItem.Id);
                        ToastService.Success("Usunięto wydatek.");
                        break;

                    case TransactionKind.Income:
                        DatabaseService.DeleteIncome(vmItem.Id);
                        ToastService.Success("Usunięto przychód.");
                        break;

                    case TransactionKind.Transfer:
                        ToastService.Info("Transfer usuń poprzez powiązane wpisy.");
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

                if (_periodBar != null)
                    _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);

                _vm.LoadFromDatabase();
            }
        }

        // ================== EDYCJA INLINE ==================

        private void StartEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TransactionCardVm vm)
            {
                _vm.StartEdit(vm);
            }
        }

        private void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TransactionCardVm vm)
            {
                _vm.SaveEdit(vm);
            }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TransactionCardVm vm)
            {
                // Cancel inline edit without saving; return to read mode
                vm.IsEditing = false;
            }
        }

        private void EditAmount_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                // wyczyść, aby ułatwić wpisanie nowej kwoty
                tb.Clear();
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

            // znajdź kontener (StackPanel) z ukrytym DatePickerem "DateEditor"
            var sp = FindAncestor<StackPanel>(fe);
            if (sp == null)
            {
                // fallback: szukaj poziom wyżej
                sp = FindAncestor<StackPanel>(VisualTreeHelper.GetParent(fe));
            }
            if (sp == null) return;

            var dp = FindDescendantByName<DatePicker>(sp, "DateEditor");
            if (dp != null)
            {
                dp.IsDropDownOpen = true;
            }
        }

        // ================== PRZYCISKI "POKAŻ WSZYSTKO" ==================

        private void ShowAllTypes_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            _vm.ShowExpenses = true;
            _vm.ShowIncomes = true;
            _vm.ShowTransfers = true;

            _vm.RefreshData();
        }

        private void ShowAllCategories_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            foreach (var c in _vm.Categories)
                c.IsSelected = true;

            _vm.RefreshData();
        }

        private void ShowAllAccounts_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            foreach (var a in _vm.Accounts)
                a.IsSelected = true;

            _vm.RefreshData();
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

            if (DateTime.TryParse(s,
                                  CultureInfo.InvariantCulture,
                                  DateTimeStyles.None,
                                  out var dt))
            {
                return dt.ToString("dd.MM.yyyy");
            }

            // fallback – kultura systemowa
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
