using System.Windows.Controls;
using Finly.ViewModels;
using Finly.Services;
using System;
using Finly.Models;
using Finly.Views.Controls;
using System.Windows;
using System.Windows.Media;

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
            // odszukaj kontrolkę po nazwie nadanej w XAML
            _periodBar = this.FindName("PeriodBar") as PeriodBarControl;
            if (_periodBar != null)
            {
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
                _periodBar.RangeChanged += PeriodBar_RangeChanged;
            }
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            if (_periodBar != null)
            {
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
            }
        }

        // ===================== Inline delete confirm (like Dashboard) =====================
        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                var parent = VisualTreeHelper.GetParent(child);
                if (parent is T typed) return typed;
                child = parent;
            }
            return null;
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

            // hide other panels first
            HideAllDeletePanels();

            // find the container for this item
            FrameworkElement? container = fe;
            while (container != null && container is not ContentPresenter && container is not Border)
            {
                container = VisualTreeHelper.GetParent(container) as FrameworkElement;
            }
            if (container == null) return;

            var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
            if (panel != null)
            {
                panel.Visibility = panel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        private void DeleteConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            HideAllDeletePanels();
        }

        private void DeleteConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            // get bound item from any element within DataTemplate
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
                // reload preserving current period
                if (_periodBar != null)
                    _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
                _vm.LoadFromDatabase();
            }
        }
    }
}




















