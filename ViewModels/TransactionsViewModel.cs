using Finly.Models;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Finly.ViewModels
{
    public class TransactionsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int UserId { get; private set; }

        // Wszystkie transakcje z DB (bez filtrów)
        public ObservableCollection<TransactionCardVm> AllTransactions { get; } = new();

        // “Logiczne” kolekcje (normalne vs zaplanowane/przyszłe)
        public ObservableCollection<TransactionCardVm> Transactions { get; } = new();
        public ObservableCollection<TransactionCardVm> PlannedTransactions { get; } = new();

        // Kolekcje widokowe (lewa/prawa kolumna)
        public ObservableCollection<TransactionCardVm> TransactionsList { get; } = new();
        public ObservableCollection<TransactionCardVm> PlannedTransactionsList { get; } = new();

        // Kompatybilność ze starszym XAML
        public ObservableCollection<TransactionCardVm> FilteredTransactions => TransactionsList;

        // Lista kategorii do edycji (ComboBox)
        public ObservableCollection<string> AvailableCategories { get; } = new();

        // ------------------ SORTOWANIE ------------------

        // ------------------ SORTOWANIE ------------------

        public enum TransactionSortMode
        {
            DateDesc,
            DateAsc
        }

        private TransactionSortMode _sortMode = TransactionSortMode.DateDesc;
        public TransactionSortMode SortMode
        {
            get => _sortMode;
            set
            {
                if (_sortMode == value) return;
                _sortMode = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        public Array SortModes => Enum.GetValues(typeof(TransactionSortMode));

        private static DateTime SortKeyDate(TransactionCardVm t, bool plannedList)
        {
            if (TryParseDateDisplay(t.DateDisplay, out var d))
                return d.Date;

            // defensywnie: planned na koniec, realized na początek
            return plannedList ? DateTime.MaxValue : DateTime.MinValue;
        }

        /// <summary>
        /// Drugi klucz sortowania: "ostatnio dodane na górze".
        /// Bez kolumny CreatedAt w DB, najstabilniej użyć Id malejąco (autoincrement).
        /// Żeby było deterministycznie także między typami, dokładamy Kind jako tie-breaker.
        /// </summary>
        private static long SortKeyId(TransactionCardVm t)
        {
            // KindOrder: Expense=3, Income=2, Transfer=1 (tylko jako deterministyczny tie-breaker)
            // Najważniejsze i tak jest Id (ostatnio dodane -> największe Id w swojej tabeli).
            long kindOrder = t.Kind switch
            {
                TransactionKind.Expense => 3,
                TransactionKind.Income => 2,
                TransactionKind.Transfer => 1,
                _ => 0
            };

            // Sklejamy klucz: kindOrder * 1_000_000_000 + Id
            // (nie chodzi o porównywanie globalnie między tabelami, tylko o deterministykę).
            return (kindOrder * 1_000_000_000L) + t.Id;
        }

        private IEnumerable<TransactionCardVm> ApplySort(IEnumerable<TransactionCardVm> src, bool plannedList)
        {
            // Sort: 1) data 2) "kolejność dodania" (Id) 3) opis (deterministycznie)
            if (SortMode == TransactionSortMode.DateAsc)
            {
                return src
                    .OrderBy(t => SortKeyDate(t, plannedList))
                    .ThenBy(t => SortKeyId(t))                 // ASC -> starsze ID wcześniej
                    .ThenBy(t => t.Description ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
            }

            return src
                .OrderByDescending(t => SortKeyDate(t, plannedList))
                .ThenByDescending(t => SortKeyId(t))          // DESC -> najnowsze ID na górze
                .ThenBy(t => t.Description ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
        }


        // ------------------ KPI ------------------

        private decimal _totalExpenses;
        public decimal TotalExpenses
        {
            get => _totalExpenses;
            private set
            {
                if (_totalExpenses == value) return;
                _totalExpenses = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Balance));
            }
        }

        private decimal _totalIncomes;
        public decimal TotalIncomes
        {
            get => _totalIncomes;
            private set
            {
                if (_totalIncomes == value) return;
                _totalIncomes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Balance));
            }
        }

        public decimal Balance => TotalIncomes - TotalExpenses;

        private string? _searchQuery;
        public string? SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery == value) return;
                _searchQuery = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        // ------------------ OKRES ------------------

        private bool _isToday;
        public bool IsToday
        {
            get => _isToday;
            set
            {
                if (value && !_isToday)
                {
                    ClearPeriods();
                    _isToday = true;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        private bool _isYesterday;
        public bool IsYesterday
        {
            get => _isYesterday;
            set
            {
                if (value && !_isYesterday)
                {
                    ClearPeriods();
                    _isYesterday = true;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        private bool _isThisWeek;
        public bool IsThisWeek
        {
            get => _isThisWeek;
            set
            {
                if (value && !_isThisWeek)
                {
                    ClearPeriods();
                    _isThisWeek = true;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        private bool _isThisMonth;
        public bool IsThisMonth
        {
            get => _isThisMonth;
            set
            {
                if (value && !_isThisMonth)
                {
                    ClearPeriods();
                    _isThisMonth = true;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        private bool _isPrevMonth;
        public bool IsPrevMonth
        {
            get => _isPrevMonth;
            set
            {
                if (value && !_isPrevMonth)
                {
                    ClearPeriods();
                    _isPrevMonth = true;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        private bool _isThisYear;
        public bool IsThisYear
        {
            get => _isThisYear;
            set
            {
                if (value && !_isThisYear)
                {
                    ClearPeriods();
                    _isThisYear = true;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        // ------------------ FILTRY: typy ------------------

        private bool _showExpenses = true;
        public bool ShowExpenses
        {
            get => _showExpenses;
            set
            {
                if (_showExpenses == value) return;
                _showExpenses = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private bool _showIncomes = true;
        public bool ShowIncomes
        {
            get => _showIncomes;
            set
            {
                if (_showIncomes == value) return;
                _showIncomes = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        private bool _showTransfers = true;
        public bool ShowTransfers
        {
            get => _showTransfers;
            set
            {
                if (_showTransfers == value) return;
                _showTransfers = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        public ObservableCollection<CategoryFilterItem> Categories { get; } = new();
        public ObservableCollection<AccountFilterItem> Accounts { get; } = new();

        private bool _showScheduled = true;
        public bool ShowScheduled
        {
            get => _showScheduled;
            set
            {
                if (_showScheduled == value) return;
                _showScheduled = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

        public object? DateFrom { get; set; }
        public object? DateTo { get; set; }

        // słowniki nazw
        private Dictionary<int, string> _accountNameById = new();
        private Dictionary<int, string> _envelopeNameById = new();

        public ICommand DeleteTransactionCommand { get; }

        public TransactionsViewModel()
        {
            DeleteTransactionCommand = new DelegateCommand(obj =>
            {
                if (obj is not TransactionCardVm vm) return;

                // Blokada kasowania rat harmonogramu (read-only)
                if (vm.IsReadOnly)
                {
                    vm.IsDeleteConfirmationVisible = false;
                    return;
                }

                try
                {
                    var src = vm.Kind switch
                    {
                        TransactionKind.Transfer => LedgerService.TransactionSource.Transfer,
                        TransactionKind.Expense => LedgerService.TransactionSource.Expense,
                        TransactionKind.Income => LedgerService.TransactionSource.Income,
                        _ => throw new InvalidOperationException("Nieznany typ transakcji.")
                    };

                    TransactionsFacadeService.DeleteTransaction(src, vm.Id);
                }
                catch
                {
                    // opcjonalnie: komunikat dla UI
                }
                finally
                {
                    vm.IsDeleteConfirmationVisible = false;
                    LoadFromDatabase();
                }
            });
        }

        public void Initialize(int userId)
        {
            UserId = userId;
            LoadLookupData();
            LoadAvailableLists();
            LoadFromDatabase();

            if (!_isToday && !_isYesterday && !_isThisWeek && !_isThisMonth && !_isPrevMonth && !_isThisYear)
                IsThisMonth = true;
        }

        public void ReloadAll()
        {
            LoadLookupData();
            LoadAvailableLists();
            LoadFromDatabase();
        }

        // ------------------ LOOKUP ------------------

        private void LoadLookupData()
        {
            Categories.Clear();

            try
            {
                foreach (var c in DatabaseService.GetCategoriesByUser(UserId) ?? new List<string>())
                    Categories.Add(new CategoryFilterItem { Name = c, IsSelected = true });
            }
            catch { }

            void EnsureCat(string name)
            {
                if (!Categories.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                    Categories.Add(new CategoryFilterItem { Name = name, IsSelected = true });
            }

            EnsureCat("(brak)");
            EnsureCat("Przychód");
            EnsureCat("Transfer");
            EnsureCat("Kredyt");

            // Podpinamy zmiany IsSelected
            foreach (var ci in Categories)
                ci.PropertyChanged += (_, __) => ApplyFilters();

            Categories.CollectionChanged += (_, e) =>
            {
                if (e.NewItems != null)
                    foreach (CategoryFilterItem ci in e.NewItems)
                        ci.PropertyChanged += (_, __) => ApplyFilters();
                ApplyFilters();
            };

            Accounts.Clear();

            try
            {
                foreach (var a in DatabaseService.GetAccounts(UserId) ?? new List<BankAccountModel>())
                    Accounts.Add(new AccountFilterItem { Name = a.AccountName, IsSelected = true });
            }
            catch { }

            try
            {
                foreach (var env in DatabaseService.GetEnvelopesNames(UserId) ?? new List<string>())
                    Accounts.Add(new AccountFilterItem { Name = NormalizeEnvelopeLabel(env), IsSelected = true });

            }
            catch { }

            Accounts.Add(new AccountFilterItem { Name = "Wolna gotówka", IsSelected = true });
            Accounts.Add(new AccountFilterItem { Name = "Odłożona gotówka", IsSelected = true });

            foreach (var ai in Accounts)
                ai.PropertyChanged += (_, __) => ApplyFilters();

            Accounts.CollectionChanged += (_, e) =>
            {
                if (e.NewItems != null)
                    foreach (AccountFilterItem ai in e.NewItems)
                        ai.PropertyChanged += (_, __) => ApplyFilters();
                ApplyFilters();
            };
        }

        private void LoadAvailableLists()
        {
            AvailableCategories.Clear();
            try
            {
                var cats = DatabaseService.GetCategoriesByUser(UserId) ?? new List<string>();
                foreach (var c in cats) AvailableCategories.Add(c);

                if (!AvailableCategories.Contains("(brak)")) AvailableCategories.Add("(brak)");
                if (!AvailableCategories.Contains("Przychód")) AvailableCategories.Add("Przychód");
                if (!AvailableCategories.Contains("Transfer")) AvailableCategories.Add("Transfer");
                if (!AvailableCategories.Contains("Kredyt")) AvailableCategories.Add("Kredyt");
            }
            catch { }
        }

        // ------------------ ŁADOWANIE DB ------------------

        public void LoadFromDatabase()
        {
            if (UserId <= 0) return;

            AllTransactions.Clear();

            try
            {
                _accountNameById = DatabaseService.GetAccounts(UserId)
                    .ToDictionary(a => a.Id, a => a.AccountName);
            }
            catch { _accountNameById = new(); }

            try
            {
                var envDt = DatabaseService.GetEnvelopesTable(UserId);
                _envelopeNameById = new();
                if (envDt != null)
                {
                    foreach (DataRow rr in envDt.Rows)
                    {
                        try
                        {
                            var id = Convert.ToInt32(rr["Id"]);
                            var nm = rr["Name"]?.ToString() ?? "";
                            if (id > 0) _envelopeNameById[id] = nm;
                        }
                        catch { }
                    }
                }
            }
            catch { _envelopeNameById = new(); }

            DataTable? expDt = null;
            try { expDt = DatabaseService.GetExpenses(UserId); } catch { }
            if (expDt != null)
                foreach (DataRow r in expDt.Rows) AddExpenseRow(r);

            DataTable? incDt = null;
            try { incDt = DatabaseService.GetIncomes(UserId); } catch { }
            if (incDt != null)
                foreach (DataRow r in incDt.Rows) AddIncomeRow(r);

            DataTable? trDt = null;
            try { trDt = DatabaseService.GetTransfers(UserId); } catch { }
            if (trDt != null)
                foreach (DataRow r in trDt.Rows) AddTransferRow(r);


            SplitTransactionsIntoPrimaryCollections();
            ApplyFilters();
        }

        private void SplitTransactionsIntoPrimaryCollections()
        {
            Transactions.Clear();
            PlannedTransactions.Clear();

            var today = DateTime.Today;

            foreach (var t in AllTransactions)
            {
                var d = TryParseDateDisplay(t.DateDisplay, out var parsed) ? parsed : DateTime.MinValue;

                bool hasValidDate = d != DateTime.MinValue;
                bool isFuture = hasValidDate && d.Date > today;

                // Planned ma sens tylko dla przyszłości
                bool isPlannedEffective = t.IsPlanned && isFuture;

                t.IsFuture = isFuture;

                if (isPlannedEffective || isFuture)
                    PlannedTransactions.Add(t);
                else
                    Transactions.Add(t);
            }
        }


        // ------------------ DATE HELPERS ------------------

        private static string ToIso(DateTime dt)
            => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string ToUiDate(DateTime dt)
            => dt.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture);

        private static bool TryParseDateDisplay(string? s, out DateTime dt)
        {
            dt = default;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var formats = new[] { "yyyy-MM-dd", "dd-MM-yyyy", "yyyy/MM/dd", "dd/MM/yyyy" };

            if (DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return true;

            return DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt)
                || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
        }

        private DateTime ParseDate(object? raw)
        {
            if (raw is DateTime dt) return dt;

            if (DateTime.TryParseExact(raw?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var p1))
                return p1;

            if (DateTime.TryParse(raw?.ToString(), out var p)) return p;

            return DateTime.MinValue;
        }

        // ------------------ WYŚWIETLANIE KONT ------------------

        private static string NormalizeEnvelopeLabel(string envName)
        {
            var s = (envName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s)) return "Koperta";

            // jeśli już jest "Koperta:" -> zostaw
            if (s.StartsWith("Koperta:", StringComparison.CurrentCultureIgnoreCase))
                return "Koperta: " + s.Substring("Koperta:".Length).Trim();

            // jeśli user nazwał kopertę "Koperta Beata" -> zrób z tego "Koperta: Beata"
            if (s.StartsWith("Koperta ", StringComparison.CurrentCultureIgnoreCase))
                return "Koperta: " + s.Substring("Koperta ".Length).Trim();

            // normalnie: "Beata" -> "Koperta: Beata"
            return "Koperta: " + s;
        }


        private string ResolvePaymentDisplay(int paymentKind, int? paymentRefId, int? fallbackAccountId = null)
        {
            // 0 FreeCash, 1 SavedCash, 2 BankAccount, 3 Envelope
            if (paymentKind == 0) return "Wolna gotówka";
            if (paymentKind == 1) return "Odłożona gotówka";

            if (paymentRefId.HasValue)
            {
                if (_accountNameById.TryGetValue(paymentRefId.Value, out var acc))
                    return acc;

                if (_envelopeNameById.TryGetValue(paymentRefId.Value, out var env))
                    return NormalizeEnvelopeLabel(env);

            }

            if (paymentKind == 2)
            {
                if (paymentRefId.HasValue && _accountNameById.TryGetValue(paymentRefId.Value, out var acc2))
                    return acc2;

                if (fallbackAccountId.HasValue && _accountNameById.TryGetValue(fallbackAccountId.Value, out var accLegacy))
                    return accLegacy;

                return "Konto bankowe";
            }

            if (paymentKind == 3)
            {
                if (paymentRefId.HasValue && _envelopeNameById.TryGetValue(paymentRefId.Value, out var env2))
                    return NormalizeEnvelopeLabel(env2);


                return "Koperta";
            }

            return "?";
        }

        private string ResolveAccountDisplay(string kind, int? id)
        {
            var k = (kind ?? string.Empty).Trim().ToLowerInvariant();

            return k switch
            {
                "bank" => (id.HasValue && _accountNameById.TryGetValue(id.Value, out var acc)) ? acc : "Konto bankowe",
                "envelope" => (id.HasValue && _envelopeNameById.TryGetValue(id.Value, out var env))
                ? NormalizeEnvelopeLabel(env)
                : "Koperta",

                "freecash" => "Wolna gotówka",
                "cash" => "Wolna gotówka",
                "savedcash" => "Odłożona gotówka",
                "saved" => "Odłożona gotówka",
                _ => "?"
            };
        }

        private string ResolveIncomeTargetFromSource(string? source)
        {
            var s = (source ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            if (s.StartsWith("Koperta:", StringComparison.CurrentCultureIgnoreCase))
                return s;

            foreach (var kv in _accountNameById)
                if (string.Equals(kv.Value, s, StringComparison.CurrentCultureIgnoreCase))
                    return kv.Value;

            foreach (var kv in _envelopeNameById)
                if (string.Equals(kv.Value, s, StringComparison.CurrentCultureIgnoreCase))
                    return $"Koperta: {kv.Value}";

            return s;
        }

        // ------------------ DODAWANIE WIERSZY ------------------

        private void AddExpenseRow(DataRow r)
        {
            int id = Convert.ToInt32(r["Id"]);
            double amt = Convert.ToDouble(r["Amount"]);
            DateTime date = ParseDate(r["Date"]);

            string desc = r.Table.Columns.Contains("Description")
                ? (r["Description"]?.ToString() ?? string.Empty)
                : (r.Table.Columns.Contains("Title") ? (r["Title"]?.ToString() ?? string.Empty) : string.Empty);

            string catName = r.Table.Columns.Contains("CategoryName")
                ? (r["CategoryName"]?.ToString() ?? string.Empty)
                : string.Empty;

            bool planned =
                r.Table.Columns.Contains("IsPlanned") &&
                r["IsPlanned"] != DBNull.Value &&
                Convert.ToInt32(r["IsPlanned"]) == 1;

            int? accountId =
                r.Table.Columns.Contains("AccountId") && r["AccountId"] != DBNull.Value
                    ? (int?)Convert.ToInt32(r["AccountId"])
                    : null;

            int pk =
                r.Table.Columns.Contains("PaymentKind") && r["PaymentKind"] != DBNull.Value
                    ? Convert.ToInt32(r["PaymentKind"])
                    : 0;

            int? pr =
                r.Table.Columns.Contains("PaymentRefId") && r["PaymentRefId"] != DBNull.Value
                    ? (int?)Convert.ToInt32(r["PaymentRefId"])
                    : null;

            // === BUDŻET ===
            int? budgetId =
                r.Table.Columns.Contains("BudgetId") && r["BudgetId"] != DBNull.Value
                    ? (int?)Convert.ToInt32(r["BudgetId"])
                    : null;

            string? budgetName =
                r.Table.Columns.Contains("BudgetName") && r["BudgetName"] != DBNull.Value
                    ? r["BudgetName"]?.ToString()
                    : null;

            string fromName = ResolvePaymentDisplay(pk, pr, fallbackAccountId: accountId);

            AllTransactions.Add(new TransactionCardVm
            {
                Id = id,
                Kind = TransactionKind.Expense,
                CategoryName = string.IsNullOrWhiteSpace(catName) ? "(brak)" : catName,
                Description = desc,
                DateDisplay = ToUiDate(date),
                AmountStr = amt.ToString("N2") + " zł",
                IsPlanned = planned,
                IsFuture = date.Date > DateTime.Today,

                AccountName = fromName,
                FromAccountName = fromName,
                ToAccountName = null,

                PaymentKind = pk,
                PaymentRefId = pr,

                // === BUDŻET w VM ===
                BudgetId = budgetId,
                BudgetName = budgetName,

                SelectedCategory = string.IsNullOrWhiteSpace(catName) ? "(brak)" : catName,
                EditDescription = desc,
                EditDate = date
            });
        }


        private void AddIncomeRow(DataRow r)
        {
            int id = Convert.ToInt32(r["Id"]);
            decimal amt = Convert.ToDecimal(r["Amount"]);
            DateTime date = ParseDate(r["Date"]);
            string desc = r["Description"]?.ToString() ?? string.Empty;

            string catName = r.Table.Columns.Contains("CategoryName")
                ? (r["CategoryName"]?.ToString() ?? string.Empty)
                : string.Empty;

            bool planned =
                r.Table.Columns.Contains("IsPlanned") &&
                r["IsPlanned"] != DBNull.Value &&
                Convert.ToInt32(r["IsPlanned"]) == 1;

            int pk =
                r.Table.Columns.Contains("PaymentKind") && r["PaymentKind"] != DBNull.Value
                    ? Convert.ToInt32(r["PaymentKind"])
                    : 0;

            int? pr =
                r.Table.Columns.Contains("PaymentRefId") && r["PaymentRefId"] != DBNull.Value
                    ? (int?)Convert.ToInt32(r["PaymentRefId"])
                    : null;

            string? sourceTxt =
                (r.Table.Columns.Contains("Source") && r["Source"] != DBNull.Value)
                    ? (r["Source"]?.ToString())
                    : null;

            // === BUDŻET ===
            int? budgetId =
                r.Table.Columns.Contains("BudgetId") && r["BudgetId"] != DBNull.Value
                    ? (int?)Convert.ToInt32(r["BudgetId"])
                    : null;

            string? budgetName =
                r.Table.Columns.Contains("BudgetName") && r["BudgetName"] != DBNull.Value
                    ? r["BudgetName"]?.ToString()
                    : null;

            string toName = ResolvePaymentDisplay(pk, pr, fallbackAccountId: null);
            var sourceResolved = ResolveIncomeTargetFromSource(sourceTxt);

            bool paymentLooksLikeCash = (pk == 0 || pk == 1);
            bool paymentUnresolved = string.IsNullOrWhiteSpace(toName) || toName == "?";

            if (!string.IsNullOrWhiteSpace(sourceResolved) && (paymentLooksLikeCash || paymentUnresolved))
                toName = sourceResolved;

            if (string.IsNullOrWhiteSpace(toName) || toName == "?")
            {
                toName = paymentLooksLikeCash
                    ? (pk == 1 ? "Odłożona gotówka" : "Wolna gotówka")
                    : "Wolna gotówka";
            }

            AllTransactions.Add(new TransactionCardVm
            {
                Id = id,
                Kind = TransactionKind.Income,
                CategoryName = string.IsNullOrEmpty(catName) ? "Przychód" : catName,
                Description = desc,
                DateDisplay = ToUiDate(date),
                AmountStr = amt.ToString("N2") + " zł",
                IsPlanned = planned,
                IsFuture = date.Date > DateTime.Today,

                AccountName = toName,
                FromAccountName = null,
                ToAccountName = toName,

                PaymentKind = pk,
                PaymentRefId = pr,

                // === BUDŻET w VM ===
                BudgetId = budgetId,
                BudgetName = budgetName,

                SelectedCategory = string.IsNullOrWhiteSpace(catName) ? "Przychód" : catName,
                EditDescription = desc,
                EditDate = date
            });
        }


        private void AddTransferRow(DataRow r)
        {
            int id = Convert.ToInt32(r["Id"]);
            decimal amt = Convert.ToDecimal(r["Amount"]);
            DateTime date = ParseDate(r["Date"]);
            string desc = r["Description"]?.ToString() ?? "Transfer";

            bool planned =
                r.Table.Columns.Contains("IsPlanned") &&
                r["IsPlanned"] != DBNull.Value &&
                Convert.ToInt32(r["IsPlanned"]) == 1;

            // 1) NEW schema (preferred): Effective*PaymentKind/RefId
            bool hasEff =
                r.Table.Columns.Contains("EffectiveFromPaymentKind") &&
                r.Table.Columns.Contains("EffectiveToPaymentKind");

            string fromName;
            string toName;

            if (hasEff && r["EffectiveFromPaymentKind"] != DBNull.Value && r["EffectiveToPaymentKind"] != DBNull.Value)
            {
                int fromPk = Convert.ToInt32(r["EffectiveFromPaymentKind"]);
                int toPk = Convert.ToInt32(r["EffectiveToPaymentKind"]);

                int? fromPr = r.Table.Columns.Contains("EffectiveFromPaymentRefId") && r["EffectiveFromPaymentRefId"] != DBNull.Value
                    ? (int?)Convert.ToInt32(r["EffectiveFromPaymentRefId"])
                    : null;

                int? toPr = r.Table.Columns.Contains("EffectiveToPaymentRefId") && r["EffectiveToPaymentRefId"] != DBNull.Value
                    ? (int?)Convert.ToInt32(r["EffectiveToPaymentRefId"])
                    : null;

                // ResolvePaymentDisplay masz już gotowe (0/1/2/3)
                fromName = ResolvePaymentDisplay(fromPk, fromPr);
                toName = ResolvePaymentDisplay(toPk, toPr);
            }
            else
            {
                // 2) LEGACY fallback: FromKind/ToKind + RefId
                string fromKind = r.Table.Columns.Contains("FromKind") ? (r["FromKind"]?.ToString() ?? "") : "";
                string toKind = r.Table.Columns.Contains("ToKind") ? (r["ToKind"]?.ToString() ?? "") : "";

                int? fromRef = (r.Table.Columns.Contains("FromRefId") && r["FromRefId"] != DBNull.Value)
                    ? (int?)Convert.ToInt32(r["FromRefId"])
                    : null;

                int? toRef = (r.Table.Columns.Contains("ToRefId") && r["ToRefId"] != DBNull.Value)
                    ? (int?)Convert.ToInt32(r["ToRefId"])
                    : null;

                fromName = ResolveAccountDisplay(fromKind, fromRef);
                toName = ResolveAccountDisplay(toKind, toRef);
            }

            AllTransactions.Add(new TransactionCardVm
            {
                Id = id,
                Kind = TransactionKind.Transfer,
                CategoryName = "Transfer",
                Description = desc,
                DateDisplay = ToUiDate(date),
                AmountStr = amt.ToString("N2") + " zł",
                IsPlanned = planned,
                IsFuture = date.Date > DateTime.Today,

                AccountName = $"{fromName} -> {toName}",
                FromAccountName = fromName,
                ToAccountName = toName,

                SelectedCategory = "Transfer",
                EditDescription = desc,
                EditDate = date
            });
        }




        // ------------------ FILTERS ------------------

        private void ClearPeriods()
        {
            _isToday = _isYesterday = _isThisWeek = _isThisMonth = _isPrevMonth = _isThisYear = false;
        }

        private bool MatchesSelectedAccounts(TransactionCardVm t, List<string> selectedAccounts, int totalAccounts)
        {
            if (selectedAccounts.Count == 0) return false;
            if (selectedAccounts.Count == totalAccounts) return true;

            string Normalize(string s) => (s ?? string.Empty).Trim();
            var set = selectedAccounts.Select(Normalize).ToHashSet(StringComparer.CurrentCultureIgnoreCase);

            bool MatchesCash(string name) =>
                name.IndexOf("gotówka", StringComparison.CurrentCultureIgnoreCase) >= 0 &&
                (set.Contains("Wolna gotówka") || set.Contains("Odłożona gotówka"));

            if (t.Kind == TransactionKind.Transfer)
            {
                var from = Normalize(t.FromAccountName ?? "");
                var to = Normalize(t.ToAccountName ?? "");
                return set.Contains(from) || set.Contains(to) || MatchesCash(from) || MatchesCash(to);
            }
            else
            {
                var n = Normalize(t.AccountName);
                return set.Contains(n) || MatchesCash(n);
            }
        }

        private decimal ParseAmountInternal(string amountStr)
        {
            if (string.IsNullOrWhiteSpace(amountStr)) return 0m;

            var txt = new string(amountStr.Where(ch => char.IsDigit(ch) || ch == ',' || ch == '.' || ch == '-').ToArray());

            if (decimal.TryParse(txt, NumberStyles.Number, CultureInfo.CurrentCulture, out var v1))
                return Math.Abs(v1);

            if (decimal.TryParse(txt, NumberStyles.Number, CultureInfo.InvariantCulture, out var v2))
                return Math.Abs(v2);

            return 0m;
        }

        private void ApplyFilters()
        {
            if (AllTransactions.Count == 0)
            {
                TransactionsList.Clear();
                PlannedTransactionsList.Clear();
                TotalExpenses = 0;
                TotalIncomes = 0;
                return;
            }

            DateTime? from = null, to = null;
            var today = DateTime.Today;

            if (IsToday) { from = today; to = today; }
            else if (IsYesterday) { var y = today.AddDays(-1); from = y; to = y; }
            else if (IsThisWeek)
            {
                int diff = ((int)today.DayOfWeek + 6) % 7;
                var start = today.AddDays(-diff).Date;
                from = start;
                to = start.AddDays(6);
            }
            else if (IsThisMonth)
            {
                var start = new DateTime(today.Year, today.Month, 1);
                from = start;
                to = start.AddMonths(1).AddDays(-1);
            }
            else if (IsPrevMonth)
            {
                var start = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                from = start;
                to = start.AddMonths(1).AddDays(-1);
            }
            else if (IsThisYear)
            {
                from = new DateTime(today.Year, 1, 1);
                to = new DateTime(today.Year, 12, 31);
            }

            if (DateFrom is DateTime df && DateTo is DateTime dt)
            {
                from = df.Date;
                to = dt.Date;
            }

            var selectedAccounts = Accounts.Where(a => a.IsSelected).Select(a => a.Name).ToList();
            var selectedCategories = Categories.Where(c => c.IsSelected).Select(c => c.Name).ToList();
            int totalAccounts = Accounts.Count;
            int totalCategories = Categories.Count;

            var q = (SearchQuery ?? string.Empty).Trim();

            bool PassCommonFilters(TransactionCardVm t)
            {
                bool dateOk = true;
                if (from.HasValue && to.HasValue)
                {
                    if (TryParseDateDisplay(t.DateDisplay, out var d))
                        dateOk = d.Date >= from.Value.Date && d.Date <= to.Value.Date;
                }

                bool typeOk;
                bool allTypesSelected = ShowExpenses && ShowIncomes && ShowTransfers;
                bool noTypesSelected = !ShowExpenses && !ShowIncomes && !ShowTransfers;

                if (noTypesSelected) typeOk = false;
                else if (allTypesSelected) typeOk = true;
                else
                {
                    var typeSelected = new[]
                    {
                (TransactionKind.Expense, ShowExpenses),
                (TransactionKind.Income, ShowIncomes),
                (TransactionKind.Transfer, ShowTransfers)
            }
                    .Where(tpl => tpl.Item2)
                    .Select(tpl => tpl.Item1)
                    .ToList();

                    typeOk = typeSelected.Contains(t.Kind);
                }

                bool accOk = MatchesSelectedAccounts(t, selectedAccounts, totalAccounts);

                bool catOk;
                if (totalCategories == 0) catOk = true;
                else if (selectedCategories.Count == 0) catOk = false;
                else if (selectedCategories.Count == totalCategories) catOk = true;
                else
                    catOk = selectedCategories.Any(x => string.Equals(t.CategoryName, x, StringComparison.CurrentCultureIgnoreCase));

                bool textOk = true;
                if (!string.IsNullOrWhiteSpace(q))
                {
                    bool textHit =
                        (t.CategoryName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        || (t.Description?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        || (t.AccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        || (t.FromAccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        || (t.ToAccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);

                    bool amountHit = false;
                    var qClean = new string(q.Where(ch => char.IsDigit(ch) || ch == ',' || ch == '.' || ch == '-').ToArray());
                    if (!string.IsNullOrWhiteSpace(qClean))
                    {
                        if (decimal.TryParse(qClean, NumberStyles.Number, CultureInfo.CurrentCulture, out var qNum)
                            || decimal.TryParse(qClean, NumberStyles.Number, CultureInfo.InvariantCulture, out qNum))
                        {
                            var amt = ParseAmountInternal(t.AmountStr);
                            amountHit = Math.Abs(amt - Math.Abs(qNum)) < 0.01m;
                        }
                        else
                        {
                            amountHit = (t.AmountStr?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
                        }
                    }

                    textOk = textHit || amountHit;
                }

                return dateOk && typeOk && accOk && catOk && textOk;
            }

            var leftRaw = AllTransactions
                .Where(t => PassCommonFilters(t) && !(t.IsPlanned || t.IsFuture));

            var leftList = ApplySort(leftRaw, plannedList: false).ToList();

            TransactionsList.Clear();
            foreach (var i in leftList) TransactionsList.Add(i);

            var rightRaw = AllTransactions
                .Where(t => PassCommonFilters(t) && (t.IsPlanned || t.IsFuture));

            var rightList = ApplySort(rightRaw, plannedList: true).ToList();

            PlannedTransactionsList.Clear();
            foreach (var r in rightList) PlannedTransactionsList.Add(r);

            TotalExpenses = leftList.Where(t => t.Kind == TransactionKind.Expense)
                .Select(x => ParseAmountInternal(x.AmountStr)).Sum();

            TotalIncomes = leftList.Where(t => t.Kind == TransactionKind.Income)
                .Select(x => ParseAmountInternal(x.AmountStr)).Sum();
        }


        public void SetPeriod(Finly.Models.DateRangeMode mode, DateTime start, DateTime end)
        {
            ClearPeriods();

            switch (mode)
            {
                case DateRangeMode.Day:
                    _isToday = true; DateFrom = null; DateTo = null; break;
                case DateRangeMode.Week:
                    _isThisWeek = true; DateFrom = null; DateTo = null; break;
                case DateRangeMode.Month:
                    _isThisMonth = true; DateFrom = null; DateTo = null; break;
                case DateRangeMode.Year:
                    _isThisYear = true; DateFrom = null; DateTo = null; break;
                case DateRangeMode.Quarter:
                case DateRangeMode.Custom:
                    DateFrom = start; DateTo = end; break;
                default:
                    DateFrom = start; DateTo = end; break;
            }

            OnPropertyChanged(nameof(IsToday));
            OnPropertyChanged(nameof(IsThisWeek));
            OnPropertyChanged(nameof(IsThisMonth));
            OnPropertyChanged(nameof(IsPrevMonth));
            OnPropertyChanged(nameof(IsThisYear));

            ApplyFilters();
        }

        public void RefreshData()
        {
            ApplyFilters();
        }

        public void StartEdit(TransactionCardVm vm)
        {
            if (vm is null) return;

            // 1) Nie edytujemy inline transferów i rekordów read-only (np. raty harmonogramu)
            if (vm.Kind == TransactionKind.Transfer) return;
            if (vm.IsReadOnly) return;

            // 2) Zamknij ewentualną edycję na innych kartach (jedna edycja na raz)
            foreach (var t in AllTransactions)
            {
                if (!ReferenceEquals(t, vm) && t.IsEditing)
                {
                    t.IsEditing = false;
                    t.IsDeleteConfirmationVisible = false;
                }
            }

            // 3) Ukryj potwierdzenie usuwania na edytowanej karcie
            vm.IsDeleteConfirmationVisible = false;

            // 4) Wypełnij pola edycji (opis/kategoria/data)
            vm.EditDescription = vm.Description ?? string.Empty;

            var cat = (vm.CategoryName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cat))
                cat = "(brak)";

            // Income: jeśli UI traktuje brak kategorii jako "Przychód"
            if (vm.Kind == TransactionKind.Income &&
                cat.Equals("(brak)", StringComparison.CurrentCultureIgnoreCase))
            {
                cat = "Przychód";
            }

            vm.SelectedCategory = cat;

            // Data do edycji: z DateDisplay -> fallback Today
            DateTime editDate;
            if (TryParseDateDisplay(vm.DateDisplay, out var parsed))
                editDate = parsed.Date;
            else
                editDate = DateTime.Today;

            vm.EditDate = editDate;

            // 5) Budżety pod datę:
            //    - czyścimy i ładujemy na świeżo
            //    - ustawiamy SelectedBudget zgodnie z BudgetId
            //    UWAGA: robimy to PRZED IsEditing = true, żeby ComboBox dostał gotowe źródło i selection.
            try
            {
                LoadBudgetsForVmDate(vm, editDate);
            }
            catch
            {
                // Best-effort: jeśli coś padnie, zostaw przynajmniej "(brak budżetu)"
                vm.AvailableBudgetsForDate.Clear();
                var none = new Budget { Id = 0, Name = "(brak budżetu)", StartDate = editDate, EndDate = editDate };
                vm.AvailableBudgetsForDate.Add(none);
                vm.SelectedBudget = none;
            }

            // 6) Start edycji na końcu (po zasileniu danych do edycji)
            vm.IsEditing = true;
        }



        public void SaveEdit(TransactionCardVm vm)
        {
            if (vm == null) return;

            // Transferów nie zapisujesz inline
            if (vm.Kind == TransactionKind.Transfer)
            {
                vm.IsEditing = false;
                return;
            }

            // Read-only (np. raty harmonogramu)
            if (vm.IsReadOnly)
            {
                vm.IsEditing = false;
                return;
            }

            var newDesc = (vm.EditDescription ?? string.Empty).Trim();
            var newCatRaw = (vm.SelectedCategory ?? string.Empty).Trim();
            var newDate = (vm.EditDate ?? DateTime.Today).Date;

            if (string.IsNullOrWhiteSpace(newCatRaw))
                newCatRaw = "(brak)";

            // Income: "(brak)" traktujemy jak "Przychód" w UI
            var uiCat = newCatRaw;
            if (vm.Kind == TransactionKind.Income &&
                uiCat.Equals("(brak)", StringComparison.CurrentCultureIgnoreCase))
            {
                uiCat = "Przychód";
            }

            // BudgetId z comboboxa (0 => null)
            int? newBudgetId = null;
            if (vm.SelectedBudget != null && vm.SelectedBudget.Id > 0)
                newBudgetId = vm.SelectedBudget.Id;

            // CategoryId do DB:
            // - Expense: "(brak)" => null/0 (zależnie od schematu)
            // - Income: "Przychód" lub "(brak)" => null/0 (domyślne)
            int? newCategoryId = null;

            try
            {
                bool treatAsNoCategory =
                    newCatRaw.Equals("(brak)", StringComparison.CurrentCultureIgnoreCase) ||
                    (vm.Kind == TransactionKind.Income && uiCat.Equals("Przychód", StringComparison.CurrentCultureIgnoreCase));

                if (!treatAsNoCategory)
                {
                    var createdId = DatabaseService.GetOrCreateCategoryId(UserId, newCatRaw);
                    if (createdId > 0) newCategoryId = createdId;
                }
            }
            catch
            {
                newCategoryId = null; // best-effort
            }

            try
            {
                using var con = DatabaseService.GetConnection();
                con.Open();
                DatabaseService.EnsureTables();

                bool ColumnExists(string table, string col)
                {
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = $"PRAGMA table_info('{table}');";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var name = r["name"]?.ToString();
                        if (string.Equals(name, col, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false;
                }

                if (vm.Kind == TransactionKind.Expense)
                {
                    var hasCatId = ColumnExists("Expenses", "CategoryId");
                    var hasCatName = ColumnExists("Expenses", "CategoryName");
                    var hasBudgetId = ColumnExists("Expenses", "BudgetId");

                    using var cmd = con.CreateCommand();

                    // budujemy UPDATE dynamicznie pod schemat
                    var sets = new List<string>
            {
                "Date = @d",
                "Description = @desc"
            };

                    if (hasCatId) sets.Add("CategoryId = @catId");
                    else if (hasCatName) sets.Add("CategoryName = @catName");

                    if (hasBudgetId) sets.Add("BudgetId = @bid");

                    cmd.CommandText = $"UPDATE Expenses SET {string.Join(", ", sets)} WHERE Id = @id;";

                    cmd.Parameters.AddWithValue("@d", newDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue("@desc", newDesc);
                    cmd.Parameters.AddWithValue("@id", vm.Id);

                    if (hasCatId)
                        cmd.Parameters.AddWithValue("@catId", newCategoryId.HasValue ? (object)newCategoryId.Value : DBNull.Value);
                    else if (hasCatName)
                        cmd.Parameters.AddWithValue("@catName", uiCat);

                    if (hasBudgetId)
                        cmd.Parameters.AddWithValue("@bid", newBudgetId.HasValue ? (object)newBudgetId.Value : DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
                else if (vm.Kind == TransactionKind.Income)
                {
                    var hasCatId = ColumnExists("Incomes", "CategoryId");
                    var hasCatName = ColumnExists("Incomes", "CategoryName");
                    var hasBudgetId = ColumnExists("Incomes", "BudgetId");

                    using var cmd = con.CreateCommand();

                    var sets = new List<string>
            {
                "Date = @d",
                "Description = @desc"
            };

                    if (hasCatId) sets.Add("CategoryId = @catId");
                    else if (hasCatName) sets.Add("CategoryName = @catName");

                    if (hasBudgetId) sets.Add("BudgetId = @bid");

                    cmd.CommandText = $"UPDATE Incomes SET {string.Join(", ", sets)} WHERE Id = @id;";

                    cmd.Parameters.AddWithValue("@d", newDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue("@desc", newDesc);
                    cmd.Parameters.AddWithValue("@id", vm.Id);

                    if (hasCatId)
                        cmd.Parameters.AddWithValue("@catId", newCategoryId.HasValue ? (object)newCategoryId.Value : DBNull.Value);
                    else if (hasCatName)
                        cmd.Parameters.AddWithValue("@catName", uiCat);

                    if (hasBudgetId)
                        cmd.Parameters.AddWithValue("@bid", newBudgetId.HasValue ? (object)newBudgetId.Value : DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
                // best-effort: nawet jak DB odmówi, UI zaktualizujemy poniżej
            }

            // === UI update ===
            vm.Description = newDesc;
            vm.CategoryName = uiCat;
            vm.DateDisplay = newDate.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture);
            vm.IsFuture = newDate > DateTime.Today;

            vm.BudgetId = newBudgetId;
            vm.BudgetName = (newBudgetId.HasValue && vm.SelectedBudget != null && vm.SelectedBudget.Id == newBudgetId.Value)
                ? vm.SelectedBudget.Name
                : null;

            vm.IsEditing = false;
            vm.IsDeleteConfirmationVisible = false;

            // Jeśli user wpisał nową kategorię – dołóż do list edycji/filtrów
            void EnsureInAvailable(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                if (!AvailableCategories.Any(x => string.Equals(x, name, StringComparison.CurrentCultureIgnoreCase)))
                    AvailableCategories.Add(name);
            }

            void EnsureInFilters(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                if (!Categories.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                    Categories.Add(new CategoryFilterItem { Name = name, IsSelected = true });
            }

            EnsureInAvailable(uiCat);
            EnsureInFilters(uiCat);

            ApplyFilters();
        }


        // ------------------ TYPY POMOCNICZE ------------------

        public enum TransactionKind { Expense, Income, Transfer }

        public class DelegateCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public DelegateCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);

            public event EventHandler? CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }

        // Jeśli masz te klasy gdzie indziej – usuń poniższe definicje, żeby nie dublować.
        public class CategoryFilterItem : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

            private string _name = "";
            public string Name { get => _name; set { _name = value; Raise(); } }

            private bool _isSelected = true;
            public bool IsSelected { get => _isSelected; set { _isSelected = value; Raise(); } }
        }

        public class AccountFilterItem : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

            private string _name = "";
            public string Name { get => _name; set { _name = value; Raise(); } }

            private bool _isSelected = true;
            public bool IsSelected { get => _isSelected; set { _isSelected = value; Raise(); } }
        }

        private void LoadBudgetsForVmDate(TransactionCardVm vm, DateTime date)
        {
            vm.AvailableBudgetsForDate.Clear();

            // „Brak” na górze (pozwala odpiąć budżet)
            // UWAGA: SelectedBudget = null też działa, ale wtedy ComboBox bywa nieczytelny.
            // Jeśli wolisz null — usuń ten element i ustaw SelectedBudget = null.
            var none = new Budget { Id = 0, Name = "(brak budżetu)", StartDate = date, EndDate = date };
            vm.AvailableBudgetsForDate.Add(none);

            try
            {
                var budgets = BudgetService.GetBudgetsForUser(UserId, from: date.Date, to: date.Date);
                foreach (var b in budgets)
                    vm.AvailableBudgetsForDate.Add(b);
            }
            catch
            {
                // zostaje samo "(brak budżetu)"
            }

            // Ustaw aktualnie przypisany
            if (vm.BudgetId.HasValue && vm.BudgetId.Value > 0)
                vm.SelectedBudget = vm.AvailableBudgetsForDate.FirstOrDefault(x => x.Id == vm.BudgetId.Value) ?? none;
            else
                vm.SelectedBudget = none;
        }


        public class TransactionCardVm : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise([CallerMemberName] string? n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

            public int Id { get; set; }

            private string _categoryName = "";
            public string CategoryName { get => _categoryName; set { _categoryName = value; Raise(); } }

            private string _description = "";

            public ObservableCollection<Budget> AvailableBudgetsForDate { get; } = new();

            private Budget? _selectedBudget;
            public Budget? SelectedBudget
            {
                get => _selectedBudget;
                set
                {
                    if (ReferenceEquals(_selectedBudget, value)) return;
                    _selectedBudget = value;
                    Raise(); // zamiast OnPropertyChanged()
                }
            }


            // Przyda się do read-mode (opcjonalnie)
            public int? BudgetId { get; set; }
            public string? BudgetName { get; set; }

            public string Description { get => _description; set { _description = value; Raise(); } }

            private string _dateDisplay = "";
            public string DateDisplay { get => _dateDisplay; set { _dateDisplay = value; Raise(); } }

            private string _amountStr = "";
            public string AmountStr { get => _amountStr; set { _amountStr = value; Raise(); } }

            private bool _isPlanned;
            public bool IsPlanned { get => _isPlanned; set { _isPlanned = value; Raise(); } }

            private bool _isFuture;
            public bool IsFuture { get => _isFuture; set { _isFuture = value; Raise(); } }

            private bool _isReadOnly;
            public bool IsReadOnly { get => _isReadOnly; set { _isReadOnly = value; Raise(); } }

            public string CategoryIcon { get; set; } = "";

            private string _accountName = "";
            public string AccountName { get => _accountName; set { _accountName = value; Raise(); } }

            private string? _fromAccountName;
            public string? FromAccountName
            {
                get => _fromAccountName;
                set
                {
                    _fromAccountName = value;
                    Raise();
                    Raise(nameof(ShowFromAccount));
                    Raise(nameof(FromAccountLabel));
                }
            }

            private string? _toAccountName;
            public string? ToAccountName
            {
                get => _toAccountName;
                set
                {
                    _toAccountName = value;
                    Raise();
                    Raise(nameof(ShowToAccount));
                    Raise(nameof(ToAccountLabel));
                }
            }

            private int _paymentKind;
            public int PaymentKind { get => _paymentKind; set { _paymentKind = value; Raise(); } }

            private int? _paymentRefId;
            public int? PaymentRefId { get => _paymentRefId; set { _paymentRefId = value; Raise(); } }

            public TransactionKind Kind { get; set; } = TransactionKind.Expense;

            public bool IsTransfer => Kind == TransactionKind.Transfer;
            public bool IsExpense => Kind == TransactionKind.Expense;
            public bool IsIncome => Kind == TransactionKind.Income;

            public bool ShowFromAccount => Kind == TransactionKind.Transfer || Kind == TransactionKind.Expense;
            public bool ShowToAccount => Kind == TransactionKind.Transfer || Kind == TransactionKind.Income;

            public string FromAccountLabel => $"Z konta: {FromAccountName}";
            public string ToAccountLabel => $"Na konto: {ToAccountName}";

            private bool _isEditing;
            public bool IsEditing { get => _isEditing; set { _isEditing = value; Raise(); } }

            private string _editDescription = "";
            public string EditDescription { get => _editDescription; set { _editDescription = value; Raise(); } }

            private DateTime? _editDate = DateTime.Today;
            public DateTime? EditDate { get => _editDate; set { _editDate = value; Raise(); } }

            private string _selectedCategory = "";
            public string SelectedCategory { get => _selectedCategory; set { _selectedCategory = value; Raise(); } }

            private bool _isDeleteConfirmationVisible;
            public bool IsDeleteConfirmationVisible { get => _isDeleteConfirmationVisible; set { _isDeleteConfirmationVisible = value; Raise(); } }

            public ICommand ShowDeleteConfirmationCommand { get; }
            public ICommand HideDeleteConfirmationCommand { get; }

            public TransactionCardVm()
            {
                ShowDeleteConfirmationCommand = new DelegateCommand(_ => IsDeleteConfirmationVisible = true);
                HideDeleteConfirmationCommand = new DelegateCommand(_ => IsDeleteConfirmationVisible = false);
            }
        }
    }
}
