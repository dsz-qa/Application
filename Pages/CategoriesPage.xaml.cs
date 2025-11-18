using Finly.Services;
using Finly.Views;
using Finly.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class CategoriesPage : UserControl
    {
        private readonly int _userId;

        // Reprezentacja wiersza w tabeli kategorii
        private sealed class CategoryRow
        {
            public int? CategoryId { get; set; }       // dla wydatków
            public string? IncomeSource { get; set; }  // dla przychodów

            public string Name { get; set; } = "";
            public string TypeDisplay { get; set; } = "";   // "Wydatek" / "Przychód"
            public int EntryCount { get; set; }
            public decimal TotalAmount { get; set; }
            public double SharePercent { get; set; }

            public bool IsIncome => !string.IsNullOrEmpty(IncomeSource);
        }

        public CategoriesPage()
        {
            InitializeComponent();
            _userId = UserService.GetCurrentUserId();
            Loaded += CategoriesPage_Loaded;
        }

        // ================== ŻYCIE STRONY ==================

        private void CategoriesPage_Loaded(object? sender, RoutedEventArgs e)
        {
            InitFilters();
            RefreshCategories();
        }

        private void InitFilters()
        {
            if (TypeCombo != null && TypeCombo.SelectedIndex < 0)
                TypeCombo.SelectedIndex = 0; // "Wszystkie"

            var today = DateTime.Today;
            var start = new DateTime(today.Year, today.Month, 1);

            if (!FromDatePicker.SelectedDate.HasValue)
                FromDatePicker.SelectedDate = start;

            if (!ToDatePicker.SelectedDate.HasValue)
                ToDatePicker.SelectedDate = today;
        }

        private void GetCurrentDateRange(out DateTime from, out DateTime to)
        {
            var today = DateTime.Today;
            from = FromDatePicker.SelectedDate ?? today;
            to = ToDatePicker.SelectedDate ?? today;

            if (to < from)
                (from, to) = (to, from);
        }

        private string GetTypeFilterText()
        {
            if (TypeCombo.SelectedItem is ComboBoxItem cbi)
                return cbi.Content?.ToString() ?? "Wszystkie";
            return "Wszystkie";
        }

        // ================== GŁÓWNE ODŚWIEŻANIE ==================

        private void RefreshCategories(int? selectCategoryId = null, string? selectIncomeSource = null)
        {
            if (_userId <= 0) return;

            GetCurrentDateRange(out var from, out var to);
            var search = (SearchBox.Text ?? "").Trim();
            var typeText = GetTypeFilterText();

            bool includeExpenses = typeText is "Wszystkie" or "Wydatki" or "Obie";
            bool includeIncomes = typeText is "Wszystkie" or "Przychody" or "Obie";

            var dict = new Dictionary<string, CategoryRow>(StringComparer.OrdinalIgnoreCase);
            decimal totalAll = 0m;

            // ===== WYDATKI =====
            if (includeExpenses)
            {
                DataTable exp = DatabaseService.GetExpenses(_userId, from, to);

                foreach (DataRow row in exp.Rows)
                {
                    int catId = 0;
                    try
                    {
                        if (row["CategoryId"] != DBNull.Value)
                            catId = Convert.ToInt32(row["CategoryId"]);
                    }
                    catch { catId = 0; }

                    string name = exp.Columns.Contains("CategoryName")
                        ? (row["CategoryName"]?.ToString() ?? "(brak kategorii)")
                        : "(brak kategorii)";

                    if (string.IsNullOrWhiteSpace(name))
                        name = "(brak kategorii)";

                    var key = $"E|{catId}|{name}";

                    if (!dict.TryGetValue(key, out var cr))
                    {
                        cr = new CategoryRow
                        {
                            CategoryId = catId == 0 ? (int?)null : catId,
                            IncomeSource = null,
                            Name = name,
                            TypeDisplay = "Wydatek",
                            EntryCount = 0,
                            TotalAmount = 0m
                        };
                        dict[key] = cr;
                    }

                    decimal amt = 0m;
                    try { amt = Math.Abs(Convert.ToDecimal(row["Amount"])); } catch { }

                    cr.EntryCount++;
                    cr.TotalAmount += amt;
                    totalAll += amt;
                }
            }

            // ===== PRZYCHODY =====
            if (includeIncomes)
            {
                DataTable inc = DatabaseService.GetIncomes(_userId, from, to, null);

                foreach (DataRow row in inc.Rows)
                {
                    string source = inc.Columns.Contains("Source")
                        ? (row["Source"]?.ToString() ?? "Przychody")
                        : "Przychody";

                    if (string.IsNullOrWhiteSpace(source))
                        source = "Przychody";

                    var key = $"I|{source}";

                    if (!dict.TryGetValue(key, out var cr))
                    {
                        cr = new CategoryRow
                        {
                            CategoryId = null,
                            IncomeSource = source,
                            Name = source,
                            TypeDisplay = "Przychód",
                            EntryCount = 0,
                            TotalAmount = 0m
                        };
                        dict[key] = cr;
                    }

                    decimal amt = 0m;
                    try { amt = Math.Abs(Convert.ToDecimal(row["Amount"])); } catch { }

                    cr.EntryCount++;
                    cr.TotalAmount += amt;
                    totalAll += amt;
                }
            }

            // ===== DOŁÓŻ WSZYSTKIE KATEGORIE UŻYTKOWNIKA (nawet z 0 wpisów) =====
            if (includeExpenses)
            {
                DataTable cats = DatabaseService.GetCategories(_userId);
                foreach (DataRow crow in cats.Rows)
                {
                    int id;
                    try { id = Convert.ToInt32(crow["Id"]); }
                    catch { continue; }

                    string name = crow["Name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var key = $"E|{id}|{name}";
                    if (!dict.ContainsKey(key))
                    {
                        dict[key] = new CategoryRow
                        {
                            CategoryId = id,
                            IncomeSource = null,
                            Name = name,
                            TypeDisplay = "Wydatek",
                            EntryCount = 0,
                            TotalAmount = 0m,
                            SharePercent = 0
                        };
                    }
                }
            }

            // ===== FILTR NAZWY + udział % =====
            IEnumerable<CategoryRow> rowsEnum = dict.Values;

            if (!string.IsNullOrWhiteSpace(search))
            {
                rowsEnum = rowsEnum.Where(r =>
                    r.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var rows = rowsEnum.ToList();

            if (totalAll > 0m)
            {
                foreach (var r in rows)
                    r.SharePercent = (double)(r.TotalAmount / totalAll * 100m);
            }
            else
            {
                foreach (var r in rows)
                    r.SharePercent = 0;
            }

            rows = rows
                .OrderByDescending(r => r.TotalAmount)
                .ThenBy(r => r.Name)
                .ToList();

            CategoriesGrid.ItemsSource = rows;

            // wybór wiersza (np. nowo dodanej kategorii)
            CategoryRow? rowToSelect = null;

            if (selectCategoryId.HasValue)
            {
                rowToSelect = rows.FirstOrDefault(r => r.CategoryId == selectCategoryId.Value);
            }
            else if (!string.IsNullOrEmpty(selectIncomeSource))
            {
                rowToSelect = rows.FirstOrDefault(r =>
                    string.Equals(r.IncomeSource, selectIncomeSource, StringComparison.OrdinalIgnoreCase));
            }

            if (rowToSelect != null)
            {
                CategoriesGrid.SelectedItem = rowToSelect;
                CategoriesGrid.ScrollIntoView(rowToSelect);
            }
            else if (rows.Count > 0 && CategoriesGrid.SelectedItem == null)
            {
                CategoriesGrid.SelectedIndex = 0;
            }
            else if (rows.Count == 0)
            {
                ClearDetails();
            }
        }

        private CategoryRow? GetSelectedRow()
            => CategoriesGrid.SelectedItem as CategoryRow;

        // ================== SZCZEGÓŁY ==================

        private void CategoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = GetSelectedRow();
            if (row == null)
            {
                ClearDetails();
                return;
            }

            CategoryNameText.Text = row.Name;
            CategorySummaryText.Text =
                $"{row.TypeDisplay} • Kwota: {row.TotalAmount:N2} • Udział: {row.SharePercent:N1}%";

            GetCurrentDateRange(out var from, out var to);
            var lastList = new List<object>();

            if (row.IsIncome)
            {
                DataTable inc = DatabaseService.GetIncomes(_userId, from, to, null);

                var filtered = inc.AsEnumerable()
                    .Where(r =>
                    {
                        var src = inc.Columns.Contains("Source")
                            ? (r["Source"]?.ToString() ?? "")
                            : "";
                        return string.Equals(src, row.IncomeSource,
                            StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderByDescending(r => r["Date"])
                    .ThenByDescending(r => r["Id"])
                    .Take(10);

                foreach (var r in filtered)
                {
                    DateTime dt;
                    DateTime.TryParse(r["Date"]?.ToString(), out dt);
                    decimal amt = 0m;
                    try { amt = Convert.ToDecimal(r["Amount"]); } catch { }

                    string desc = inc.Columns.Contains("Description")
                        ? (r["Description"]?.ToString() ?? "")
                        : "";

                    lastList.Add(new
                    {
                        Date = dt.ToString("dd.MM.yyyy"),
                        Amount = amt,
                        Description = string.IsNullOrWhiteSpace(desc) ? row.Name : desc
                    });
                }
            }
            else
            {
                DataTable exp = DatabaseService.GetExpenses(
                    _userId, from, to, row.CategoryId, null, null);

                var filtered = exp.AsEnumerable()
                    .OrderByDescending(r => r["Date"])
                    .ThenByDescending(r => r["Id"])
                    .Take(10);

                foreach (var r in filtered)
                {
                    DateTime dt;
                    DateTime.TryParse(r["Date"]?.ToString(), out dt);
                    decimal amt = 0m;
                    try { amt = Convert.ToDecimal(r["Amount"]); } catch { }

                    string desc = "";
                    if (exp.Columns.Contains("Title"))
                        desc = r["Title"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(desc) && exp.Columns.Contains("Note"))
                        desc = r["Note"]?.ToString() ?? "";

                    lastList.Add(new
                    {
                        Date = dt.ToString("dd.MM.yyyy"),
                        Amount = amt,
                        Description = desc
                    });
                }
            }

            LastTransactionsGrid.ItemsSource = lastList;
        }

        private void ClearDetails()
        {
            CategoryNameText.Text = "(Wybierz kategorię)";
            CategorySummaryText.Text = "";
            LastTransactionsGrid.ItemsSource = null;
        }

        // ================== FILTRY – PRZYCISKI ==================

        private void ApplyFilters_Click(object sender, RoutedEventArgs e)
            => RefreshCategories();

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            TypeCombo.SelectedIndex = 0;
            FromDatePicker.SelectedDate = null;
            ToDatePicker.SelectedDate = null;
            InitFilters();
            RefreshCategories();
        }

        private void PresetToday_Click(object sender, RoutedEventArgs e)
        {
            var d = DateTime.Today;
            FromDatePicker.SelectedDate = d;
            ToDatePicker.SelectedDate = d;
            RefreshCategories();
        }

        private void PresetThisMonth_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            var start = new DateTime(today.Year, today.Month, 1);
            var end = start.AddMonths(1).AddDays(-1);

            FromDatePicker.SelectedDate = start;
            ToDatePicker.SelectedDate = end;
            RefreshCategories();
        }

        private void PresetThisYear_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            var start = new DateTime(today.Year, 1, 1);
            var end = new DateTime(today.Year, 12, 31);

            FromDatePicker.SelectedDate = start;
            ToDatePicker.SelectedDate = end;
            RefreshCategories();
        }

        // ================== DODAJ / EDYTUJ / ARCHIWIZUJ / SCAL ==================

        private void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            if (_userId <= 0) return;

            var name = PromptForText("Dodaj kategorię", "Nazwa kategorii:");
            if (string.IsNullOrWhiteSpace(name)) return;

            var newId = DatabaseService.CreateCategory(_userId, name.Trim());
            RefreshCategories(selectCategoryId: newId);

            ToastService.Show("Kategoria została utworzona.", "success");
        }

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedRow();
            if (row == null)
            {
                MessageBox.Show("Najpierw wybierz kategorię.", "Kategorie",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var name = PromptForText("Edytuj kategorię", "Nazwa kategorii:", row.Name);
            if (string.IsNullOrWhiteSpace(name)) return;

            if (row.CategoryId.HasValue)
            {
                DatabaseService.UpdateCategory(row.CategoryId.Value, name.Trim());
                RefreshCategories(selectCategoryId: row.CategoryId.Value);
                ToastService.Show("Kategoria została zaktualizowana.", "success");
            }
            else
            {
                // na razie nie zmieniamy nazw źródeł przychodów
                RefreshCategories(selectIncomeSource: row.IncomeSource);
            }
        }

        private void ArchiveCategory_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedRow();
            if (row == null || !row.CategoryId.HasValue)
            {
                MessageBox.Show("Najpierw wybierz kategorię wydatków (z przypisaną kategorią).",
                    "Kategorie", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Czy na pewno chcesz zarchiwizować kategorię „{row.Name}”?\n" +
                "Nie będzie się pojawiać w listach, ale pozostanie w starych transakcjach.",
                "Archiwizuj kategorię",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            // na razie zwykły DELETE; w przyszłości można dodać IsArchived
            DatabaseService.DeleteCategory(row.CategoryId.Value);
            RefreshCategories();

            ToastService.Show("Kategoria została zarchiwizowana.", "info");
        }

        private void MergeCategories_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Scalanie kategorii dodamy w kolejnym kroku (okno z wyborem kategorii docelowej). " +
                "Na razie funkcja jest wyłączona, żeby niczego nie popsuć.",
                "Scal kategorie",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ================== ŁADNY INPUT W STYLU FINLY ==================

        private string? PromptForText(string title, string label, string? initial = null)
        {
            var owner = Window.GetWindow(this);
            var dlg = new TextInputDialog(title, label, initial);
            if (owner != null)
                dlg.Owner = owner;

            var result = dlg.ShowDialog();
            if (result == true)
                return dlg.Value;

            return null;
        }
    }
}




