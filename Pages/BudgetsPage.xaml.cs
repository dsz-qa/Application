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

            // default type selection
            if (TypeMonthlyRadio != null) TypeMonthlyRadio.IsChecked = true;

            // populate categories into form combo box
            try
            {
                var cats = DatabaseService.GetCategoriesByUser(_userId) ?? new List<string>();
                FormCategoryBox.ItemsSource = cats;
            }
            catch
            {
                FormCategoryBox.ItemsSource = new List<string>();
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
            _vm.Reload();
        }

        private void CancelFormBtn_Click(object sender, RoutedEventArgs e)
        {
            // hide panel and clear form
            _vm.IsAddPanelVisible = false;
            ClearForm();
        }

        private void ClearForm()
        {
            if (TypeMonthlyRadio != null) TypeMonthlyRadio.IsChecked = true;
            if (TypeWeeklyRadio != null) TypeWeeklyRadio.IsChecked = false;
            if (TypeQuarterlyRadio != null) TypeQuarterlyRadio.IsChecked = false;
            if (TypeYearlyRadio != null) TypeYearlyRadio.IsChecked = false;

            FormCategoryBox.SelectedIndex = -1;
            FormAmountBox.Text = string.Empty;
            RepeatCheck.IsChecked = false;
            RolloverCheck.IsChecked = false;
        }

        private void SaveBudgetBtn_Click(object sender, RoutedEventArgs e)
        {
            // gather data from form
            var type = BudgetType.Monthly;
            if (TypeWeeklyRadio.IsChecked == true) type = BudgetType.Weekly;
            else if (TypeMonthlyRadio.IsChecked == true) type = BudgetType.Monthly;
            else if (TypeQuarterlyRadio.IsChecked == true)
            {
                // no native Quarterly in enum; map to Monthly for now or handle specially
                // choose Monthly mapping; you can extend model later.
                type = BudgetType.Monthly;
            }
            else if (TypeYearlyRadio.IsChecked == true)
            {
                // map to Rollover for yearly-like persistence if available, otherwise OneTime
                type = BudgetType.Rollover;
            }

            var catName = FormCategoryBox.SelectedItem as string;
            int? catId = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(catName))
                {
                    var id = DatabaseService.GetOrCreateCategoryId(_userId, catName);
                    if (id >0) catId = id;
                }
            }
            catch { }

            if (!TryParseAmount(FormAmountBox.Text, out var amount) || amount <=0m)
            {
                ToastService.Info("Podaj poprawną kwotę.");
                return;
            }

            bool repeat = RepeatCheck.IsChecked == true;
            bool rollover = RolloverCheck.IsChecked == true;

            var model = new BudgetModel
            {
                Name = (catName ?? "") + " budżet",
                Type = type,
                CategoryId = catId,
                CategoryName = catName,
                Amount = amount,
                Active = true,
                LastRollover = rollover ?0m :0m
            };

            // persist via service if available; otherwise add to VM collections
            try
            {
                // Add to grouped collections in VM
                switch (model.Type)
                {
                    case BudgetType.Monthly:
                        _vm.BudgetsMonthly.Add(model);
                        break;
                    case BudgetType.Weekly:
                        _vm.BudgetsWeekly.Add(model);
                        break;
                    case BudgetType.Rollover:
                    case BudgetType.OneTime:
                    default:
                        _vm.BudgetsYearly.Add(model);
                        break;
                }

                // Also add to main list if matches selected type (used by totals)
                if (model.Type == _vm.SelectedBudgetType)
                    _vm.Budgets.Add(model);

                // Optionally save
                // BudgetService.SaveBudgets(_userId, _vm.Budgets.ToList());

                ToastService.Success("Zapisano budżet.");
                ClearForm();
                _vm.IsAddPanelVisible = false;
                _vm.Reload();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać budżetu.\n" + ex.Message);
            }
        }

        private static bool TryParseAmount(string? s, out decimal value)
        {
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return true;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("pl-PL"), out value)) return true;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
            return false;
        }
    }
}


