using System.Windows.Controls;
using Finly.ViewModels;
using Finly.Services;
using System;
using Finly.Models;
using Finly.Views.Controls;
using System.Windows;
using System.Windows.Media;
using System.Linq;

namespace Finly.Pages
{
    public partial class TransactionsPage : UserControl
    {
        private TransactionsViewModel _vm;
        private PeriodBarControl? _periodBar;
        public TransactionsPage()
        {
            InitializeComponent();
            _vm = new TransactionsViewModel();
            this.DataContext = _vm;
            Loaded += TransactionsPage_Loaded;
        }

        private void TransactionsPage_Loaded(object sender, RoutedEventArgs e)
        {
            int uid = UserService.GetCurrentUserId();
            _vm.Initialize(uid);
            _periodBar = this.FindName("PeriodBar") as PeriodBarControl;
            if (_periodBar != null)
            {
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
                _periodBar.RangeChanged += PeriodBar_RangeChanged;
            }
            // fill combo sources
            try
            {
                var cats = DatabaseService.GetCategoriesByUser(uid) ?? new System.Collections.Generic.List<string>();
                this.Resources["CategoriesForEditRes"] = cats.ToArray();
            }
            catch { this.Resources["CategoriesForEditRes"] = Array.Empty<string>(); }

            try
            {
                var accs = DatabaseService.GetAccounts(uid)?.Select(a => a.AccountName).ToList() ?? new System.Collections.Generic.List<string>();
                accs.Add("Wolna gotówka");
                accs.Add("Odłożona gotówka");
                this.Resources["AccountsForEditRes"] = accs.ToArray();
            }
            catch { this.Resources["AccountsForEditRes"] = new string[] { "Wolna gotówka", "Odłożona gotówka" }; }
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            if (_periodBar != null)
            {
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
            }
        }

        private static T? FindDescendantByName<T>(DependencyObject? start, string name) where T : FrameworkElement
        {
            if (start == null) return null;
            int cnt = VisualTreeHelper.GetChildrenCount(start);
            for (int i = 0; i < cnt; i++)
            {
                if (VisualTreeHelper.GetChild(start, i) is FrameworkElement fe)
                {
                    if (fe is T t && t.Name == name) return t;
                    var deeper = FindDescendantByName<T>(fe, name);
                    if (deeper != null) return deeper;
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
                    if (panel != null) panel.Visibility = Visibility.Collapsed;
                }
            }

            CollapseInside(this.FindName("RealizedItems") as ItemsControl);
            CollapseInside(this.FindName("PlannedItems") as ItemsControl);
        }

        private void ShowDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            HideAllDeletePanels();
            FrameworkElement? container = fe;
            while (container != null && container is not ContentPresenter && container is not Border)
                container = VisualTreeHelper.GetParent(container) as FrameworkElement;
            if (container == null) return;
            var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
            if (panel != null) panel.Visibility = Visibility.Visible;
        }

        private void DeleteConfirmNo_Click(object sender, RoutedEventArgs e) => HideAllDeletePanels();

        private void DeleteConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) { HideAllDeletePanels(); return; }
            if (fe.DataContext is not TransactionCardVm vmItem) { HideAllDeletePanels(); return; }

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
                if (_periodBar != null) _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
                _vm.LoadFromDatabase();
            }
        }

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
            if (sender is TextBox tb) tb.SelectAll();
        }

        private void DateIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            // Find the parent StackPanel that holds DateText, DateEditPanel and DateEditor
            var sp = FindAncestor<StackPanel>(fe);
            if (sp == null) return;
            var dp = FindDescendantByName<DatePicker>(sp, "DateEditor");
            if (dp != null)
            {
                dp.IsDropDownOpen = true;
            }
        }
    }
}

























































































































































