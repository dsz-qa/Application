using Finly.Services;
using Finly.ViewModels;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Finly.Models; // use models namespace for BudgetModel and BudgetType

namespace Finly.Pages
{
    public partial class BudgetsPage : UserControl
    {
        private BudgetsViewModel _vm = new BudgetsViewModel();
        private int _userId;
        private List<BudgetModel> _budgets = new List<BudgetModel>();
        private readonly Brush _ok = new SolidColorBrush(Color.FromRgb(0x4C,0xAF,0x50));
        private readonly Brush _warn = new SolidColorBrush(Color.FromRgb(0xFF,0xA0,0x00));
        private readonly Brush _danger = new SolidColorBrush(Color.FromRgb(0xF4,0x43,0x36));

        public BudgetsPage()
        {
            InitializeComponent();
            Loaded += BudgetsPage_Loaded;
        }

        private void BudgetsPage_Loaded(object? sender, RoutedEventArgs e)
        {
            _userId = _userId >0 ? _userId : UserService.GetCurrentUserId();
            _vm.Initialize(_userId);
            DataContext = _vm;
            // Removed old BudgetPeriodBar wiring (control no longer exists)

            // Hook budget type selector RadioButtons inside the right StackPanel
            HookBudgetTypeSelector();
        }

        private void HookBudgetTypeSelector()
        {
            // find radio buttons by content
            foreach (var rb in FindVisualChildren<RadioButton>(this))
            {
                var text = rb.Content?.ToString()?.Trim();
                if (text == "Miesięczny") rb.Checked += (s, e) => _vm.SelectedBudgetType = BudgetType.Monthly;
                else if (text == "Tygodniowy") rb.Checked += (s, e) => _vm.SelectedBudgetType = BudgetType.Weekly;
                else if (text == "Koperty") rb.Checked += (s, e) => _vm.SelectedBudgetType = BudgetType.Rollover;
                else if (text == "Własny") rb.Checked += (s, e) => _vm.SelectedBudgetType = BudgetType.OneTime;
            }
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i =0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T t) yield return t;
                foreach (T childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
            }
        }

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            // TODO: implement add budget logic (previous logic removed after refactor)
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            // TODO: implement refresh logic
        }
    }
}


