using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class CategoriesPage : UserControl, INotifyPropertyChanged
    {
        // ===== MODELE DANYCH (wewnętrzne, proste) =====

        public sealed class CategorySummary
        {
            public string Name { get; set; } = string.Empty;
            /// <summary>
            /// Np. "Wydatek", "Przychód", "Obie"
            /// </summary>
            public string TypeDisplay { get; set; } = string.Empty;
            public int EntryCount { get; set; }
            public decimal TotalAmount { get; set; }
            public double SharePercent { get; set; }
        }

        public sealed class CategoryTransaction
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public string Description { get; set; } = string.Empty;
        }

        // ===== POLA / WŁAŚCIWOŚCI BINDINGÓW =====

        private object? _selectedTypeFilter;
        private DateTime? _fromDate;
        private DateTime? _toDate;
        private string _searchText = string.Empty;
        private CategorySummary? _selectedCategory;

        public ObservableCollection<CategorySummary> CategoryItems { get; } =
            new ObservableCollection<CategorySummary>();

        public ObservableCollection<CategoryTransaction> LastTransactions { get; } =
            new ObservableCollection<CategoryTransaction>();

        public object? SelectedTypeFilter
        {
            get => _selectedTypeFilter;
            set
            {
                _selectedTypeFilter = value;
                OnPropertyChanged();
            }
        }

        public DateTime? FromDate
        {
            get => _fromDate;
            set
            {
                _fromDate = value;
                OnPropertyChanged();
            }
        }

        public DateTime? ToDate
        {
            get => _toDate;
            set
            {
                _toDate = value;
                OnPropertyChanged();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
            }
        }

        public CategorySummary? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged();
                LoadCategoryDetails(); // Za każdym razem, gdy użytkownik zmieni kategorię
            }
        }

        // ===== KONSTRUKTOR =====

        public CategoriesPage()
        {
            InitializeComponent();
            DataContext = this;

            // Domyślny zakres – ten miesiąc
            var now = DateTime.Now;
            FromDate = new DateTime(now.Year, now.Month, 1);
            ToDate = FromDate.Value.AddMonths(1).AddDays(-1);

            // Domyślny typ – "Wszystkie" (pierwsza pozycja ComboBoxa)
            SelectedTypeFilter = null; // ustawi się w UI

            // Tymczasowe dane do designu / testów
            LoadSampleData();
        }

        // ===== ŁADOWANIE DANYCH (DOCZEPIĆ DO DB) =====

        private void LoadSampleData()
        {
            CategoryItems.Clear();

            CategoryItems.Add(new CategorySummary
            {
                Name = "Jedzenie",
                TypeDisplay = "Wydatek",
                EntryCount = 42,
                TotalAmount = 1234.56m,
                SharePercent = 35.2
            });

            CategoryItems.Add(new CategorySummary
            {
                Name = "Transport",
                TypeDisplay = "Wydatek",
                EntryCount = 12,
                TotalAmount = 420.00m,
                SharePercent = 12.0
            });

            CategoryItems.Add(new CategorySummary
            {
                Name = "Wynagrodzenie",
                TypeDisplay = "Przychód",
                EntryCount = 2,
                TotalAmount = 5800.00m,
                SharePercent = 80.0
            });

            if (CategoryItems.Count > 0)
            {
                SelectedCategory = CategoryItems[0];
            }
        }

        /// <summary>
        /// Tutaj później podłączysz DB:
        /// - agregacja transakcji po kategorii
        /// - filtr: typ, data od/do, SearchText
        /// </summary>
        private void ReloadFromDatabase()
        {
            // TODO: podłącz DatabaseService i uzupełnij CategoryItems
            // Na razie zostawiamy sample.
            LoadSampleData();
        }

        private void LoadCategoryDetails()
        {
            LastTransactions.Clear();

            if (SelectedCategory == null)
                return;

            // TODO: w przyszłości pobierz REALNE transakcje z DB
            // Poniżej dummy-data jako przykład wyglądu.
            LastTransactions.Add(new CategoryTransaction
            {
                Date = DateTime.Now.Date.AddDays(-1),
                Amount = 58.90m,
                Description = "Biedronka – zakupy spożywcze"
            });
            LastTransactions.Add(new CategoryTransaction
            {
                Date = DateTime.Now.Date.AddDays(-3),
                Amount = 120.00m,
                Description = "Pizzeria X – obiad"
            });
            LastTransactions.Add(new CategoryTransaction
            {
                Date = DateTime.Now.Date.AddDays(-7),
                Amount = 230.50m,
                Description = "Lidl – większe zakupy"
            });
        }

        // ===== OBSŁUGA PRZYCISKÓW FILTRÓW =====

        private void ApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            // Docelowo: wywołaj ReloadFromDatabase() z parametrami filtrów.
            ReloadFromDatabase();
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            SelectedTypeFilter = null;
            SearchText = string.Empty;

            var now = DateTime.Now;
            FromDate = new DateTime(now.Year, now.Month, 1);
            ToDate = FromDate.Value.AddMonths(1).AddDays(-1);

            ReloadFromDatabase();
        }

        private void PresetToday_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            FromDate = today;
            ToDate = today;
            ReloadFromDatabase();
        }

        private void PresetThisMonth_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Now;
            FromDate = new DateTime(now.Year, now.Month, 1);
            ToDate = FromDate.Value.AddMonths(1).AddDays(-1);
            ReloadFromDatabase();
        }

        private void PresetThisYear_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Now;
            FromDate = new DateTime(now.Year, 1, 1);
            ToDate = new DateTime(now.Year, 12, 31);
            ReloadFromDatabase();
        }

        // ===== AKCJE NA KATEGORIACH =====

        private void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            // TODO: otwórz okno/dialog dodawania kategorii.
            MessageBox.Show("Tu otworzysz dialog dodawania kategorii.", "Dodaj kategorię");
        }

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCategory == null)
            {
                MessageBox.Show("Najpierw wybierz kategorię z listy.", "Brak wyboru");
                return;
            }

            // TODO: otwórz dialog edycji (nazwa, typ, kolor itd.).
            MessageBox.Show($"Tu edytujesz kategorię: {SelectedCategory.Name}", "Edytuj kategorię");
        }

        private void MergeCategories_Click(object sender, RoutedEventArgs e)
        {
            // TODO:
            // 1) okno wyboru: Kategoria źródłowa + docelowa
            // 2) update transakcji w DB
            // 3) oznaczenie starej jako archiwalnej
            MessageBox.Show("Tu zrobimy kreator scalania kategorii.", "Scal kategorie");
        }

        private void ArchiveCategory_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCategory == null)
            {
                MessageBox.Show("Najpierw wybierz kategorię z listy.", "Brak wyboru");
                return;
            }

            // TODO: flaga 'archived' w DB + odświeżenie widoku.
            MessageBox.Show($"Tu oznaczysz kategorię '{SelectedCategory.Name}' jako archiwalną.", "Archiwizuj kategorię");
        }

        // ===== INotifyPropertyChanged =====

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

