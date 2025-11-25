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
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Finly.Pages
{
    public partial class CategoriesPage : UserControl
    {
        private readonly int _uid;
        private readonly ObservableCollection<CategoryVm> _categories = new();
        private readonly ObservableCollection<ChartBarItem> _spendingBars = new();
        private readonly ObservableCollection<ChartBarItem> _incomeBars = new();

        private CollectionView _categoriesView;

        // editing state
        private CategoryVm? _editingVm;

        public CategoriesPage() : this(UserService.GetCurrentUserId()) { }

        public CategoriesPage(int userId)
        {
            InitializeComponent();
            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            CategoriesList.ItemsSource = _categories;
            SpendingChart.ItemsSource = _spendingBars;
            IncomeChart.ItemsSource = _incomeBars;

            Loaded += CategoriesPage_Loaded;

            _categoriesView = (CollectionView)CollectionViewSource.GetDefaultView(_categories);
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
                try
                {
                    // próbuj użyć bezpiecznej metody jeśli istnieje
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
            else if (sender is FrameworkElement fe)
            {
                // maybe Tag stored on parent panel
                var parent = FindAncestor<Border>(fe);
                if (parent != null && parent.Tag is CategoryVm vm2)
                {
                    try
                    {
                        var dsType = typeof(DatabaseService);
                        var safeMethod = dsType.GetMethod("DeleteCategorySafe");
                        if (safeMethod != null)
                        {
                            safeMethod.Invoke(null, new object[] { _uid, vm2.Name });
                        }
                        else
                        {
                            var id = DatabaseService.GetCategoryIdByName(_uid, vm2.Name);
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

                try
                {
                    // próbuj użyć bezpiecznej metody jeśli istnieje
                    var dsType = typeof(DatabaseService);
                    var safeMethod = dsType.GetMethod("DeleteCategorySafe");
                    if (safeMethod != null)
                    {
                        safeMethod.Invoke(null, new object[] { _uid, vm.Name });
                    }
                    else
                    {
                        // fallback: usuń po Id
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
        }

        // === Wykresy ===

        private void LoadCharts()
        {
            LoadSpendingChart();
            LoadIncomeChart();
        }

        private void LoadSpendingChart()
        {
            _spendingBars.Clear();

            // ostatnie 30 dni
            var from = DateTime.Today.AddDays(-30);
            var to = DateTime.Today;

            var data = DatabaseService.GetSpendingByCategorySafe(_uid, from, to) ?? new List<DatabaseService.CategoryAmountDto>();
            if (!data.Any()) return;

            var max = data.Max(x => x.Amount);
            foreach (var d in data.OrderByDescending(x => x.Amount))
            {
                _spendingBars.Add(new ChartBarItem
                {
                    Name = d.Name,
                    Amount = d.Amount,
                    Brush = GetRandomBrush(d.Name),
                    BarWidth = (max <= 0) ? 0 : (200.0 * (double)(d.Amount / max))
                });
            }
        }

        private void LoadIncomeChart()
        {
            _incomeBars.Clear();

            // ostatnie 30 dni
            var from = DateTime.Today.AddDays(-30);
            var to = DateTime.Today;

            // użyjemy bezpiecznej metody (jeśli nie ma dokładnej metody wg kategorii, użyjemy przychodów wg źródła)
            var data = DatabaseService.GetIncomeBySourceSafe(_uid, from, to) ?? new List<DatabaseService.CategoryAmountDto>();
            if (!data.Any()) return;

            var max = data.Max(x => x.Amount);
            foreach (var d in data.OrderByDescending(x => x.Amount))
            {
                _incomeBars.Add(new ChartBarItem
                {
                    Name = d.Name,
                    Amount = d.Amount,
                    Brush = GetRandomBrush(d.Name),
                    BarWidth = (max <= 0) ? 0 : (200.0 * (double)(d.Amount / max))
                });
            }
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

        private Brush GetBrushForName(string name)
        {
            return GetRandomBrush(name);
        }

        private static Brush GetRandomBrush(string seed)
        {
            // deterministyczny dobór koloru na podstawie nazwy
            var palette = new[]
            {
                "#FFED7A1A","#FF3FA7D6","#FF7BC96F","#FFAF7AC5","#FFF6BF26","#FF56C1A7","#FFCE6A6B","#FF9AA0A6"
            };
            var idx = Math.Abs(seed?.GetHashCode() ?? 0) % palette.Length;
            return (Brush)(new BrushConverter().ConvertFromString(palette[idx])!);
        }

        // visual helpers
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

        private static T? FindAncestor<T>(FrameworkElement start) where T : FrameworkElement
        {
            var parent = start.Parent as DependencyObject;
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

        private class CategoryVm
        {
            public string Name { get; set; } = "";
            public Brush ColorBrush { get; set; } = Brushes.Gray;
        }

        private class ChartBarItem
        {
            public string Name { get; set; } = "";
            public decimal Amount { get; set; }
            public Brush Brush { get; set; } = Brushes.Gray;
            public double BarWidth { get; set; } = 0.0;
        }
    }
}