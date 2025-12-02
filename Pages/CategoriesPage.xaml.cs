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

        // Główna lista kategorii (lewa kolumna)
        private readonly ObservableCollection<CategoryVm> _categories = new();
        public ObservableCollection<CategoryVm> Categories => _categories;

        private CollectionView _categoriesView;

        // transakcje dla dolnego panelu
        private readonly ObservableCollection<CategoryTransactionRow> _lastTransactions = new();

        // ====== stan filtrowania ======
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

        // ====== stan edycji / wyboru ======
        private CategoryVm? _editingVm;
        private CategoryVm? _selectedCategory;
        private string _selectedColorHex = string.Empty;
        private string? _categoryDescriptionDraft;
        private string? _selectedIcon;

        // ====== dane do środkowych paneli (binding) ======
        public ObservableCollection<CategoryShareItem> CategoryShares { get; } = new();
        public ObservableCollection<CategoryShareItem> TopCategories { get; } = new();
        public ObservableCollection<CategoryShareItem> BottomCategories { get; } = new();

        private string _activeCategoriesSummary = string.Empty;
        public string ActiveCategoriesSummary
        {
            get => _activeCategoriesSummary;
            set
            {
                _activeCategoriesSummary = value;
                RaisePropertyChanged(nameof(ActiveCategoriesSummary));
            }
        }

        // – dane wybranej kategorii / porównanie okresów
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

        private decimal _selectedCategoryAverageMonthly;
        public decimal SelectedCategoryAverageMonthly
        {
            get => _selectedCategoryAverageMonthly;
            private set
            {
                _selectedCategoryAverageMonthly = value;
                RaisePropertyChanged(nameof(SelectedCategoryAverageMonthly));
            }
        }

        private int _selectedCategoryTransactionCount;
        public int SelectedCategoryTransactionCount
        {
            get => _selectedCategoryTransactionCount;
            private set
            {
                _selectedCategoryTransactionCount = value;
                RaisePropertyChanged(nameof(SelectedCategoryTransactionCount));
            }
        }

        public ObservableCollection<CategoryTransactionRow> SelectedCategoryRecentTransactions { get; } = new();

        public decimal AmountDifference => CurrentPeriodAmount - PreviousPeriodAmount;

        public double PercentageDifference
        {
            get
            {
                if (PreviousPeriodAmount == 0) return 0;
                return (double)((CurrentPeriodAmount - PreviousPeriodAmount) / PreviousPeriodAmount) * 100.0;
            }
        }

        // ====== INotifyPropertyChanged ======
        public event PropertyChangedEventHandler? PropertyChanged;
        private void RaisePropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // KPI refs (górne 3 kafelki)
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

            DataContext = this;

            Loaded += CategoriesPage_Loaded;

            _categoriesView = (CollectionView)System.Windows.Data.CollectionViewSource.GetDefaultView(_categories);
            _categoriesView.Filter = CategoryFilter;

            PeriodBar.RangeChanged += PeriodBar_RangeChanged;

            LastTransactionsList.ItemsSource = _lastTransactions;
        }

        // ====== FILTROWANIE ======
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

        // ====== ŻYCIE STRONY ======
        private void CategoriesPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureKpiRefs();
                EnsureDefaultCategories();
                LoadCategories();
                UpdateCategoryKpis();
                ClearAllDeleteConfirmPanels();
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
            UpdateCategoryKpis();

            // bardzo ważne: po zmianie okresu odśwież szczegóły WYBRANEJ kategorii
            if (_selectedCategory != null)
                LoadSelectedCategoryDetails();
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

        // ====== ŁADOWANIE KATEGORII Z BAZY ======
        private void LoadCategories()
        {
            _categories.Clear();

            try
            {
                using var con = DatabaseService.GetConnection();
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
SELECT Id, Name, Color, Icon, Description
FROM Categories
WHERE UserId=@u AND (IsArchived = 0 OR IsArchived IS NULL)
ORDER BY Name;";
                cmd.Parameters.AddWithValue("@u", _uid);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    string name = reader.GetString(1);
                    string? color = reader.IsDBNull(2) ? null : reader.GetString(2);
                    string? icon = reader.IsDBNull(3) ? null : reader.GetString(3);
                    string? desc = reader.IsDBNull(4) ? null : reader.GetString(4);

                    Brush brush = string.IsNullOrWhiteSpace(color)
                        ? GetBrushForName(name)
                        : (Brush)new BrushConverter().ConvertFromString(color)!;

                    _categories.Add(new CategoryVm
                    {
                        Id = id,
                        Name = name,
                        ColorBrush = brush,
                        Icon = icon,
                        Description = desc
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd ładowania kategorii: " + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            ApplyCategoriesFilter();
            ClearAllDeleteConfirmPanels();
            RaisePropertyChanged(nameof(Categories));
        }

        // ====== KPI + STRUKTURA WYDATKÓW ======
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

                foreach (DataRow row in dt.Rows)
                {
                    try
                    {
                        var name = (row[6]?.ToString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(name) || name == "(brak)") continue;

                        decimal amount;
                        var val = row[3];
                        if (val is decimal dec) amount = dec;
                        else if (val is double d) amount = (decimal)d;
                        else amount = Convert.ToDecimal(val);
                        amount = Math.Abs(amount);

                        counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
                        sums[name] = sums.TryGetValue(name, out var s) ? s + amount : amount;
                        totalSum += amount;
                    }
                    catch { }
                }

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

                // Liczby kategorii
                var allCats = DatabaseService.GetCategoriesByUser(_uid) ?? new List<string>();
                int total = allCats.Count;
                var activeSet = new HashSet<string>(
                    sums.Where(kv => kv.Value > 0).Select(kv => kv.Key),
                    StringComparer.CurrentCultureIgnoreCase);
                int active = allCats.Count(c => activeSet.Contains(c));
                int inactive = Math.Max(0, total - active);

                if (_totalCats != null) _totalCats.Text = total.ToString(CultureInfo.CurrentCulture);
                if (_activeCats != null) _activeCats.Text = active.ToString(CultureInfo.CurrentCulture);
                if (_inactiveCats != null) _inactiveCats.Text = inactive.ToString(CultureInfo.CurrentCulture);

                ActiveCategoriesSummary = $"Aktywne kategorie: {active}/{total}";

                // Struktura wydatków (kafelek "Struktura wydatków według kategorii")
                CategoryShares.Clear();
                TopCategories.Clear();
                BottomCategories.Clear();

                if (sums.Count > 0)
                {
                    foreach (var kv in sums.OrderByDescending(k => k.Value))
                    {
                        var pct = totalSum > 0m ? (double)(kv.Value / totalSum * 100m) : 0.0;
                        CategoryShares.Add(new CategoryShareItem
                        {
                            Name = kv.Key,
                            Amount = kv.Value,
                            Percent = pct
                        });
                    }

                    var positive = sums.Where(kv => kv.Value > 0).ToList();

                    foreach (var kv in positive.OrderByDescending(k => k.Value).Take(3))
                    {
                        var pct = totalSum > 0m ? (double)(kv.Value / totalSum * 100m) : 0.0;
                        TopCategories.Add(new CategoryShareItem
                        {
                            Name = kv.Key,
                            Amount = kv.Value,
                            Percent = pct
                        });
                    }

                    foreach (var kv in positive.OrderBy(k => k.Value).Take(3))
                    {
                        var pct = totalSum > 0m ? (double)(kv.Value / totalSum * 100m) : 0.0;
                        BottomCategories.Add(new CategoryShareItem
                        {
                            Name = kv.Key,
                            Amount = kv.Value,
                            Percent = pct
                        });
                    }
                }

                // powiadom bindingi, że kolekcje się zmieniły
                RaisePropertyChanged(nameof(CategoryShares));
                RaisePropertyChanged(nameof(TopCategories));
                RaisePropertyChanged(nameof(BottomCategories));
            }
            catch
            {
                CategoryShares.Clear();
                TopCategories.Clear();
                BottomCategories.Clear();
                ActiveCategoriesSummary = string.Empty;
            }
        }

        // ====== DODAWANIE / EDYCJA / USUWANIE ======
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

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CategoryVm vm)
            {
                _editingVm = vm;
                _selectedCategory = vm;

                EditNameBox.Text = vm.Name;
                EditDescriptionBox.Text = vm.Description ?? string.Empty;

                EditPanel.Visibility = Visibility.Visible;
                EditPanelMessage.Text = string.Empty;

                _selectedColorHex = (vm.ColorBrush as SolidColorBrush)?.Color.ToString() ?? string.Empty;

                // po kliknięciu w edycję od razu pokaż szczegóły wybranej kategorii
                ShowCategoryPanels();
                LoadSelectedCategoryDetails();
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

                MessageBox.Show("Kategoria usunięta.", "OK",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                LoadCategories();
                HideCategoryPanels();
                UpdateCategoryKpis();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd usuwania: " + ex.Message,
                    "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
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
                _editingVm.Description = _categoryDescriptionDraft;

                if (!string.IsNullOrWhiteSpace(_selectedColorHex))
                {
                    var brush = (Brush)new BrushConverter().ConvertFromString(_selectedColorHex)!;
                    _editingVm.ColorBrush = brush;
                }

                EditPanelMessage.Text = "Zapisano.";
                EditPanel.Visibility = Visibility.Collapsed;
                _editingVm = null;

                LoadCategories();
                UpdateCategoryKpis();

                // odśwież szczegóły (kafelki po prawej)
                if (_selectedCategory != null)
                    LoadSelectedCategoryDetails();
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

        // ====== WYBÓR KATEGORII (kliknięcie na kafelek z listy) ======
        private void CategoryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
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

        // ====== LOGIKA DLA SZCZEGÓŁÓW KATEGORII (środkowe kafelki + dół) ======
        private void LoadSelectedCategoryDetails()
        {
            if (_selectedCategory == null)
            {
                CurrentPeriodAmount = 0;
                PreviousPeriodAmount = 0;
                SelectedCategoryAverageMonthly = 0;
                SelectedCategoryTransactionCount = 0;
                SelectedCategoryRecentTransactions.Clear();
                _lastTransactions.Clear();
                HideCategoryPanels();
                return;
            }

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

            // bieżący okres
            var expensesDt = DatabaseService.GetExpenses(_uid, from, to, catId, null, null);
            decimal sum = 0m;
            int count = 0;
            foreach (DataRow row in expensesDt.Rows)
            {
                try
                {
                    sum += Math.Abs(Convert.ToDecimal(row[3]));
                    count++;
                }
                catch { }
            }

            SummaryTotalText.Text = "Suma wydatków w tym okresie: " + sum.ToString("N2") + " zł";

            double monthsSpan = Math.Max(1.0, (to - from).TotalDays / 30.0);
            decimal monthlyAvg = monthsSpan <= 0 ? 0 : sum / (decimal)monthsSpan;
            SummaryMonthlyAvgText.Text = "Średni wydatek w tej kategorii (miesięcznie): " + monthlyAvg.ToString("N2") + " zł";
            SummaryTxnCountText.Text = count + (count == 1 ? " transakcja" : " transakcji");

            // dane pod binding (środkowy kafelek „Szczegóły kategorii”)
            CurrentPeriodAmount = sum;
            SelectedCategoryAverageMonthly = monthlyAvg;
            SelectedCategoryTransactionCount = count;

            // porównanie z poprzednim okresem (kafelek po prawej)
            var span = to - from;
            if (span <= TimeSpan.Zero) span = TimeSpan.FromDays(30);
            DateTime prevTo = from;
            DateTime prevFrom = from - span;
            var prevDt = DatabaseService.GetExpenses(_uid, prevFrom, prevTo, catId, null, null);
            decimal prevSum = 0m;
            foreach (DataRow row in prevDt.Rows)
            {
                try { prevSum += Math.Abs(Convert.ToDecimal(row[3])); }
                catch { }
            }
            PreviousPeriodAmount = prevSum;

            // ostatnie transakcje – dół strony + lista w środkowym kafelku
            _lastTransactions.Clear();
            SelectedCategoryRecentTransactions.Clear();

            var txList = DatabaseService.GetLastTransactionsForCategory(_uid, catId.Value, 12);
            foreach (var t in txList)
            {
                var row = new CategoryTransactionRow
                {
                    Date = t.Date,
                    Amount = t.Amount,
                    Description = t.Description
                };
                _lastTransactions.Add(row);
                SelectedCategoryRecentTransactions.Add(row);
            }

            _selectedColorHex = (_selectedCategory.ColorBrush as SolidColorBrush)?.Color.ToString() ?? string.Empty;

            // odśwież bindingi na wszelki wypadek
            RaisePropertyChanged(nameof(CurrentPeriodAmount));
            RaisePropertyChanged(nameof(PreviousPeriodAmount));
            RaisePropertyChanged(nameof(SelectedCategoryAverageMonthly));
            RaisePropertyChanged(nameof(SelectedCategoryTransactionCount));
            RaisePropertyChanged(nameof(SelectedCategoryRecentTransactions));
        }

        // ====== POMOCNICZE ======
        private Brush GetBrushForName(string name) => GetRandomBrush(name);

        private static Brush GetRandomBrush(string seed)
        {
            var palette = new[]
            {
                "#FFED7A1A","#FF3FA7D6","#FF7BC96F","#FFAF7AC5",
                "#FFF6BF26","#FF56C1A7","#FFCE6A6B","#FF9AA0A6"
            };
            var idx = Math.Abs(seed?.GetHashCode() ?? 0) % palette.Length;
            return (Brush)new BrushConverter().ConvertFromString(palette[idx])!;
        }

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
                // ignorujemy błędy seedowania
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

        // ====== VM pomocnicze ======
        public class CategoryVm : INotifyPropertyChanged
        {
            private Brush _colorBrush = Brushes.Gray;
            private bool _isDeleteConfirmVisible;
            private string? _description;

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
                set
                {
                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }

            public bool IsDeleteConfirmVisible
            {
                get => _isDeleteConfirmVisible;
                set
                {
                    _isDeleteConfirmVisible = value;
                    OnPropertyChanged(nameof(IsDeleteConfirmVisible));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string prop) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
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
            public decimal Amount { get; set; }
            public double Percent { get; set; }
        }
    }
}
