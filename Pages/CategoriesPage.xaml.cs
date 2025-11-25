using Finly.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Finly.Views.Controls;

namespace Finly.Pages
{
    public partial class CategoriesPage : UserControl
    {
        private readonly int _uid;
        private readonly ObservableCollection<CategoryVm> _categories = new();

        private CollectionView _categoriesView;

        // editing state
        private CategoryVm? _editingVm;

        public CategoriesPage() : this(UserService.GetCurrentUserId()) { }

        public CategoriesPage(int userId)
        {
            InitializeComponent();
            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            CategoriesList.ItemsSource = _categories;

            Loaded += CategoriesPage_Loaded;

            _categoriesView = (CollectionView)CollectionViewSource.GetDefaultView(_categories);

            // PeriodBar from XAML
            PeriodBar.RangeChanged += PeriodBar_RangeChanged;

            // subscribe to donut clicks
            SpendingChart.SliceClicked += SpendingChart_SliceClicked;
            IncomeChart.SliceClicked += IncomeChart_SliceClicked;
        }

        private void CategoriesPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureDefaultCategories();
                LoadCategories();
                LoadCharts();
            }
            catch (Exception ex)
            {
                MessageText.Text = "Błąd ładowania kategorii: " + ex.Message;
            }
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            LoadCharts();
        }

        // === Lista kategorii ===

        private void LoadCategories()
        {
            _categories.Clear();

            var list = DatabaseService.GetCategoriesByUser(_uid) ?? new List<string>();
            foreach (var name in list)
            {
                _categories.Add(new CategoryVm { Name = name, ColorBrush = GetBrushForName(name) });
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // mogą przychodzić zdarzenia podczas InitializeComponent zanim _categoriesView będzie ustawione
            if (_categoriesView == null) return;

            var txt = SearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(txt))
            {
                _categoriesView.Filter = null;
            }
            else
            {
                _categoriesView.Filter = obj =>
                {
                    if (obj is CategoryVm vm)
                    {
                        var name = vm.Name ?? string.Empty; // zabezpieczenie przed null
                        return name.IndexOf(txt, StringComparison.CurrentCultureIgnoreCase) >= 0;
                    }
                    return false;
                };
            }
        }

        private void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            var name = (NewCategoryBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageText.Text = "Podaj nazwę kategorii.";
                return;
            }

            try
            {
                // metoda istnieje w projekcie (używana wcześniej)
                DatabaseService.GetOrCreateCategoryId(_uid, name);
                NewCategoryBox.Text = "";
                MessageText.Text = "Kategoria dodana.";
                LoadCategories();
                LoadCharts();
            }
            catch (Exception ex)
            {
                MessageText.Text = "Błąd dodawania kategorii: " + ex.Message;
            }
        }

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
            {
                _editingVm = vm;
                EditNameBox.Text = vm.Name;
                EditPanel.Visibility = Visibility.Visible;
                EditPanelMessage.Text = string.Empty;
            }
        }

        // inline delete confirmation handlers
        private void ShowDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CategoryVm vm)
            {
                // find the DeleteConfirmPanel within the visual tree of this item
                var container = FindAncestor<ContentPresenter>(fe);
                if (container == null) return;

                var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
                if (panel != null)
                {
                    panel.Visibility = Visibility.Visible;
                    // store vm reference in Tag for confirm button to use
                    panel.Tag = vm;
                }
            }
        }

        private void DeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
            {
                TryDeleteCategory(vm);
            }
            else if (sender is FrameworkElement fe)
            {
                var container = FindAncestor<ContentPresenter>(fe);
                if (container == null) return;
                var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
                if (panel != null && panel.Tag is CategoryVm vm2)
                {
                    TryDeleteCategory(vm2);
                }
            }
        }

        private void DeleteCancel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var container = FindAncestor<ContentPresenter>(fe);
                if (container == null) return;
                var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
                if (panel != null)
                    panel.Visibility = Visibility.Collapsed;
            }
        }

        private void TryDeleteCategory(CategoryVm vm)
        {
            try
            {
                var dsType = typeof(DatabaseService);
                var safeMethod = dsType.GetMethod("DeleteCategorySafe");
                if (safeMethod != null)
                {
                    safeMethod.Invoke(null, new object[] { _uid, vm.Name });
                }
                else
                {
                    var id = DatabaseService.GetCategoryIdByName(_uid, vm.Name);
                    if (id.HasValue)
                        DatabaseService.DeleteCategory(id.Value);
                }

                MessageText.Text = "Kategoria usunięta.";
                LoadCategories();
                LoadCharts();
            }
            catch (Exception ex)
            {
                MessageText.Text = "Błąd usuwania: " + ex.Message;
            }
        }

        private void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_editingVm == null) return;

            var newName = (EditNameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                EditPanelMessage.Text = "Podaj nazwę.";
                return;
            }

            try
            {
                // create or get id for new name
                DatabaseService.GetOrCreateCategoryId(_uid, newName);
                EditPanelMessage.Text = "Zapisano.";
                EditPanel.Visibility = Visibility.Collapsed;
                _editingVm = null;
                LoadCategories();
                LoadCharts();
            }
            catch (Exception ex)
            {
                EditPanelMessage.Text = "Błąd zapisu: " + ex.Message;
            }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            _editingVm = null;
            EditPanel.Visibility = Visibility.Collapsed;
            EditPanelMessage.Text = string.Empty;
        }

        private void DeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
            {
                var yes = MessageBox.Show($"Czy na pewno usunąć kategorię \"{vm.Name}\"? (operacja może wpłynąć na dane)", "Usuń kategorię", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (yes != MessageBoxResult.Yes) return;

                TryDeleteCategory(vm);
            }
        }

        // === Wykresy ===

        private void LoadCharts()
        {
            LoadSpendingChart();
            LoadIncomeChart();
        }

        private void LoadSpendingChart()
        {
            // prepare donut using Dashboard's PieSlice helper
            // SpendingDonut.ItemsSource = null;

            DateTime from = PeriodBar.StartDate;
            DateTime to = PeriodBar.EndDate;
            if (from > to) (from, to) = (to, from);

            var data = DatabaseService.GetSpendingByCategorySafe(_uid, from, to) ?? new List<DatabaseService.CategoryAmountDto>();
            if (!data.Any())
            {
                PieCenterNameText.Text = "Brak danych";
                PieCenterValueText.Text = "0,00 zł";
                PieCenterPercentText.Text = string.Empty;
                // clear charts
                SpendingChart.Draw(new Dictionary<string, decimal>(), 0, new Brush[0]);
                return;
            }

            var list = data.Where(x => x.Amount > 0).OrderByDescending(x => x.Amount).ToList();
            var sum = list.Sum(x => x.Amount);
            if (sum <= 0)
            {
                PieCenterNameText.Text = "Brak danych";
                PieCenterValueText.Text = "0,00 zł";
                PieCenterPercentText.Text = string.Empty;
                SpendingChart.Draw(new Dictionary<string, decimal>(), 0, new Brush[0]);
                return;
            }

            var palette = new[] { "#FFED7A1A", "#FF3FA7D6", "#FF7BC96F", "#FFAF7AC5", "#FFF6BF26", "#FF56C1A7", "#FFCE6A6B", "#FF9AA0A6" };
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int idx = 0;

            var totals = new Dictionary<string, decimal>();
            foreach (var item in list)
            {
                totals[item.Name] = item.Amount;
            }

            var brushes = totals.Keys.Select((k, i) => (Brush)(new BrushConverter().ConvertFromString(palette[i % palette.Length])!)).ToArray();

            SpendingChart.Draw(totals, sum, brushes);

            PieCenterNameText.Text = "Wszystko";
            PieCenterValueText.Text = sum.ToString("N2") + " zł";
            PieCenterPercentText.Text = string.Empty;
        }

        private void LoadIncomeChart()
        {
            // IncomeDonut.ItemsSource = null;

            DateTime from = PeriodBar.StartDate;
            DateTime to = PeriodBar.EndDate;
            if (from > to) (from, to) = (to, from);

            var data = DatabaseService.GetIncomeBySourceSafe(_uid, from, to) ?? new List<DatabaseService.CategoryAmountDto>();
            var list = data.Where(x => x.Amount > 0).OrderByDescending(x => x.Amount).ToList();
            var sum = list.Sum(x => x.Amount);

            if (sum <= 0)
            {
                IncomeCenterNameText.Text = "Brak danych";
                IncomeCenterValueText.Text = "0,00 zł";
                IncomeCenterPercentText.Text = string.Empty;
                IncomeChart.Draw(new Dictionary<string, decimal>(), 0, new Brush[0]);
                return;
            }

            var palette = new[] { "#FFED7A1A", "#FF3FA7D6", "#FF7BC96F", "#FFAF7AC5", "#FFF6BF26", "#FF56C1A7", "#FFCE6A6B", "#FF9AA0A6" };
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int idx = 0;
            var totals = new Dictionary<string, decimal>();
            foreach (var item in list)
            {
                totals[item.Name] = item.Amount;
            }
            var brushes = totals.Keys.Select((k, i) => (Brush)(new BrushConverter().ConvertFromString(palette[i % palette.Length])!)).ToArray();

            IncomeChart.Draw(totals, sum, brushes);

            IncomeCenterNameText.Text = "Wszystko";
            IncomeCenterValueText.Text = sum.ToString("N2") + " zł";
            IncomeCenterPercentText.Text = string.Empty;
        }

        private void SpendingChart_SliceClicked(object? sender, SliceClickedEventArgs e)
        {
            if (e == null) return;
            var total = DatabaseService.GetSpendingByCategorySafe(_uid, PeriodBar.StartDate, PeriodBar.EndDate)?.Sum(x => x.Amount) ?? 0m;
            if (total <= 0) return;
            var share = e.Amount / total * 100m;
            PieCenterNameText.Text = e.Name;
            PieCenterValueText.Text = e.Amount.ToString("N2") + " zł";
            PieCenterPercentText.Text = $"{share:N1}% udziału";

            // filter left list to this category
            _categoriesView.Filter = obj => (obj as CategoryVm)?.Name == e.Name;
        }

        private void IncomeChart_SliceClicked(object? sender, SliceClickedEventArgs e)
        {
            if (e == null) return;
            var total = DatabaseService.GetIncomeBySourceSafe(_uid, PeriodBar.StartDate, PeriodBar.EndDate)?.Sum(x => x.Amount) ?? 0m;
            if (total <= 0) return;
            var share = e.Amount / total * 100m;
            IncomeCenterNameText.Text = e.Name;
            IncomeCenterValueText.Text = e.Amount.ToString("N2") + " zł";
            IncomeCenterPercentText.Text = $"{share:N1}% udziału";
        }

        // === Domyślne kategorie ===

        private void EnsureDefaultCategories()
        {
            try
            {
                var existing = DatabaseService.GetCategoriesByUser(_uid) ?? new List<string>();

                var defaultExpenses = new[] { "Jedzenie", "Transport", "Mieszkanie", "Rachunki", "Rozrywka", "Zdrowie", "Ubrania" };
                var defaultIncomes = new[] { "Wynagrodzenie", "Prezent", "Zwrot", "Inne" };

                foreach (var c in defaultExpenses.Concat(defaultIncomes))
                {
                    if (!existing.Any(e => string.Equals(e, c, StringComparison.OrdinalIgnoreCase)))
                    {
                        DatabaseService.GetOrCreateCategoryId(_uid, c);
                    }
                }
            }
            catch
            {
                // nie przerywamy ładowania, ewentualne błędy zostaną pokazane przy późniejszych operacjach
            }
        }

        // === Pomocnicze ===

        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(start);
            while (parent != null)
            {
                if (parent is T t) return t;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private static T? FindDescendantByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == name) return fe;
                var res = FindDescendantByName<T>(child, name);
                if (res != null) return res;
            }
            return null;
        }

        private Brush GetBrushForName(string name) => GetRandomBrush(name);

        private static Brush GetRandomBrush(string seed)
        {
            var palette = new[]
            {
                "#FFED7A1A","#FF3FA7D6","#FF7BC96F","#FFAF7AC5","#FFF6BF26","#FF56C1A7","#FFCE6A6B","#FF9AA0A6"
            };
            var idx = Math.Abs(seed?.GetHashCode() ?? 0) % palette.Length;
            return (Brush)(new BrushConverter().ConvertFromString(palette[idx])!);
        }

        private class CategoryVm
        {
            public string Name { get; set; } = "";
            public Brush ColorBrush { get; set; } = Brushes.Gray;
        }

        private class PieSliceModel
        {
            public string Name { get; set; } = "";
            public decimal Amount { get; set; }
            public Brush Brush { get; set; } = Brushes.Gray;
            public Geometry Geometry { get; set; } = Geometry.Empty;
        }
    }
}