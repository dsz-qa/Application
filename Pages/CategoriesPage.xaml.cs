// CategoriesPage.xaml.cs
using Finly.Services;
using Finly.Services.Features;
using Finly.Views.Dialogs;
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
using System.Windows.Threading;

namespace Finly.Pages
{
    public partial class CategoriesPage : UserControl, INotifyPropertyChanged
    {
        private readonly int _uid;

        // ===== TRYB (pod PeriodBar): Przychody / Wszystko / Wydatki =====
        private enum CategoryMode { Incomes, All, Expenses }
        private CategoryMode _mode = CategoryMode.All;

        // ===== LISTA KATEGORII =====
        private readonly ObservableCollection<CategoryVm> _categories = new();
        public ObservableCollection<CategoryVm> Categories => _categories;

        private CollectionView _categoriesView;

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                var v = value ?? string.Empty;
                if (_searchText != v)
                {
                    _searchText = v;
                    RaisePropertyChanged(nameof(SearchText));
                    ClearAllDeleteConfirmPanels();
                    ApplyCategoriesFilter();
                }
            }
        }

        private CategoryVm? _selectedCategory;

        // ===== BINDINGI do XAML =====
        public bool HasSelectedCategory => _selectedCategory != null;
        public string SelectedCategoryName => _selectedCategory?.Name ?? string.Empty;
        public string SelectedCategoryDescription => _selectedCategory?.Description ?? string.Empty;
        public Brush SelectedCategoryColorBrush => _selectedCategory?.ColorBrush ?? Brushes.Transparent;

        // ===== Ostatnia transakcja (TYLKO 1) =====
        private CategoryTransactionRow? _lastTransaction;
        public bool HasLastTransaction => _lastTransaction != null;

        public string LastTransactionDateText
            => _lastTransaction == null ? string.Empty : _lastTransaction.Date.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);

        public string LastTransactionDescription
            => _lastTransaction?.Description ?? string.Empty;

        public string LastTransactionAmountText
            => _lastTransaction == null ? string.Empty : _lastTransaction.Amount.ToString("N2", CultureInfo.CurrentCulture) + " zł";

        // ===== Szczegóły: statystyki =====
        private decimal _selectedCategoryTotalAmount;
        public decimal SelectedCategoryTotalAmount
        {
            get => _selectedCategoryTotalAmount;
            set { _selectedCategoryTotalAmount = value; RaisePropertyChanged(nameof(SelectedCategoryTotalAmount)); }
        }

        private int _selectedCategoryTransactionCount;
        public int SelectedCategoryTransactionCount
        {
            get => _selectedCategoryTransactionCount;
            set { _selectedCategoryTransactionCount = value; RaisePropertyChanged(nameof(SelectedCategoryTransactionCount)); }
        }

        // ===== Analizy =====
        public ObservableCollection<CategoryShareItem> CategoryShares { get; } = new();
        public ObservableCollection<CategoryShareItem> TopCategories { get; } = new();
        public ObservableCollection<CategoryShareItem> BottomCategories { get; } = new();

        // ===== KPI refs =====
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

        private int _activeCategoriesCount;
        private int _inactiveCategoriesCount;
        private int _totalCategoriesCount;

        // ===== STABILNOŚĆ / unsub =====
        private bool _isUnloaded;
        private EventHandler? _dbChangedHandler;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void RaisePropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public CategoriesPage() : this(UserService.GetCurrentUserId()) { }

        public CategoriesPage(int userId)
        {
            InitializeComponent();

            _uid = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            DataContext = this;

            _categoriesView = (CollectionView)CollectionViewSource.GetDefaultView(_categories);
            _categoriesView.Filter = CategoryFilter;

            Loaded += CategoriesPage_Loaded;
            Unloaded += CategoriesPage_Unloaded;

            // Hook PeriodBar events like Dashboard (bez ryzyka NRE)
            if (FindName("PeriodBar") is Finly.Views.Controls.PeriodBarControl pb)
            {
                pb.RangeChanged += PeriodBar_RangeChanged;
                pb.SearchClicked += PeriodBar_RangeChanged; // traktujemy jak reload
                pb.ClearClicked += PeriodBar_ClearClicked;
            }

            // Refresh when DB changes (jak Dashboard)
            _dbChangedHandler = (_, __) =>
            {
                if (_isUnloaded) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        EnsureKpiRefs();
                        LoadCategories();
                        UpdateModeButtonsUi();
                        UpdateCategoryKpis();
                        UpdateCategoryStats();
                        LoadSelectedCategoryDetails();
                    }
                    catch { }
                }), DispatcherPriority.Background);
            };
            DatabaseService.DataChanged += _dbChangedHandler;
        }

        private void CategoriesPage_Unloaded(object? sender, RoutedEventArgs e)
        {
            _isUnloaded = true;

            try
            {
                if (_dbChangedHandler != null)
                    DatabaseService.DataChanged -= _dbChangedHandler;
            }
            catch { }

            try
            {
                if (FindName("PeriodBar") is Finly.Views.Controls.PeriodBarControl pb)
                {
                    pb.RangeChanged -= PeriodBar_RangeChanged;
                    pb.SearchClicked -= PeriodBar_RangeChanged;
                    pb.ClearClicked -= PeriodBar_ClearClicked;
                }
            }
            catch { }
        }

        // ========== TRYB ==========
        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b) return;

            _mode = (b.Tag?.ToString()) switch
            {
                "Incomes" => CategoryMode.Incomes,
                "Expenses" => CategoryMode.Expenses,
                _ => CategoryMode.All
            };

            UpdateModeButtonsUi();

            UpdateCategoryKpis();
            UpdateCategoryStats();
            LoadSelectedCategoryDetails();
        }

        private void UpdateModeButtonsUi()
        {
            SetModeBtn(ModeIncomesBtn, _mode == CategoryMode.Incomes);
            SetModeBtn(ModeAllBtn, _mode == CategoryMode.All);
            SetModeBtn(ModeExpensesBtn, _mode == CategoryMode.Expenses);
        }

        // KLUCZ: żadnego FindResource na elemencie => zero LogicalTreeLoop
        private void SetModeBtn(Button btn, bool active)
        {
            var orange = GetAppBrush("Separator.Orange", Brushes.Orange);
            var blue = GetAppBrush("Brand.Blue", Brushes.DodgerBlue);

            btn.BorderBrush = active ? orange : blue;
            btn.BorderThickness = active ? new Thickness(2) : new Thickness(1);
            btn.Opacity = active ? 1.0 : 0.92;
        }

        // ========== FILTROWANIE ==========
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
                EnsureDefaultCategoriesOncePerUser();
                LoadCategories();

                UpdateModeButtonsUi();
                UpdateCategoryKpis();
                UpdateCategoryStats();
                LoadSelectedCategoryDetails();
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
            ClearAllDeleteConfirmPanels();
            UpdateCategoryKpis();
            UpdateCategoryStats();
            LoadSelectedCategoryDetails();
        }

        private void PeriodBar_ClearClicked(object? sender, EventArgs e)
        {
            // zachowanie jak Dashboard: po clear wracasz do "Ten miesiąc"
            try
            {
                if (FindName("PeriodBar") is Finly.Views.Controls.PeriodBarControl pb)
                    pb.SetPreset(Finly.Models.DateRangeMode.Month);
            }
            catch { }

            PeriodBar_RangeChanged(sender, e);
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

        // ===== procenty: 0.5% kroki =====
        private static double RoundToHalfPercent(double pct)
        {
            if (pct <= 0.0) return 0.0;
            return Math.Round(pct * 2.0, MidpointRounding.AwayFromZero) / 2.0;
        }

        // ============================================================
        //  AGREGACJA (Expenses/Incomes -> per kategoria)
        // ============================================================
        private void AccumulateCategoryAggregates(
            DateTime from, DateTime to,
            Dictionary<string, int> counts,
            Dictionary<string, decimal> sums,
            ref decimal totalSum,
            bool includeExpenses,
            bool includeIncomes)
        {
            using var con = DatabaseService.GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            var sqlParts = new List<string>();

            if (includeExpenses)
            {
                sqlParts.Add(@"
SELECT c.Name AS Name, ABS(e.Amount) AS Amount
FROM Expenses e
JOIN Categories c ON c.Id = e.CategoryId
WHERE e.UserId=@u AND e.Date>=@from AND e.Date<=@to
  AND e.CategoryId IS NOT NULL");
            }

            if (includeIncomes)
            {
                sqlParts.Add(@"
SELECT c.Name AS Name, ABS(i.Amount) AS Amount
FROM Incomes i
JOIN Categories c ON c.Id = i.CategoryId
WHERE i.UserId=@u AND i.Date>=@from AND i.Date<=@to
  AND i.CategoryId IS NOT NULL");
            }

            if (sqlParts.Count == 0) return;

            cmd.CommandText = string.Join("\nUNION ALL\n", sqlParts) + ";";
            cmd.Parameters.AddWithValue("@u", _uid);
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.IsDBNull(0) ? "" : r.GetString(0);
                name = (name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                decimal amount = 0m;
                try { amount = Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture); } catch { }

                counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
                sums[name] = sums.TryGetValue(name, out var s) ? s + amount : amount;
                totalSum += amount;
            }
        }

        private (decimal sum, int count) GetCategoryTotalsById(DateTime from, DateTime to, int categoryId, bool expenses, bool incomes)
        {
            using var con = DatabaseService.GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            var sqlParts = new List<string>();

            if (expenses)
            {
                sqlParts.Add(@"
SELECT ABS(e.Amount) AS Amount
FROM Expenses e
WHERE e.UserId=@u AND e.CategoryId=@c AND e.Date>=@from AND e.Date<=@to");
            }

            if (incomes)
            {
                sqlParts.Add(@"
SELECT ABS(i.Amount) AS Amount
FROM Incomes i
WHERE i.UserId=@u AND i.CategoryId=@c AND i.Date>=@from AND i.Date<=@to");
            }

            if (sqlParts.Count == 0) return (0m, 0);

            cmd.CommandText = string.Join("\nUNION ALL\n", sqlParts) + ";";
            cmd.Parameters.AddWithValue("@u", _uid);
            cmd.Parameters.AddWithValue("@c", categoryId);
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            using var r = cmd.ExecuteReader();
            decimal sum = 0m;
            int count = 0;

            while (r.Read())
            {
                try
                {
                    var amt = Convert.ToDecimal(r.GetValue(0), CultureInfo.InvariantCulture);
                    sum += amt;
                    count++;
                }
                catch { }
            }

            return (sum, count);
        }

        // ========== KPI U GÓRY ==========
        private void UpdateCategoryKpis()
        {
            EnsureKpiRefs();

            var pb = FindName("PeriodBar") as Finly.Views.Controls.PeriodBarControl;
            if (pb == null) return;

            DateTime from = pb.StartDate;
            DateTime to = pb.EndDate;
            if (from > to) (from, to) = (to, from);

            var counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            var sums = new Dictionary<string, decimal>(StringComparer.CurrentCultureIgnoreCase);
            decimal totalSum = 0m;

            bool incExp = _mode == CategoryMode.All || _mode == CategoryMode.Expenses;
            bool incInc = _mode == CategoryMode.All || _mode == CategoryMode.Incomes;

            try
            {
                AccumulateCategoryAggregates(from, to, counts, sums, ref totalSum, incExp, incInc);
            }
            catch { }

            // Najczęściej używana
            if (counts.Count > 0)
            {
                var mostUsed = counts.OrderByDescending(kv => kv.Value).First();
                if (_mostUsedName != null) _mostUsedName.Text = mostUsed.Key;
                if (_mostUsedCount != null) _mostUsedCount.Text = $"Liczba transakcji: {mostUsed.Value}";
                if (_mostUsedColor != null) _mostUsedColor.Fill = GetBrushForName(mostUsed.Key);
            }
            else
            {
                if (_mostUsedName != null) _mostUsedName.Text = "Brak danych dla wybranego okresu";
                if (_mostUsedCount != null) _mostUsedCount.Text = string.Empty;
                if (_mostUsedColor != null) _mostUsedColor.Fill = Brushes.Transparent;
            }

            // Najdroższa
            if (sums.Count > 0)
            {
                var mostExp = sums.OrderByDescending(kv => kv.Value).First();
                if (_mostExpName != null) _mostExpName.Text = mostExp.Key;
                if (_mostExpAmount != null) _mostExpAmount.Text = mostExp.Value.ToString("N2", CultureInfo.CurrentCulture) + " zł";

                var pctRaw = totalSum > 0m ? (double)(mostExp.Value / totalSum * 100m) : 0.0;
                var pct = RoundToHalfPercent(pctRaw);

                if (_mostExpPercentBar != null) _mostExpPercentBar.Value = pct;
                if (_mostExpPercentText != null)
                    _mostExpPercentText.Text = pct.ToString("N1", CultureInfo.CurrentCulture) + "% wszystkich transakcji";
            }
            else
            {
                if (_mostExpName != null) _mostExpName.Text = "Brak danych dla wybranego okresu";
                if (_mostExpAmount != null) _mostExpAmount.Text = string.Empty;
                if (_mostExpPercentBar != null) _mostExpPercentBar.Value = 0;
                if (_mostExpPercentText != null) _mostExpPercentText.Text = string.Empty;
            }

            // Liczniki kategorii
            var allCats = DatabaseService.GetCategoriesByUser(_uid) ?? new List<string>();
            _totalCategoriesCount = allCats.Count;

            var activeSet = new HashSet<string>(
                sums.Where(kv => kv.Value > 0).Select(kv => kv.Key),
                StringComparer.CurrentCultureIgnoreCase);

            _activeCategoriesCount = allCats.Count(c => activeSet.Contains(c));
            _inactiveCategoriesCount = Math.Max(0, _totalCategoriesCount - _activeCategoriesCount);

            if (_totalCats != null) _totalCats.Text = _totalCategoriesCount.ToString(CultureInfo.CurrentCulture);
            if (_activeCats != null) _activeCats.Text = _activeCategoriesCount.ToString(CultureInfo.CurrentCulture);
            if (_inactiveCats != null) _inactiveCats.Text = _inactiveCategoriesCount.ToString(CultureInfo.CurrentCulture);
        }

        // ========== ŁADOWANIE LISTY ==========
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
                    try { brush = (Brush)(new BrushConverter().ConvertFromString(color)!); }
                    catch { brush = GetBrushForName(name); }
                }

                _categories.Add(new CategoryVm
                {
                    Id = id,
                    Name = name,
                    ColorBrush = brush,
                    ColorHex = string.IsNullOrWhiteSpace(color) ? null : color,
                    Icon = icon
                });
            }

            // opisy + kolory z bazy (best-effort)
            try
            {
                using var con = DatabaseService.GetConnection();
                con.Open();

                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT Id, Description, Color FROM Categories WHERE UserId=@u;";
                cmd.Parameters.AddWithValue("@u", _uid);

                using var reader = cmd.ExecuteReader();
                var mapDesc = new Dictionary<int, string?>();
                var mapColor = new Dictionary<int, string?>();

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

            // jeśli usunięto wybraną kategorię
            if (_selectedCategory != null && !_categories.Any(c => c.Id == _selectedCategory.Id))
                _selectedCategory = null;

            LoadSelectedCategoryDetails();
        }

        // ========== DODAJ / EDYTUJ (DIALOG) ==========
        private void AddCategoryDialog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new EditCategoryDialog { Owner = Window.GetWindow(this) };
            dlg.LoadForAdd();

            if (dlg.ShowDialog() != true) return;

            var vm = dlg.Category;
            var name = (vm.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            try
            {
                int id = DatabaseService.GetOrCreateCategoryId(_uid, name);

                string? colorToSave = string.IsNullOrWhiteSpace(vm.ColorHex) ? null : vm.ColorHex;
                DatabaseService.UpdateCategoryFull(id, _uid, name, colorToSave, vm.Icon);

                try
                {
                    using var con = DatabaseService.GetConnection();
                    con.Open();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "UPDATE Categories SET Description=@d WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@d", (object?)vm.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
                catch { }

                ToastService.Success("Dodano kategorię.");

                LoadCategories();
                UpdateCategoryKpis();
                UpdateCategoryStats();

                var again = _categories.FirstOrDefault(x => x.Id == id);
                if (again != null) SelectCategory(again);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd dodawania: " + ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
                OpenEditDialogFor(vm);
        }

        private void OpenEditDialogFor(CategoryVm vm)
        {
            var dlg = new EditCategoryDialog { Owner = Window.GetWindow(this) };
            dlg.LoadForEdit(vm.Id, vm.Name, vm.Description, vm.ColorHex, vm.Icon);

            if (dlg.ShowDialog() != true) return;

            var data = dlg.Category;
            var newName = (data.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;

            try
            {
                string? colorToSave = string.IsNullOrWhiteSpace(data.ColorHex) ? null : data.ColorHex;
                DatabaseService.UpdateCategoryFull(vm.Id, _uid, newName, colorToSave, data.Icon ?? vm.Icon);

                try
                {
                    using var con = DatabaseService.GetConnection();
                    con.Open();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "UPDATE Categories SET Description=@d WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@d", (object?)data.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@id", vm.Id);
                    cmd.ExecuteNonQuery();
                }
                catch { }

                ToastService.Success("Zapisano zmiany kategorii.");

                LoadCategories();

                var again = _categories.FirstOrDefault(x => x.Id == vm.Id);
                if (again != null) SelectCategory(again);

                UpdateCategoryKpis();
                UpdateCategoryStats();
                LoadSelectedCategoryDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd zapisu: " + ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========== USUWANIE ==========
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

                ToastService.Success("Kategoria usunięta.");

                if (_selectedCategory != null && _selectedCategory.Id == vm.Id)
                    _selectedCategory = null;

                LoadCategories();
                UpdateCategoryKpis();
                UpdateCategoryStats();
                LoadSelectedCategoryDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd usuwania: " + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========== ZAZNACZANIE KAFELKA ==========
        private void CategoryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // jeśli kliknięto w przycisk, nie zaznaczaj
            if (e.OriginalSource is DependencyObject d)
            {
                var parent = d;
                while (parent != null)
                {
                    if (parent is Button) return;
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }

            if (sender is FrameworkElement fe && fe.DataContext is CategoryVm vm)
                SelectCategory(vm);
        }

        private void SelectCategory(CategoryVm vm)
        {
            foreach (var c in _categories)
                c.IsSelected = ReferenceEquals(c, vm);

            _selectedCategory = vm;

            RaisePropertyChanged(nameof(HasSelectedCategory));
            RaisePropertyChanged(nameof(SelectedCategoryName));
            RaisePropertyChanged(nameof(SelectedCategoryDescription));
            RaisePropertyChanged(nameof(SelectedCategoryColorBrush));

            LoadSelectedCategoryDetails();
        }

        // ========== SZCZEGÓŁY WYBRANEJ KATEGORII ==========
        private void LoadSelectedCategoryDetails()
        {
            SelectedCategoryTotalAmount = 0m;
            SelectedCategoryTransactionCount = 0;

            _lastTransaction = null;
            RaisePropertyChanged(nameof(HasLastTransaction));
            RaisePropertyChanged(nameof(LastTransactionDateText));
            RaisePropertyChanged(nameof(LastTransactionDescription));
            RaisePropertyChanged(nameof(LastTransactionAmountText));

            RaisePropertyChanged(nameof(HasSelectedCategory));
            RaisePropertyChanged(nameof(SelectedCategoryName));
            RaisePropertyChanged(nameof(SelectedCategoryDescription));
            RaisePropertyChanged(nameof(SelectedCategoryColorBrush));

            if (_selectedCategory == null) return;

            int catId = _selectedCategory.Id;
            if (catId <= 0)
            {
                int? byName = DatabaseService.GetCategoryIdByName(_uid, _selectedCategory.Name);
                if (!byName.HasValue) return;
                catId = byName.Value;
            }

            var pb = FindName("PeriodBar") as Finly.Views.Controls.PeriodBarControl;
            if (pb == null) return;

            DateTime from = pb.StartDate;
            DateTime to = pb.EndDate;
            if (from > to) (from, to) = (to, from);

            bool incExp = _mode == CategoryMode.All || _mode == CategoryMode.Expenses;
            bool incInc = _mode == CategoryMode.All || _mode == CategoryMode.Incomes;

            var (sum, cnt) = GetCategoryTotalsById(from, to, catId, incExp, incInc);

            SelectedCategoryTotalAmount = sum;
            SelectedCategoryTransactionCount = cnt;

            try { LoadLastTransactionForCategory(catId); } catch { }
        }

        private void LoadLastTransactionForCategory(int categoryId)
        {
            _lastTransaction = null;

            var pb = FindName("PeriodBar") as Finly.Views.Controls.PeriodBarControl;
            if (pb == null) return;

            DateTime from = pb.StartDate;
            DateTime to = pb.EndDate;
            if (from > to) (from, to) = (to, from);

            using var con = DatabaseService.GetConnection();
            con.Open();
            using var cmd = con.CreateCommand();

            var dateFrom = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dateTo = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (_mode == CategoryMode.Expenses)
            {
                cmd.CommandText = @"
SELECT Date, ABS(Amount) as Amount, IFNULL(Description,'') as Description
FROM Expenses
WHERE UserId=@u AND CategoryId=@c AND Date>=@from AND Date<=@to
ORDER BY Date DESC
LIMIT 1;";
            }
            else if (_mode == CategoryMode.Incomes)
            {
                cmd.CommandText = @"
SELECT Date, ABS(Amount) as Amount, IFNULL(Description,'') as Description
FROM Incomes
WHERE UserId=@u AND CategoryId=@c AND Date>=@from AND Date<=@to
ORDER BY Date DESC
LIMIT 1;";
            }
            else
            {
                cmd.CommandText = @"
SELECT Date, Amount, Description FROM (
    SELECT Date, ABS(Amount) as Amount, IFNULL(Description,'') as Description
    FROM Expenses
    WHERE UserId=@u AND CategoryId=@c AND Date>=@from AND Date<=@to
    UNION ALL
    SELECT Date, ABS(Amount) as Amount, IFNULL(Description,'') as Description
    FROM Incomes
    WHERE UserId=@u AND CategoryId=@c AND Date>=@from AND Date<=@to
)
ORDER BY Date DESC
LIMIT 1;";
            }

            cmd.Parameters.AddWithValue("@u", _uid);
            cmd.Parameters.AddWithValue("@c", categoryId);
            cmd.Parameters.AddWithValue("@from", dateFrom);
            cmd.Parameters.AddWithValue("@to", dateTo);

            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                try
                {
                    var dateStr = r.IsDBNull(0) ? "" : r.GetString(0);
                    var dt = DateTime.TryParse(dateStr, out var parsed) ? parsed : DateTime.MinValue;

                    decimal amt = 0m;
                    try { amt = Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture); } catch { }

                    var desc = r.IsDBNull(2) ? "" : r.GetString(2);

                    _lastTransaction = new CategoryTransactionRow
                    {
                        Date = dt,
                        Amount = amt,
                        Description = desc
                    };
                }
                catch
                {
                    _lastTransaction = null;
                }
            }

            RaisePropertyChanged(nameof(HasLastTransaction));
            RaisePropertyChanged(nameof(LastTransactionDateText));
            RaisePropertyChanged(nameof(LastTransactionDescription));
            RaisePropertyChanged(nameof(LastTransactionAmountText));
        }

        // ========== STRUKTURA KATEGORII ==========
        private void UpdateCategoryStats()
        {
            var pb = FindName("PeriodBar") as Finly.Views.Controls.PeriodBarControl;
            if (pb == null) return;

            DateTime from = pb.StartDate;
            DateTime to = pb.EndDate;
            if (from > to) (from, to) = (to, from);

            bool incExp = _mode == CategoryMode.All || _mode == CategoryMode.Expenses;
            bool incInc = _mode == CategoryMode.All || _mode == CategoryMode.Incomes;

            var list = new List<(string name, decimal amount, int count)>();

            foreach (var cat in _categories)
            {
                var (sum, cnt) = GetCategoryTotalsById(from, to, cat.Id, incExp, incInc);
                list.Add((cat.Name, sum, cnt));
            }

            var totalAmount = list.Sum(x => x.amount);

            CategoryShares.Clear();
            foreach (var it in list.OrderByDescending(x => x.amount))
            {
                var pctRaw = totalAmount > 0 ? (double)(it.amount / totalAmount) * 100.0 : 0.0;
                var pct = RoundToHalfPercent(pctRaw);

                CategoryShares.Add(new CategoryShareItem
                {
                    Name = it.name,
                    Amount = it.amount,
                    Percent = pct,
                    Dominant = pct >= 20.0
                });
            }

            var positive = list.Where(x => x.amount > 0).ToList();

            TopCategories.Clear();
            foreach (var it in positive.OrderByDescending(x => x.amount).Take(3))
            {
                var pctRaw = totalAmount > 0 ? (double)(it.amount / totalAmount) * 100.0 : 0.0;
                var pct = RoundToHalfPercent(pctRaw);

                TopCategories.Add(new CategoryShareItem { Name = it.name, Amount = it.amount, Percent = pct });
            }
            FillWithPlaceholders(TopCategories, 3);

            BottomCategories.Clear();
            foreach (var it in positive.OrderBy(x => x.amount).Take(3))
            {
                var pctRaw = totalAmount > 0 ? (double)(it.amount / totalAmount) * 100.0 : 0.0;
                var pct = RoundToHalfPercent(pctRaw);

                BottomCategories.Add(new CategoryShareItem { Name = it.name, Amount = it.amount, Percent = pct });
            }
            FillWithPlaceholders(BottomCategories, 3);

            RaisePropertyChanged(nameof(CategoryShares));
            RaisePropertyChanged(nameof(TopCategories));
            RaisePropertyChanged(nameof(BottomCategories));
        }

        private static void FillWithPlaceholders(ObservableCollection<CategoryShareItem> col, int targetCount)
        {
            while (col.Count < targetCount)
                col.Add(new CategoryShareItem { Name = "(brak)", Amount = 0m, Percent = 0.0, Dominant = false });
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

        private void ClearAllDeleteConfirmPanels()
        {
            foreach (var c in _categories)
                if (c.IsDeleteConfirmVisible) c.IsDeleteConfirmVisible = false;
        }

        // ============================================================
        //  SEED domyślnych kategorii TYLKO RAZ na użytkownika
        // ============================================================
        private void EnsureDefaultCategoriesOncePerUser()
        {
            try
            {
                using var con = DatabaseService.GetConnection();
                con.Open();

                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS AppSettings(
    Key TEXT PRIMARY KEY,
    Value TEXT
);";
                    cmd.ExecuteNonQuery();
                }

                var key = $"categories_seeded_u{_uid}";

                var alreadySeeded = false;
                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key=@k LIMIT 1;";
                    cmd.Parameters.AddWithValue("@k", key);
                    var v = cmd.ExecuteScalar() as string;
                    alreadySeeded = string.Equals(v, "1", StringComparison.OrdinalIgnoreCase);
                }

                if (alreadySeeded) return;

                var existing = DatabaseService.GetCategoriesByUser(_uid) ?? new List<string>();

                var defaultExpenses = new[] { "Jedzenie", "Transport", "Mieszkanie", "Rachunki", "Rozrywka", "Zdrowie", "Ubrania" };
                var defaultIncomes = new[] { "Wynagrodzenie", "Prezent", "Zwrot", "Inne" };

                foreach (var c in defaultExpenses.Concat(defaultIncomes))
                {
                    if (!existing.Any(e => string.Equals(e, c, StringComparison.OrdinalIgnoreCase)))
                        DatabaseService.GetOrCreateCategoryId(_uid, c);
                }

                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText = "INSERT OR REPLACE INTO AppSettings(Key,Value) VALUES(@k,'1');";
                    cmd.Parameters.AddWithValue("@k", key);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        // ===== MODELE =====
        public class CategoryVm : INotifyPropertyChanged
        {
            private Brush _colorBrush = Brushes.Gray;
            private bool _isDeleteConfirmVisible;
            private string? _description;
            private string? _colorHex;
            private bool _isSelected;

            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;

            public Brush ColorBrush
            {
                get => _colorBrush;
                set { _colorBrush = value; OnPropertyChanged(nameof(ColorBrush)); }
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

        // STABILNY lookup zasobów: tylko App.TryFindResource (jak w Dashboard)
        private static Brush GetAppBrush(string key, Brush fallback)
        {
            try
            {
                return Application.Current?.TryFindResource(key) as Brush ?? fallback;
            }
            catch
            {
                return fallback;
            }
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
            public bool Dominant { get; set; }
        }
    }
}
