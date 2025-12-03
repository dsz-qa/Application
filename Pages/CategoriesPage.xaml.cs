using Finly.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
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

        // ===== LISTA KATEGORII (lewa kolumna) =====
        private readonly ObservableCollection<CategoryVm> _categories = new();
        public ObservableCollection<CategoryVm> Categories => _categories;

        private CollectionView _categoriesView;

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
                    ClearAllDeleteConfirmPanels();
                    ApplyCategoriesFilter();
                }
            }
        }

        // wybrana kategoria (dla szczegółów / porównania)
        private CategoryVm? _selectedCategory;

        // Szczegóły do ramki po prawej
        private string _selectedCategoryName = string.Empty;
        public string SelectedCategoryName
        {
            get => _selectedCategoryName;
            private set
            {
                _selectedCategoryName = value;
                RaisePropertyChanged(nameof(SelectedCategoryName));
            }
        }

        private Brush _selectedCategoryColorBrush = Brushes.Transparent;
        public Brush SelectedCategoryColorBrush
        {
            get => _selectedCategoryColorBrush;
            private set
            {
                _selectedCategoryColorBrush = value;
                RaisePropertyChanged(nameof(SelectedCategoryColorBrush));
            }
        }

        private string? _selectedCategoryDescriptionText;
        public string? SelectedCategoryDescriptionText
        {
            get => _selectedCategoryDescriptionText;
            private set
            {
                _selectedCategoryDescriptionText = value;
                RaisePropertyChanged(nameof(SelectedCategoryDescriptionText));
            }
        }

        // Tryb edycji w ramce szczegółów
        private bool _isDetailsEditMode;
        public bool IsDetailsEditMode
        {
            get => _isDetailsEditMode;
            set
            {
                _isDetailsEditMode = value;
                RaisePropertyChanged(nameof(IsDetailsEditMode));
            }
        }

        public string EditCategoryName { get; set; } = string.Empty;
        public string? EditCategoryDescription { get; set; }
        public string? EditCategoryColorHex { get; set; }
        public Brush EditCategoryColorBrush { get; set; } = Brushes.Transparent;

        // kolor wybrany w panelu dodawania
        private string _selectedColorHex = string.Empty;

        // ===== STATYSTYKI DLA WYBRANEJ KATEGORII (środek) =====
        private decimal _selectedCategoryTotalAmount;
        public decimal SelectedCategoryTotalAmount
        {
            get => _selectedCategoryTotalAmount;
            set
            {
                _selectedCategoryTotalAmount = value;
                RaisePropertyChanged(nameof(SelectedCategoryTotalAmount));
            }
        }

        private decimal _selectedCategoryAverageMonthly;
        public decimal SelectedCategoryAverageMonthly
        {
            get => _selectedCategoryAverageMonthly;
            set
            {
                _selectedCategoryAverageMonthly = value;
                RaisePropertyChanged(nameof(SelectedCategoryAverageMonthly));
            }
        }

        private int _selectedCategoryTransactionCount;
        public int SelectedCategoryTransactionCount
        {
            get => _selectedCategoryTransactionCount;
            set
            {
                _selectedCategoryTransactionCount = value;
                RaisePropertyChanged(nameof(SelectedCategoryTransactionCount));
            }
        }

        public ObservableCollection<CategoryTransactionRow> SelectedCategoryRecentTransactions { get; } = new();

        // ===== STRUKTURA WYDATKÓW (prawa-góra) =====
        public ObservableCollection<CategoryShareItem> CategoryShares { get; } = new();
        public ObservableCollection<CategoryShareItem> TopCategories { get; } = new();
        public ObservableCollection<CategoryShareItem> BottomCategories { get; } = new();

        private int _activeCategoriesCount;
        public int ActiveCategoriesCount
        {
            get => _activeCategoriesCount;
            set
            {
                _activeCategoriesCount = value;
                RaisePropertyChanged(nameof(ActiveCategoriesCount));
                RaisePropertyChanged(nameof(ActiveCategoriesSummary));
            }
        }

        private int _totalCategoriesCount;
        public int TotalCategoriesCount
        {
            get => _totalCategoriesCount;
            set
            {
                _totalCategoriesCount = value;
                RaisePropertyChanged(nameof(TotalCategoriesCount));
                RaisePropertyChanged(nameof(ActiveCategoriesSummary));
            }
        }

        public string ActiveCategoriesSummary =>
            $"Aktywne kategorie w wybranym okresie: {ActiveCategoriesCount}/{TotalCategoriesCount}";

        // ===== PORÓWNANIE Z POPRZEDNIM OKRESEM (prawa-dół) =====
        private decimal _currentPeriodAmount;
        public decimal CurrentPeriodAmount
        {
            get => _currentPeriodAmount;
            set
            {
                _currentPeriodAmount = value;
                RaisePropertyChanged(nameof(CurrentPeriodAmount));
                RaisePropertyChanged(nameof(AmountDifference));
                RaisePropertyChanged(nameof(PercentageDifference));
            }
        }

        private decimal _previousPeriodAmount;
        public decimal PreviousPeriodAmount
        {
            get => _previousPeriodAmount;
            set
            {
                _previousPeriodAmount = value;
                RaisePropertyChanged(nameof(PreviousPeriodAmount));
                RaisePropertyChanged(nameof(AmountDifference));
                RaisePropertyChanged(nameof(PercentageDifference));
            }
        }

        public decimal AmountDifference => CurrentPeriodAmount - PreviousPeriodAmount;

        public double PercentageDifference =>
            PreviousPeriodAmount == 0
                ? 0
                : (double)((CurrentPeriodAmount - PreviousPeriodAmount) / PreviousPeriodAmount) * 100.0;

        // ===== KPI U GÓRY =====
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

        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler? PropertyChanged;
        private void RaisePropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public CategoriesPage() : this(UserService.GetCurrentUserId()) { }

        public CategoriesPage(int userId)
        {
            InitializeComponent();

            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            DataContext = this;

            Loaded += CategoriesPage_Loaded;

            _categoriesView = (CollectionView)CollectionViewSource.GetDefaultView(_categories);
            _categoriesView.Filter = CategoryFilter;

            PeriodBar.RangeChanged += PeriodBar_RangeChanged;
        }

        // ========== FILTROWANIE LISTY KATEGORII (Szukaj) ==========

        private bool CategoryFilter(object obj)
        {
            if (obj is not CategoryVm vm) return false;
            var q = (SearchText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(q)) return true;

            var cmp = StringComparison.CurrentCultureIgnoreCase;
            return (vm.Name?.IndexOf(q, cmp) >= 0) ||
                   (vm.Description?.IndexOf(q, cmp) >= 0);
        }

        private void ApplyCategoriesFilter()
        {
            try { _categoriesView.Refresh(); } catch { }
        }

        // ========== ŻYCIE STRONY ==========

        private void CategoriesPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureKpiRefs();
                EnsureDefaultCategories();
                LoadCategories();
                UpdateCategoryKpis();
                UpdateCategoryStats();
                UpdateCategoryComparison();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd ładowania kategorii: " + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            EnsureKpiRefs();

            UpdateCategoryKpis();       // górne trzy kafelki
            UpdateCategoryStats();      // struktura wydatków + TOP/BOTTOM
            UpdateCategoryComparison(); // porównanie okresów dla wybranej kategorii
            LoadSelectedCategoryDetails(); // szczegóły wybranej kategorii
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

        // ========== KPI U GÓRY (zależne od okresu) ==========

        private void UpdateCategoryKpis()
        {
            EnsureKpiRefs();
            try
            {
                DateTime from = PeriodBar.StartDate;
                DateTime to = PeriodBar.EndDate;
                if (from > to) (from, to) = (to, from);

                // Expenses by category name
                var dt = DatabaseService.GetExpenses(_uid, from, to, null, null, null);
                var counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
                var sums = new Dictionary<string, decimal>(StringComparer.CurrentCultureIgnoreCase);
                decimal totalSum = 0m;
                foreach (DataRow row in dt.Rows)
                {
                    try
                    {
                        var name = (row[6]?.ToString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(name) || name == "(brak)") continue;
                        var amount = Math.Abs(Convert.ToDecimal(row[3]));
                        counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
                        sums[name] = sums.TryGetValue(name, out var s) ? s + amount : amount;
                        totalSum += amount;
                    }
                    catch { }
                }

                // Incomes aggregate by category – add to existing aggregates
                try
                {
                    using var con = DatabaseService.GetConnection();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = @"SELECT c.Name, i.Amount FROM Incomes i JOIN Categories c ON c.Id = i.CategoryId WHERE i.UserId=@u AND i.Date>=@from AND i.Date<=@to AND i.CategoryId IS NOT NULL;";
                    cmd.Parameters.AddWithValue("@u", _uid);
                    cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        string name = r.IsDBNull(0) ? "" : r.GetString(0);
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var amount = Math.Abs(Convert.ToDecimal(r.GetValue(1)));
                        counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
                        sums[name] = sums.TryGetValue(name, out var s) ? s + amount : amount;
                        totalSum += amount;
                    }
                }
                catch { }

                // Najczęściej używana
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

                // Najdroższa (sumy wydatków+przychodów)
                if (sums.Count > 0)
                {
                    var mostExp = sums.OrderByDescending(kv => kv.Value).First();
                    if (_mostExpName != null) _mostExpName.Text = mostExp.Key;
                    if (_mostExpAmount != null) _mostExpAmount.Text = mostExp.Value.ToString("N2") + " zł";
                    var pct = totalSum > 0m ? (double)(mostExp.Value / totalSum * 100m) : 0.0;
                    if (_mostExpPercentBar != null) _mostExpPercentBar.Value = pct;
                    if (_mostExpPercentText != null) _mostExpPercentText.Text = Math.Round(pct, 0) + "% wszystkich transakcji";
                }
                else
                {
                    if (_mostExpName != null) _mostExpName.Text = "Brak danych dla wybranego okresu";
                    if (_mostExpAmount != null) _mostExpAmount.Text = string.Empty;
                    if (_mostExpPercentBar != null) _mostExpPercentBar.Value = 0;
                    if (_mostExpPercentText != null) _mostExpPercentText.Text = string.Empty;
                }

                // Twoje kategorie – aktywne gdy mają jakiekolwiek tx (expense/income)
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

        // ========== ŁADOWANIE LISTY KATEGORII (niezależne od okresu) ==========

        private void LoadCategories()
        {
            _categories.Clear();

            var tuples = DatabaseService.GetCategoriesExtended(_uid);
            foreach (var (id, name, color, icon) in tuples)
            {
                Brush brush;
                if (string.IsNullOrWhiteSpace(color))
                {
                    brush = GetBrushForName(name);
                }
                else
                {
                    brush = (Brush)(new BrushConverter().ConvertFromString(color)!);
                }

                var vm = new CategoryVm
                {
                    Id = id,
                    Name = name,
                    ColorBrush = brush,
                    ColorHex = string.IsNullOrWhiteSpace(color) ? null : color,
                    Icon = icon
                };
                _categories.Add(vm);
            }

            // opisy
            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT Id, Description, Color FROM Categories WHERE UserId=@u;";
                cmd.Parameters.AddWithValue("@u", _uid);
                using var reader = cmd.ExecuteReader();
                var mapDesc = new Dictionary<int, string?>(256);
                var mapColor = new Dictionary<int, string?>(256);
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);
                    string? desc = reader.IsDBNull(1) ? null : reader.GetString(1);
                    string? colorHex = reader.IsDBNull(2) ? null : reader.GetString(2);
                    mapDesc[id] = desc;
                    mapColor[id] = colorHex;
                }

                foreach (var vm in _categories)
                {
                    if (mapDesc.TryGetValue(vm.Id, out var d)) vm.Description = d;
                    if (mapColor.TryGetValue(vm.Id, out var ch) && !string.IsNullOrWhiteSpace(ch))
                    {
                        vm.ColorHex = ch;
                        try { vm.ColorBrush = (Brush)(new BrushConverter().ConvertFromString(ch)!); } catch { }
                    }
                }
            }
            catch { }

            ApplyCategoriesFilter();
            ClearAllDeleteConfirmPanels();
            RaisePropertyChanged(nameof(Categories));

            // po każdej zmianie listy przelicz statystyki
            UpdateCategoryStats();
            UpdateCategoryComparison();
        }

        // ========== DODAWANIE / EDYCJA / USUWANIE ==========

        private void AddCategoryTile_Click(object sender, RoutedEventArgs e)
        {
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

        // kliknięcie ikony edycji – włączenie inline-edita pod kafelkiem
        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
            {
                _selectedCategory = vm;

                // tylko jedna kategoria w trybie edycji
                foreach (var c in _categories)
                    c.IsEditing = false;

                vm.IsEditing = true;

                LoadSelectedCategoryDetails();
                UpdateCategoryComparison();
                UpdateDetailsFrameFromSelection();
            }
        }

        // wybór koloru w edycji inline
        private void InlineColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is CategoryVm vm && b.Tag is string hex)
            {
                vm.ColorHex = hex;
                var brush = (Brush)(new BrushConverter().ConvertFromString(hex)!);
                vm.ColorBrush = brush;
            }
        }

        // zapis edycji inline
        private void InlineSaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not CategoryVm vm) return;

            var newName = (vm.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("Podaj nazwę kategorii.", "Błąd",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string? colorToSave = string.IsNullOrWhiteSpace(vm.ColorHex) ? null : vm.ColorHex;

                DatabaseService.UpdateCategoryFull(vm.Id, _uid, newName, colorToSave, vm.Icon);

                try
                {
                    using var con = DatabaseService.GetConnection();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "UPDATE Categories SET Description=@d WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@d", (object?)vm.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", vm.Id);
                    cmd.ExecuteNonQuery();
                }
                catch { }

                vm.IsEditing = false;

                LoadCategories();
                UpdateCategoryKpis();
                UpdateDetailsFrameFromSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd zapisu kategorii: " + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // anulowanie edycji inline
        private void InlineCancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
            {
                vm.IsEditing = false;
                // odśwież listę z bazy, żeby odrzucić lokalne zmiany
                LoadCategories();
                UpdateDetailsFrameFromSelection();
            }
        }

        private void ShowDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CategoryVm vm)
            {
                ClearAllDeleteConfirmPanels();
                vm.IsDeleteConfirmVisible = true;
            }
        }

        private void HideDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
                vm.IsDeleteConfirmVisible = false;
            else
                ClearAllDeleteConfirmPanels();
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

                MessageBox.Show("Kategoria usunięta.", "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                LoadCategories();
                UpdateCategoryKpis();
                UpdateDetailsFrameFromSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd usuwania: " + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // kliknięcie w cały kafelek kategorii – wybór do statystyk
        private void CategoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is CategoryVm vm)
            {
                // zaznacz tylko jedną kartę: reset dla innych
                foreach (var c in _categories)
                {
                    if (ReferenceEquals(c, vm)) continue;
                    c.IsSelected = false;
                }
                vm.IsSelected = true;

                _selectedCategory = vm;
                LoadSelectedCategoryDetails();
                UpdateCategoryComparison();
                UpdateDetailsFrameFromSelection();
            }
        }

        private void UpdateDetailsFrameFromSelection()
        {
            if (_selectedCategory == null)
            {
                SelectedCategoryName = string.Empty;
                SelectedCategoryColorBrush = Brushes.Transparent;
                SelectedCategoryDescriptionText = null;
                IsDetailsEditMode = false;
                return;
            }

            SelectedCategoryName = _selectedCategory.Name;
            SelectedCategoryColorBrush = _selectedCategory.ColorBrush;
            SelectedCategoryDescriptionText = _selectedCategory.Description;
            IsDetailsEditMode = false;
        }


        // ========== SZCZEGÓŁY WYBRANEJ KATEGORII (zależne od okresu) ==========

        private void LoadSelectedCategoryDetails()
        {
            SelectedCategoryTotalAmount = 0m;
            SelectedCategoryAverageMonthly = 0m;
            SelectedCategoryTransactionCount = 0;
            SelectedCategoryRecentTransactions.Clear();

            if (_selectedCategory == null) return;

            int? catId = DatabaseService.GetCategoryIdByName(_uid, _selectedCategory.Name);
            if (!catId.HasValue) return;

            DateTime from = PeriodBar.StartDate;
            DateTime to = PeriodBar.EndDate;
            if (from > to) (from, to) = (to, from);

            // Expenses table
            DataTable? expensesDt = null;
            try { expensesDt = DatabaseService.GetExpenses(_uid, from, to, catId, null, null); } catch { }
            decimal sumExp = 0m; int countExp = 0;
            if (expensesDt != null)
            {
                foreach (DataRow row in expensesDt.Rows)
                {
                    try { sumExp += Math.Abs(Convert.ToDecimal(row[3])); countExp++; } catch { }
                }
            }

            // Incomes for category
            var (sumInc, countInc) = SumIncomes(_uid, from, to, catId);
            var totalSum = sumExp + sumInc;
            var totalCount = countExp + countInc;
            SelectedCategoryTotalAmount = totalSum;
            SelectedCategoryTransactionCount = totalCount;

            double monthsSpan = Math.Max(1.0, (to - from).TotalDays / 30.0);
            SelectedCategoryAverageMonthly = monthsSpan <= 0 ? 0 : totalSum / (decimal)monthsSpan;

            // Ostatnie transakcje
            try
            {
                var txList = DatabaseService.GetLastTransactionsForCategory(_uid, catId.Value, 5);
                SelectedCategoryRecentTransactions.Clear();
                foreach (var t in txList)
                {
                    SelectedCategoryRecentTransactions.Add(new CategoryTransactionRow { Date = t.Date, Amount = t.Amount, Description = t.Description });
                }
            }
            catch { }
        }

        // ========== STRUKTURA KATEGORII (wydatki+przychody) ==========

        private void UpdateCategoryStats()
        {
            try
            {
                DateTime from = PeriodBar.StartDate;
                DateTime to = PeriodBar.EndDate;
                if (from > to) (from, to) = (to, from);

                TotalCategoriesCount = _categories.Count;

                var list = new List<(string name, decimal amount, int count)>();

                foreach (var cat in _categories)
                {
                    int? catId = null;
                    try { catId = DatabaseService.GetCategoryIdByName(_uid, cat.Name); } catch { }

                    if (!catId.HasValue)
                    {
                        list.Add((cat.Name, 0m, 0));
                        continue;
                    }

                    // expenses
                    DataTable? dt = null;
                    try { dt = DatabaseService.GetExpenses(_uid, from, to, catId, null, null); } catch { }
                    decimal sumExp = 0m;
                    int cntExp = 0;

                    if (dt != null)
                    {
                        foreach (DataRow r in dt.Rows)
                        {
                            try { sumExp += Math.Abs(Convert.ToDecimal(r[3])); cntExp++; } catch { }
                        }
                    }

                    // incomes
                    var (sumInc, cntInc) = SumIncomes(_uid, from, to, catId);
                    var total = sumExp + sumInc;
                    var cnt = cntExp + cntInc;

                    list.Add((cat.Name, total, cnt));
                }

                ActiveCategoriesCount = list.Count(x => x.count > 0);
                decimal totalAmount = list.Sum(x => x.amount);

                CategoryShares.Clear();
                if (totalAmount > 0)
                {
                    foreach (var it in list.OrderByDescending(x => x.amount))
                    {
                        double pct = (double)(it.amount / totalAmount) * 100.0;
                        CategoryShares.Add(new CategoryShareItem
                        {
                            Name = it.name,
                            Amount = it.amount,
                            Percent = pct
                        });
                    }
                }
                else
                {
                    foreach (var it in list)
                    {
                        CategoryShares.Add(new CategoryShareItem
                        {
                            Name = it.name,
                            Amount = 0m,
                            Percent = 0
                        });
                    }
                }

                var positive = list.Where(x => x.amount > 0).ToList();

                TopCategories.Clear();
                foreach (var it in positive.OrderByDescending(x => x.amount).Take(3))
                {
                    double pct = totalAmount > 0 ? (double)(it.amount / totalAmount) * 100.0 : 0.0;
                    TopCategories.Add(new CategoryShareItem
                    {
                        Name = it.name,
                        Amount = it.amount,
                        Percent = pct
                    });
                }

                BottomCategories.Clear();
                foreach (var it in positive.OrderBy(x => x.amount).Take(3))
                {
                    double pct = totalAmount > 0 ? (double)(it.amount / totalAmount) * 100.0 : 0.0;
                    BottomCategories.Add(new CategoryShareItem
                    {
                        Name = it.name,
                        Amount = it.amount,
                        Percent = pct
                    });
                }

                RaisePropertyChanged(nameof(CategoryShares));
                RaisePropertyChanged(nameof(TopCategories));
                RaisePropertyChanged(nameof(BottomCategories));
            }
            catch
            {
                CategoryShares.Clear();
                TopCategories.Clear();
                BottomCategories.Clear();
                ActiveCategoriesCount = 0;
                TotalCategoriesCount = _categories.Count;
            }
        }

        // ========== PORÓWNANIE OKRESÓW (wydatki+przychody) ==========

        private void UpdateCategoryComparison()
        {
            try
            {
                if (_selectedCategory == null)
                {
                    CurrentPeriodAmount = 0;
                    PreviousPeriodAmount = 0;
                    return;
                }

                int? catId = null;
                try { catId = DatabaseService.GetCategoryIdByName(_uid, _selectedCategory.Name); } catch { }
                if (!catId.HasValue)
                {
                    CurrentPeriodAmount = 0;
                    PreviousPeriodAmount = 0;
                    return;
                }

                DateTime from = PeriodBar.StartDate;
                DateTime to = PeriodBar.EndDate;
                if (from > to) (from, to) = (to, from);

                var span = to - from;
                DateTime prevTo = from;
                DateTime prevFrom = from - span;

                // current
                decimal currExp = 0m;
                DataTable? cdt = null;
                try { cdt = DatabaseService.GetExpenses(_uid, from, to, catId, null, null); } catch { }
                if (cdt != null)
                {
                    foreach (DataRow r in cdt.Rows)
                    {
                        try { currExp += Math.Abs(Convert.ToDecimal(r[3])); } catch { }
                    }
                }

                var (currInc, _) = SumIncomes(_uid, from, to, catId);

                // previous
                decimal prevExp = 0m;
                DataTable? pdt = null;
                try { pdt = DatabaseService.GetExpenses(_uid, prevFrom, prevTo, catId, null, null); } catch { }
                if (pdt != null)
                {
                    foreach (DataRow r in pdt.Rows)
                    {
                        try { prevExp += Math.Abs(Convert.ToDecimal(r[3])); } catch { }
                    }
                }

                var (prevInc, _) = SumIncomes(_uid, prevFrom, prevTo, catId);

                CurrentPeriodAmount = currExp + currInc;
                PreviousPeriodAmount = prevExp + prevInc;
            }
            catch
            {
                CurrentPeriodAmount = 0;
                PreviousPeriodAmount = 0;
            }
        }

        // ===== Ramka szczegółów: akcje edycji =====
        private void EditDetails_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCategory == null) return;
            IsDetailsEditMode = true;
            EditCategoryName = _selectedCategory.Name;
            EditCategoryDescription = _selectedCategory.Description;
            EditCategoryColorHex = _selectedCategory.ColorHex;
            EditCategoryColorBrush = _selectedCategory.ColorBrush;
            RaisePropertyChanged(nameof(EditCategoryName));
            RaisePropertyChanged(nameof(EditCategoryDescription));
            RaisePropertyChanged(nameof(EditCategoryColorHex));
            RaisePropertyChanged(nameof(EditCategoryColorBrush));
        }


        private void SaveDetails_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCategory == null) return;
            var newName = (EditCategoryName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("Podaj nazwę kategorii.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                string? colorToSave = string.IsNullOrWhiteSpace(EditCategoryColorHex) ? _selectedCategory.ColorHex : EditCategoryColorHex;
                DatabaseService.UpdateCategoryFull(_selectedCategory.Id, _uid, newName, colorToSave, _selectedCategory.Icon);
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "UPDATE Categories SET Description=@d WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@d", (object?)EditCategoryDescription ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", _selectedCategory.Id);
                cmd.ExecuteNonQuery();

                // Lokalnie zaktualizuj VM
                _selectedCategory.Name = newName;
                _selectedCategory.Description = EditCategoryDescription;
                if (!string.IsNullOrWhiteSpace(colorToSave))
                {
                    _selectedCategory.ColorHex = colorToSave;
                    try { _selectedCategory.ColorBrush = (Brush)(new BrushConverter().ConvertFromString(colorToSave)!); } catch { }
                }

                // Odśwież listę i detale
                LoadCategories();
                _selectedCategory = _categories.FirstOrDefault(c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
                UpdateDetailsFrameFromSelection();
                LoadSelectedCategoryDetails();
                UpdateCategoryStats();
                IsDetailsEditMode = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd zapisu: " + ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelDetails_Click(object sender, RoutedEventArgs e)
        {
            IsDetailsEditMode = false;
            UpdateDetailsFrameFromSelection();
        }

        // ========== POMOCNICZE ==========

        private Brush GetBrushForName(string name) => GetRandomBrush(name);

        private static Brush GetRandomBrush(string seed)
        {
            var palette = new[]
            {
                "#FFED7A1A","#FF3FA7D6","#FF7BC96F","#FFAF7AC5",
                "#FFF6BF26","#FF56C1A7","#FFCE6A6B","#FF9AA0A6"
            };
            var idx = Math.Abs(seed?.GetHashCode() ?? 0) % palette.Length;
            return (Brush)(new BrushConverter().ConvertFromString(palette[idx])!);
        }

        private void EnsureDefaultCategories()
        {
            try
            {
                var existing = DatabaseService.GetCategoriesByUser(_uid) ?? new List<string>();
                var defaultExpenses = new[]
                {
                    "Jedzenie", "Transport", "Mieszkanie", "Rachunki",
                    "Rozrywka", "Zdrowie", "Ubrania"
                };
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
                // ignoruj
            }
        }

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

        // Helper: sum incomes for user in range optionally for specific category
        private static (decimal sum, int count) SumIncomes(int userId, DateTime from, DateTime to, int? categoryId)
        {
            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"SELECT Amount, Date FROM Incomes WHERE UserId=@u AND Date>=@from AND Date<=@to" +
                                  (categoryId.HasValue ? " AND CategoryId=@cat" : "") + ";";
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));
                if (categoryId.HasValue) cmd.Parameters.AddWithValue("@cat", categoryId.Value);
                using var r = cmd.ExecuteReader();
                decimal sum = 0m; int count = 0;
                while (r.Read())
                {
                    try
                    {
                        var amt = Convert.ToDecimal(r.GetValue(0));
                        sum += Math.Abs(amt);
                        count++;
                    }
                    catch { }
                }
                return (sum, count);
            }
            catch { return (0m, 0); }
        }

        // ===== MODELE WEWNĘTRZNE =====

        public class CategoryVm : INotifyPropertyChanged
        {
            private Brush _colorBrush = Brushes.Gray;
            private bool _isDeleteConfirmVisible;
            private string? _description;
            private bool _isEditing;
            private string? _colorHex;
            private bool _isSelected;

            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;

            public Brush ColorBrush
            {
                get => _colorBrush;
                set
                {
                    _colorBrush = value;
                    OnPropertyChanged(nameof(ColorBrush));
                    OnPropertyChanged(nameof(Color));
                }
            }

            // dla XAML – to samo co ColorBrush
            public Brush Color
            {
                get => _colorBrush;
                set
                {
                    _colorBrush = value;
                    OnPropertyChanged(nameof(Color));
                    OnPropertyChanged(nameof(ColorBrush));
                }
            }

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

            // czy wyświetlać panel edycji pod kafelkiem
            public bool IsEditing
            {
                get => _isEditing;
                set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); }
            }

            // kolor w formie hex (do zapisu w bazie)
            public string? ColorHex
            {
                get => _colorHex;
                set { _colorHex = value; OnPropertyChanged(nameof(ColorHex)); }
            }

            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string prop)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public class CategoryTransactionRow
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public string Description { get; set; } = string.Empty;
        }

        public class CategoryShareItem
        {
            public string Name { get; set; } = string.Empty;
            public double Percent { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
