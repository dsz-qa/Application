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
using System.Windows.Shapes;

namespace Finly.Pages
{
    public partial class CategoriesPage : UserControl, INotifyPropertyChanged
    {
        private readonly int _uid;
        private readonly ObservableCollection<CategoryVm> _categories = new();
        public ObservableCollection<CategoryVm> Categories => _categories;

        private CollectionView _categoriesView;

        // filtering state
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value ?? string.Empty;
                    RaisePropertyChanged(nameof(SearchText));
                    // hide any open delete confirmations when user types
                    ClearAllDeleteConfirmPanels();
                    ApplyCategoriesFilter();
                }
            }
        }

        // editing state
        private CategoryVm? _editingVm;

        // selected category state
        private CategoryVm? _selectedCategory;
        private readonly ObservableCollection<CategoryTransactionRow> _lastTransactions = new();
        private string _selectedColorHex = string.Empty; // used for edit and add panels
        private string? _categoryDescriptionDraft;
        private string? _selectedIcon;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void RaisePropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // KPI refs
        private TextBlock? _mostUsedName;
        private TextBlock? _mostUsedCount;
        private Rectangle? _mostUsedColor;

        private TextBlock? _mostExpName;
        private TextBlock? _mostExpAmount;
        private ProgressBar? _mostExpPercentBar;
        private TextBlock? _mostExpPercentText;

        private TextBlock? _totalCats;
        private TextBlock? _activeCats;
        private TextBlock? _inactiveCats;

        public CategoriesPage() : this(UserService.GetCurrentUserId()) { }

        public CategoriesPage(int userId)
        {
            InitializeComponent();
            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            DataContext = this; // expose Categories to XAML via this

            Loaded += CategoriesPage_Loaded;

            _categoriesView = (CollectionView)CollectionViewSource.GetDefaultView(_categories);
            _categoriesView.Filter = CategoryFilter;

            PeriodBar.RangeChanged += PeriodBar_RangeChanged;

            LastTransactionsList.ItemsSource = _lastTransactions;
        }

        private bool CategoryFilter(object obj)
        {
            if (obj is not CategoryVm vm) return false;
            var q = (SearchText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(q)) return true;
            var cmp = StringComparison.CurrentCultureIgnoreCase;
            return (vm.Name?.IndexOf(q, cmp) >= 0) || (vm.Description?.IndexOf(q, cmp) >= 0);
        }

        private void ApplyCategoriesFilter()
        {
            try { _categoriesView.Refresh(); } catch { }
        }

        private void EnsureKpiRefs()
        {
            _mostUsedName ??= FindName("MostUsedCategoryNameText") as TextBlock;
            _mostUsedCount ??= FindName("MostUsedCountText") as TextBlock;
            _mostUsedColor ??= FindName("MostUsedColorRect") as Rectangle;

            _mostExpName ??= FindName("MostExpensiveNameText") as TextBlock;
            _mostExpAmount ??= FindName("MostExpensiveAmountText") as TextBlock;
            _mostExpPercentBar ??= FindName("MostExpensivePercentBar") as ProgressBar;
            _mostExpPercentText ??= FindName("MostExpensivePercentText") as TextBlock;

            _totalCats ??= FindName("TotalCategoriesText") as TextBlock;
            _activeCats ??= FindName("ActiveCategoriesText") as TextBlock;
            _inactiveCats ??= FindName("InactiveCategoriesText") as TextBlock;
        }

        private void CategoriesPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureKpiRefs();
                EnsureDefaultCategories();
                LoadCategories();
                UpdateCategoryKpis();
                // ensure no delete confirmation is visible on load
                ClearAllDeleteConfirmPanels();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd ładowania kategorii: " + ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            EnsureKpiRefs();
            UpdateCategoryKpis();
            if (_selectedCategory != null) LoadSelectedCategoryDetails();
        }

        private void UpdateCategoryKpis()
        {
            EnsureKpiRefs();
            try
            {
                DateTime from = PeriodBar.StartDate;
                DateTime to = PeriodBar.EndDate;
                if (from > to) (from, to) = (to, from);

                var dt = DatabaseService.GetExpenses(_uid, from, to, null, null, null);

                var counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
                var sums = new Dictionary<string, decimal>(StringComparer.CurrentCultureIgnoreCase);
                decimal totalSum = 0m;

                foreach (System.Data.DataRow row in dt.Rows)
                {
                    try
                    {
                        var name = (row[6]?.ToString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(name) || name == "(brak)") continue;
                        decimal amount = 0m;
                        var val = row[3];
                        if (val is decimal dec) amount = dec; else if (val is double d) amount = (decimal)d; else amount = Convert.ToDecimal(val);
                        amount = Math.Abs(amount);

                        counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
                        sums[name] = sums.TryGetValue(name, out var s) ? s + amount : amount;
                        totalSum += amount;
                    }
                    catch { }
                }

                if (counts.Count > 0)
                {
                    var mostUsed = counts.OrderByDescending(kv => kv.Value).First();
                    _mostUsedName!.Text = mostUsed.Key;
                    _mostUsedCount!.Text = $"Liczba transakcji: {mostUsed.Value}";
                    _mostUsedColor!.Fill = GetBrushForName(mostUsed.Key);
                }
                else
                {
                    if (_mostUsedName != null) _mostUsedName.Text = "Brak danych dla wybranego okresu";
                    if (_mostUsedCount != null) _mostUsedCount.Text = string.Empty;
                    if (_mostUsedColor != null) _mostUsedColor.Fill = Brushes.Transparent;
                }

                if (sums.Count > 0)
                {
                    var mostExp = sums.OrderByDescending(kv => kv.Value).First();
                    if (_mostExpName != null) _mostExpName.Text = mostExp.Key;
                    if (_mostExpAmount != null) _mostExpAmount.Text = mostExp.Value.ToString("N2") + " zł";
                    var pct = totalSum > 0m ? (double)(mostExp.Value / totalSum * 100m) : 0.0;
                    if (_mostExpPercentBar != null) _mostExpPercentBar.Value = pct;
                    if (_mostExpPercentText != null) _mostExpPercentText.Text = Math.Round(pct, 0) + "% wszystkich wydatków";
                }
                else
                {
                    if (_mostExpName != null) _mostExpName.Text = "Brak danych dla wybranego okresu";
                    if (_mostExpAmount != null) _mostExpAmount.Text = string.Empty;
                    if (_mostExpPercentBar != null) _mostExpPercentBar.Value = 0;
                    if (_mostExpPercentText != null) _mostExpPercentText.Text = string.Empty;
                }

                var allCats = DatabaseService.GetCategoriesByUser(_uid) ?? new List<string>();
                int total = allCats.Count;
                var activeSet = new HashSet<string>(sums.Where(kv => kv.Value > 0).Select(kv => kv.Key), StringComparer.CurrentCultureIgnoreCase);
                int active = allCats.Count(c => activeSet.Contains(c));
                int inactive = Math.Max(0, total - active);

                if (_totalCats != null) _totalCats.Text = total.ToString(CultureInfo.CurrentCulture);
                if (_activeCats != null) _activeCats.Text = active.ToString(CultureInfo.CurrentCulture);
                if (_inactiveCats != null) _inactiveCats.Text = inactive.ToString(CultureInfo.CurrentCulture);
            }
            catch { }
        }

        private void LoadCategories()
        {
            _categories.Clear();

            var tuples = DatabaseService.GetCategoriesExtended(_uid);
            foreach (var (id, name, color, icon) in tuples)
            {
                Brush brush = string.IsNullOrWhiteSpace(color) ? GetBrushForName(name) : (Brush)(new BrushConverter().ConvertFromString(color)!);
                _categories.Add(new CategoryVm { Id = id, Name = name, ColorBrush = brush, Icon = icon });
            }

            // Load descriptions for all categories (in one query)
            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT Id, Description FROM Categories WHERE UserId=@u;";
                cmd.Parameters.AddWithValue("@u", _uid);
                using var reader = cmd.ExecuteReader();
                var map = new Dictionary<int, string?>(256);
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);
                    string? desc = reader.IsDBNull(1) ? null : reader.GetString(1);
                    map[id] = desc;
                }
                foreach (var vm in _categories)
                {
                    if (map.TryGetValue(vm.Id, out var d)) vm.Description = d;
                }
            }
            catch { }

            ApplyCategoriesFilter();
            // ensure no stale confirm panel stays visible
            ClearAllDeleteConfirmPanels();
            RaisePropertyChanged(nameof(Categories));
        }

        private void AddCategoryTile_Click(object sender, RoutedEventArgs e)
        {
            // show add panel and reset fields
            AddPanelMessage.Text = string.Empty;
            AddNameBox.Text = string.Empty;
            AddDescriptionBox.Text = string.Empty;
            _selectedColorHex = string.Empty;
            AddPanel.Visibility = Visibility.Visible;
            try { AddPanel.UpdateLayout(); AddPanel.BringIntoView(); } catch { }
        }

        private void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            var name = (AddNameBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AddPanelMessage.Text = "Podaj nazwę kategorii.";
                return;
            }

            try
            {
                int id = DatabaseService.GetOrCreateCategoryId(_uid, name);
                string? colorToSave = string.IsNullOrWhiteSpace(_selectedColorHex) ? null : _selectedColorHex;
                DatabaseService.UpdateCategoryFull(id, _uid, name, colorToSave, null);
                try
                {
                    using var con = DatabaseService.GetConnection();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "UPDATE Categories SET Description=@d WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@d", (object?)AddDescriptionBox.Text ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
                catch { }

                AddPanelMessage.Text = "Dodano.";
                AddNameBox.Text = string.Empty;
                AddDescriptionBox.Text = string.Empty;
                _selectedColorHex = string.Empty;

                LoadCategories();
                UpdateCategoryKpis();
            }
            catch (Exception ex)
            {
                AddPanelMessage.Text = "Błąd dodawania: " + ex.Message;
            }
        }

        private void ClearAddPanel_Click(object sender, RoutedEventArgs e)
        {
            AddNameBox.Text = string.Empty;
            AddDescriptionBox.Text = string.Empty;
            _selectedColorHex = string.Empty;
            AddPanelMessage.Text = string.Empty;
            AddPanel.Visibility = Visibility.Collapsed;
        }

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
            {
                _editingVm = vm;
                _selectedCategory = vm;

                EditNameBox.Text = vm.Name;
                EditDescriptionBox.Text = _categoryDescriptionDraft ?? string.Empty;

                EditPanel.Visibility = Visibility.Visible;
                EditPanelMessage.Text = string.Empty;

                _selectedColorHex = (vm.ColorBrush as SolidColorBrush)?.Color.ToString() ?? string.Empty;
            }
        }

        private void ShowDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CategoryVm vm)
            {
                // show inline confirm panel only for this item
                ClearAllDeleteConfirmPanels();
                vm.IsDeleteConfirmVisible = true;
            }
        }

        private void HideDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
            {
                vm.IsDeleteConfirmVisible = false;
            }
            else
            {
                ClearAllDeleteConfirmPanels();
            }
        }

        private void DeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
            {
                vm.IsDeleteConfirmVisible = false;
                TryDeleteCategory(vm);
            }
        }

        private void TryDeleteCategory(CategoryVm vm)
        {
            try
            {
                var id = DatabaseService.GetCategoryIdByName(_uid, vm.Name);
                if (id.HasValue)
                    DatabaseService.DeleteCategory(id.Value);

                MessageBox.Show("Kategoria usunięta.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadCategories();
                HideCategoryPanels();
                UpdateCategoryKpis();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd usuwania: " + ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
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
                string? colorToSave = null;
                if (!string.IsNullOrWhiteSpace(_selectedColorHex))
                    colorToSave = _selectedColorHex;

                _categoryDescriptionDraft = EditDescriptionBox.Text;

                DatabaseService.UpdateCategoryFull(_editingVm.Id, _uid, newName, colorToSave, _editingVm.Icon);

                try
                {
                    using var con = DatabaseService.GetConnection();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "UPDATE Categories SET Description=@d WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@d", (object?)_categoryDescriptionDraft ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", _editingVm.Id);
                    cmd.ExecuteNonQuery();
                }
                catch { }

                _editingVm.Name = newName;
                if (!string.IsNullOrWhiteSpace(_selectedColorHex))
                {
                    var brush = (Brush)(new BrushConverter().ConvertFromString(_selectedColorHex)!);
                    _editingVm.ColorBrush = brush;
                }

                EditPanelMessage.Text = "Zapisano.";
                EditPanel.Visibility = Visibility.Collapsed;
                _editingVm = null;

                LoadCategories();
                UpdateCategoryKpis();
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

        private void CategoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is CategoryVm vm)
            {
                _selectedCategory = vm;
                ShowCategoryPanels();
                LoadSelectedCategoryDetails();
            }
        }

        private void ShowCategoryPanels()
        {
            SummaryPanel.Visibility = Visibility.Visible;
            TransactionsPanel.Visibility = Visibility.Visible;
        }

        private void HideCategoryPanels()
        {
            SummaryPanel.Visibility = Visibility.Collapsed;
            TransactionsPanel.Visibility = Visibility.Collapsed;
        }

        private void LoadSelectedCategoryDetails()
        {
            if (_selectedCategory == null) return;

            int? catId = DatabaseService.GetCategoryIdByName(_uid, _selectedCategory.Name);
            if (!catId.HasValue)
            {
                HideCategoryPanels();
                return;
            }

            SummaryCategoryNameText.Text = _selectedCategory.Name;

            DateTime from = PeriodBar.StartDate;
            DateTime to = PeriodBar.EndDate;
            if (from > to) (from, to) = (to, from);

            var expensesDt = DatabaseService.GetExpenses(_uid, from, to, catId, null, null);
            decimal sum = 0m;
            int count = 0;
            foreach (System.Data.DataRow row in expensesDt.Rows)
            {
                try
                {
                    sum += Convert.ToDecimal(row[3]);
                    count++;
                }
                catch { }
            }
            SummaryTotalText.Text = "Suma wydatków w tym okresie: " + sum.ToString("N2") + " zł";

            double monthsSpan = Math.Max(1.0, (to - from).TotalDays / 30.0);
            decimal monthlyAvg = monthsSpan <= 0 ? 0 : sum / (decimal)monthsSpan;
            SummaryMonthlyAvgText.Text = "Średni wydatek w tej kategorii (miesiące)): " + monthlyAvg.ToString("N2") + " zł";

            SummaryTxnCountText.Text = count + (count == 1 ? " transakcja" : " transakcji");

            _lastTransactions.Clear();
            var txList = DatabaseService.GetLastTransactionsForCategory(_uid, catId.Value, 12);
            foreach (var t in txList)
            {
                _lastTransactions.Add(new CategoryTransactionRow { Date = t.Date, Amount = t.Amount, Description = t.Description });
            }

            _selectedColorHex = (_selectedCategory.ColorBrush as SolidColorBrush)?.Color.ToString() ?? string.Empty;
        }

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
                // ignore seeding errors
            }
        }

        // === Wybór koloru (Add/Edit panel) ===
        private void ColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string hex)
            {
                _selectedColorHex = hex;
                b.Focus();
            }
        }

        private void ClearAllDeleteConfirmPanels()
        {
            foreach (var c in _categories)
            {
                if (c.IsDeleteConfirmVisible)
                    c.IsDeleteConfirmVisible = false;
            }
        }

        public class CategoryVm : INotifyPropertyChanged
        {
            private Brush _colorBrush = Brushes.Gray;
            private bool _isDeleteConfirmVisible;
            private string? _description;
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public Brush ColorBrush { get => _colorBrush; set { _colorBrush = value; OnPropertyChanged(nameof(ColorBrush)); OnPropertyChanged(nameof(Color)); } }
            // For XAML bindings that expect Color property
            public Brush Color { get => _colorBrush; set { _colorBrush = value; OnPropertyChanged(nameof(Color)); OnPropertyChanged(nameof(ColorBrush)); } }
            public string? Icon { get; set; }

            public string? Description
            {
                get => _description;
                set { _description = value; OnPropertyChanged(nameof(Description)); }
            }

            public bool IsDeleteConfirmVisible
            {
                get => _isDeleteConfirmVisible;
                set { _isDeleteConfirmVisible = value; OnPropertyChanged(nameof(IsDeleteConfirmVisible)); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private class CategoryTransactionRow
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public string Description { get; set; } = string.Empty;
        }
    }
}