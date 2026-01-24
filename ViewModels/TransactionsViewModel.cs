using Finly.Models;
using Finly.Services.Features;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
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

        // ------------------ SORTOWANIE (NA POZIOMIE VM, NIE CardVm) ------------------

        public enum TransactionSortMode
        {
            DateDesc, // najnowsze -> najstarsze
            DateAsc   // najstarsze -> najnowsze
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

            // Brak daty -> na koniec listy
            return plannedList ? DateTime.MaxValue : DateTime.MinValue;
        }

        // ------------------ KPI ------------------

        private decimal _totalExpenses;
        public decimal TotalExpenses
        {
            get => _totalExpenses;
            private set
            {
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
                _searchQuery = value;
                OnPropertyChanged();
                ApplyFilters();
            }
        }

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

        private bool _showExpenses = true;
        public bool ShowExpenses
        {
            get => _showExpenses;
            set
            {
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

            Categories.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (CategoryFilterItem ci in e.NewItems) ci.PropertyChanged += (_, __) => ApplyFilters();
                if (e.OldItems != null)
                    foreach (CategoryFilterItem ci in e.OldItems) ci.PropertyChanged -= (_, __) => ApplyFilters();
                ApplyFilters();
            };

            foreach (var ci in Categories)
                ci.PropertyChanged += (_, __) => ApplyFilters();

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
                    Accounts.Add(new AccountFilterItem { Name = $"Koperta: {env}", IsSelected = true });
            }
            catch { }

            Accounts.Add(new AccountFilterItem { Name = "Wolna gotówka", IsSelected = true });
            Accounts.Add(new AccountFilterItem { Name = "Odłożona gotówka", IsSelected = true });

            Accounts.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (AccountFilterItem ai in e.NewItems) ai.PropertyChanged += (_, __) => ApplyFilters();
                if (e.OldItems != null)
                    foreach (AccountFilterItem ai in e.OldItems) ai.PropertyChanged -= (_, __) => ApplyFilters();
                ApplyFilters();
            };

            foreach (var ai in Accounts)
                ai.PropertyChanged += (_, __) => ApplyFilters();
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
            }
            catch { }
        }

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

                bool isFuture = d != DateTime.MinValue && d.Date > today;
                bool isPlannedOrFuture = t.IsPlanned || isFuture;

                t.IsFuture = isFuture;

                if (isPlannedOrFuture) PlannedTransactions.Add(t);
                else Transactions.Add(t);
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

        // ------------------ WYŚWIETLANIE KONT (PaymentKind/Ref) ------------------

        private string ResolvePaymentDisplay(int paymentKind, int? paymentRefId, int? fallbackAccountId = null)
        {
            // 0 FreeCash, 1 SavedCash, 2 BankAccount, 3 Envelope (wg Twojego kodu)
            if (paymentKind == 0) return "Wolna gotówka";
            if (paymentKind == 1) return "Odłożona gotówka";

            if (paymentRefId.HasValue)
            {
                if (_accountNameById.TryGetValue(paymentRefId.Value, out var acc))
                    return acc;

                if (_envelopeNameById.TryGetValue(paymentRefId.Value, out var env))
                    return $"Koperta: {env}";
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
                    return $"Koperta: {env2}";

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
                "envelope" => (id.HasValue && _envelopeNameById.TryGetValue(id.Value, out var env)) ? $"Koperta: {env}" : "Koperta",
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

            // Jeśli już jest w formacie "Koperta: X" – zostaw
            if (s.StartsWith("Koperta:", StringComparison.CurrentCultureIgnoreCase))
                return s;

            // Jeśli Source pasuje nazwą do konta bankowego – zwróć nazwę konta
            foreach (var kv in _accountNameById)
            {
                if (string.Equals(kv.Value, s, StringComparison.CurrentCultureIgnoreCase))
                    return kv.Value;
            }

            // Jeśli Source pasuje nazwą do koperty – zwróć "Koperta: X"
            foreach (var kv in _envelopeNameById)
            {
                if (string.Equals(kv.Value, s, StringComparison.CurrentCultureIgnoreCase))
                    return $"Koperta: {kv.Value}";
            }

            // W innym przypadku pokaż tekst Source (to jest Twoje „źródło / konto docelowe” z UI)
            return s;
        }



        // ------------------ DODAWANIE WIERSZY Z DB ------------------

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
                CategoryIcon = "?",

                AccountName = fromName,
                FromAccountName = fromName,
                ToAccountName = null,

                PaymentKind = pk,
                PaymentRefId = pr,

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

            // 1) Najpierw próbuj stabilnego księgowania (PaymentKind/PaymentRefId)
            string toName = ResolvePaymentDisplay(pk, pr, fallbackAccountId: null);

            // 2) Jeśli PaymentKind wskazuje na gotówkę (0/1) ALBO wynik jest nieprzydatny,
            //    a Source ma wartość – to Source ma pierwszeństwo do wyświetlenia.
            var sourceResolved = ResolveIncomeTargetFromSource(sourceTxt);

            bool paymentLooksLikeCash = (pk == 0 || pk == 1);
            bool paymentUnresolved = string.IsNullOrWhiteSpace(toName) || toName == "?";

            if (!string.IsNullOrWhiteSpace(sourceResolved) && (paymentLooksLikeCash || paymentUnresolved))
            {
                toName = sourceResolved;
            }

            // 3) Ostateczny fallback (gdy nie ma ani payment ani source)
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
                CategoryIcon = "+",

                AccountName = toName,
                FromAccountName = null,
                ToAccountName = toName,

                PaymentKind = pk,
                PaymentRefId = pr,

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

            string fromKind = r.Table.Columns.Contains("FromKind") ? (r["FromKind"]?.ToString() ?? "") : "";
            string toKind = r.Table.Columns.Contains("ToKind") ? (r["ToKind"]?.ToString() ?? "") : "";

            int? fromRef = null;
            try
            {
                if (r.Table.Columns.Contains("FromRefId") && r["FromRefId"] != DBNull.Value)
                    fromRef = Convert.ToInt32(r["FromRefId"]);
            }
            catch { }

            int? toRef = null;
            try
            {
                if (r.Table.Columns.Contains("ToRefId") && r["ToRefId"] != DBNull.Value)
                    toRef = Convert.ToInt32(r["ToRefId"]);
            }
            catch { }

            string fromName = ResolveAccountDisplay(fromKind, fromRef);
            string toName = ResolveAccountDisplay(toKind, toRef);

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
                CategoryIcon = "⇄",

                AccountName = $"{fromName} -> {toName}",
                FromAccountName = fromName,
                ToAccountName = toName,

                SelectedCategory = "Transfer",
                EditDescription = desc,
                EditDate = date
            });
        }

        public void RefreshData() => ApplyFilters();

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

        // ------------------ INLINE EDIT (tylko opis/data/kategoria) ------------------

        public void StartEdit(TransactionCardVm vm)
        {
            if (vm == null) return;

            LoadAvailableLists();

            vm.EditDescription = vm.Description ?? string.Empty;

            if (TryParseDateDisplay(vm.DateDisplay, out var parsed))
                vm.EditDate = parsed.Date;
            else
                vm.EditDate = DateTime.Today;

            vm.SelectedCategory = string.IsNullOrWhiteSpace(vm.CategoryName)
                ? (vm.Kind == TransactionKind.Income ? "Przychód" : "(brak)")
                : vm.CategoryName;

            vm.IsEditing = true;
        }

        public void SaveEdit(TransactionCardVm vm)
        {
            if (vm == null) return;

            try
            {
                DateTime newDate = (vm.EditDate ?? DateTime.Today).Date;
                string newDesc = vm.EditDescription ?? string.Empty;
                string? selectedCat = vm.SelectedCategory?.Trim();

                bool newPlanned = newDate.Date > DateTime.Today;

                switch (vm.Kind)
                {
                    case TransactionKind.Expense:
                        SaveEditExpense(vm, newDate, newDesc, selectedCat, newPlanned);
                        break;

                    case TransactionKind.Income:
                        SaveEditIncome(vm, newDate, newDesc, selectedCat, newPlanned);
                        break;

                    case TransactionKind.Transfer:
                        SaveEditTransfer(vm, newDate, newDesc, newPlanned);
                        break;
                }
            }
            catch
            {
                // opcjonalnie: komunikat
            }
            finally
            {
                vm.IsEditing = false;
                LoadFromDatabase();
            }
        }

        private void SaveEditTransfer(TransactionCardVm vm, DateTime newDate, string newDesc, bool newPlanned)
        {
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = @"
SELECT Amount, Date, COALESCE(IsPlanned,0) AS IsPlanned
FROM Transfers
WHERE Id=@id AND UserId=@u
LIMIT 1;";
                read.Parameters.AddWithValue("@id", vm.Id);
                read.Parameters.AddWithValue("@u", UserId);

                using var r = read.ExecuteReader();
                if (!r.Read())
                {
                    tx.Rollback();
                    return;
                }
            }

            using (var upd = c.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = @"
UPDATE Transfers
SET Date=@d,
    Description=@desc,
    IsPlanned=@p
WHERE Id=@id AND UserId=@u;";
                upd.Parameters.AddWithValue("@d", ToIso(newDate));
                upd.Parameters.AddWithValue("@desc", (object?)newDesc ?? DBNull.Value);
                upd.Parameters.AddWithValue("@p", newPlanned ? 1 : 0);
                upd.Parameters.AddWithValue("@id", vm.Id);
                upd.Parameters.AddWithValue("@u", UserId);

                upd.ExecuteNonQuery();
            }

            tx.Commit();

            vm.DateDisplay = ToUiDate(newDate);
            vm.Description = newDesc;
            vm.IsPlanned = newPlanned;
            vm.IsFuture = newPlanned;
        }

        private void SaveEditExpense(TransactionCardVm vm, DateTime newDate, string newDesc, string? selectedCat, bool newPlanned)
        {
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            int dbUserId;
            decimal amount;
            DateTime oldDate;
            bool dbIsPlanned;
            int paymentKind;
            int? paymentRefId;

            using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = @"
SELECT UserId,
       Amount,
       Date,
       COALESCE(IsPlanned,0) AS IsPlanned,
       COALESCE(PaymentKind,0) AS PaymentKind,
       PaymentRefId
FROM Expenses
WHERE Id=@id AND UserId=@u
LIMIT 1;";
                read.Parameters.AddWithValue("@id", vm.Id);
                read.Parameters.AddWithValue("@u", UserId);

                using var r = read.ExecuteReader();
                if (!r.Read())
                {
                    tx.Rollback();
                    return;
                }

                dbUserId = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
                amount = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
                var dateTxt = r.IsDBNull(2) ? "" : (r.GetValue(2)?.ToString() ?? "");
                dbIsPlanned = !r.IsDBNull(3) && Convert.ToInt32(r.GetValue(3)) == 1;
                paymentKind = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4));
                paymentRefId = r.IsDBNull(5) ? (int?)null : Convert.ToInt32(r.GetValue(5));

                oldDate =
                    DateTime.TryParseExact(dateTxt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1) ? d1 :
                    (DateTime.TryParse(dateTxt, out var d2) ? d2 : DateTime.MinValue);
            }

            if (dbUserId != UserId)
            {
                tx.Rollback();
                return;
            }

            int catId = 0;
            if (!string.IsNullOrWhiteSpace(selectedCat) &&
                !string.Equals(selectedCat, "(brak)", StringComparison.CurrentCultureIgnoreCase))
            {
                try
                {
                    var id = DatabaseService.GetOrCreateCategoryId(UserId, selectedCat!);
                    if (id > 0) catId = id;
                }
                catch { catId = 0; }
            }

            bool oldPlanned = dbIsPlanned || (oldDate != DateTime.MinValue && oldDate.Date > DateTime.Today);

            if (oldPlanned != newPlanned)
            {
                if (oldPlanned && !newPlanned)
                {
                    LedgerService.ApplyExpenseEffect(c, tx, UserId, amount, paymentKind, paymentRefId);
                }
                else if (!oldPlanned && newPlanned)
                {
                    LedgerService.RevertExpenseEffect(c, tx, UserId, amount, paymentKind, paymentRefId);
                }
            }

            using (var upd = c.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = @"
UPDATE Expenses
SET Date=@d,
    Description=@desc,
    CategoryId=@cat,
    IsPlanned=@p
WHERE Id=@id AND UserId=@u;";
                upd.Parameters.AddWithValue("@d", ToIso(newDate));
                upd.Parameters.AddWithValue("@desc", (object?)newDesc ?? DBNull.Value);
                if (catId > 0) upd.Parameters.AddWithValue("@cat", catId);
                else upd.Parameters.AddWithValue("@cat", DBNull.Value);
                upd.Parameters.AddWithValue("@p", newPlanned ? 1 : 0);
                upd.Parameters.AddWithValue("@id", vm.Id);
                upd.Parameters.AddWithValue("@u", UserId);

                upd.ExecuteNonQuery();
            }

            tx.Commit();

            vm.DateDisplay = ToUiDate(newDate);
            vm.Description = newDesc;
            vm.CategoryName = string.IsNullOrWhiteSpace(selectedCat) ? "(brak)" : selectedCat!;
            vm.SelectedCategory = vm.CategoryName;
            vm.IsPlanned = newPlanned;
            vm.IsFuture = newPlanned;
        }

        private void SaveEditIncome(TransactionCardVm vm, DateTime newDate, string newDesc, string? selectedCat, bool newPlanned)
        {
            using var c = DatabaseService.GetConnection();
            DatabaseService.EnsureTables();
            using var tx = c.BeginTransaction();

            var info = ReadIncomeLedgerInfo(c, tx, vm.Id);
            if (info == null || info.UserId != UserId)
            {
                tx.Rollback();
                return;
            }

            bool oldPlanned = info.IsPlanned || info.Date.Date > DateTime.Today;

            int? catId = null;
            if (!string.IsNullOrWhiteSpace(selectedCat) &&
                !string.Equals(selectedCat, "Przychód", StringComparison.CurrentCultureIgnoreCase))
            {
                try
                {
                    var id = DatabaseService.GetOrCreateCategoryId(UserId, selectedCat!);
                    if (id > 0) catId = id;
                }
                catch { catId = null; }
            }

            if (oldPlanned != newPlanned)
            {
                if (oldPlanned && !newPlanned)
                {
                    LedgerService.ApplyIncomeEffect(c, tx, UserId, info.Amount, info.PaymentKind, info.PaymentRefId);
                }
                else if (!oldPlanned && newPlanned)
                {
                    LedgerService.RevertIncomeEffect(c, tx, UserId, info.Amount, info.PaymentKind, info.PaymentRefId);
                }
            }

            UpdateIncomeRowSql(c, tx, UserId, vm.Id, newDate, newDesc, catId, newPlanned);

            tx.Commit();

            vm.DateDisplay = ToUiDate(newDate);
            vm.Description = newDesc;
            vm.CategoryName = string.IsNullOrWhiteSpace(selectedCat) ? "Przychód" : selectedCat!;
            vm.SelectedCategory = vm.CategoryName;

            vm.IsPlanned = newPlanned;
            vm.IsFuture = newPlanned;
        }

        // ------------------ SQL UPDATE HELPERS ------------------

        private static void UpdateExpenseRowSql(SqliteConnection c, SqliteTransaction tx, Expense exp)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = @"
UPDATE Expenses
SET Date=@d,
    Description=@desc,
    CategoryId=@cat,
    IsPlanned=@p
WHERE Id=@id AND UserId=@u;";

            cmd.Parameters.AddWithValue("@d", ToIso(exp.Date));
            cmd.Parameters.AddWithValue("@desc", (object?)exp.Description ?? DBNull.Value);

            if (exp.CategoryId > 0) cmd.Parameters.AddWithValue("@cat", exp.CategoryId);
            else cmd.Parameters.AddWithValue("@cat", DBNull.Value);

            cmd.Parameters.AddWithValue("@p", exp.IsPlanned ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", exp.Id);
            cmd.Parameters.AddWithValue("@u", exp.UserId);

            cmd.ExecuteNonQuery();
        }

        private static void UpdateIncomeRowSql(
            SqliteConnection c,
            SqliteTransaction tx,
            int userId,
            int incomeId,
            DateTime date,
            string description,
            int? categoryId,
            bool isPlanned)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = @"
UPDATE Incomes
SET Date=@d,
    Description=@desc,
    CategoryId=@cat,
    IsPlanned=@p
WHERE Id=@id AND UserId=@u;";

            cmd.Parameters.AddWithValue("@d", ToIso(date));
            cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);

            if (categoryId.HasValue && categoryId.Value > 0) cmd.Parameters.AddWithValue("@cat", categoryId.Value);
            else cmd.Parameters.AddWithValue("@cat", DBNull.Value);

            cmd.Parameters.AddWithValue("@p", isPlanned ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", incomeId);
            cmd.Parameters.AddWithValue("@u", userId);

            cmd.ExecuteNonQuery();
        }

        private sealed class IncomeLedgerInfo
        {
            public int UserId { get; set; }
            public decimal Amount { get; set; }
            public DateTime Date { get; set; }
            public bool IsPlanned { get; set; }
            public int PaymentKind { get; set; }
            public int? PaymentRefId { get; set; }
        }

        private static IncomeLedgerInfo? ReadIncomeLedgerInfo(SqliteConnection c, SqliteTransaction tx, int incomeId)
        {
            if (!DatabaseService.TableExists(c, "Incomes")) return null;

            bool hasPlanned = DatabaseService.ColumnExists(c, "Incomes", "IsPlanned");
            bool hasPk = DatabaseService.ColumnExists(c, "Incomes", "PaymentKind");
            bool hasPr = DatabaseService.ColumnExists(c, "Incomes", "PaymentRefId");

            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = @"
SELECT UserId,
       Amount,
       Date,
       " + (hasPlanned ? "IsPlanned" : "0") + @" AS IsPlanned,
       " + (hasPk ? "PaymentKind" : "0") + @" AS PaymentKind,
       " + (hasPr ? "PaymentRefId" : "NULL") + @" AS PaymentRefId
FROM Incomes
WHERE Id=@id
LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", incomeId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            var dateTxt = r.IsDBNull(2) ? "" : (r.GetValue(2)?.ToString() ?? "");
            DateTime dt =
                DateTime.TryParseExact(dateTxt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1) ? d1 :
                (DateTime.TryParse(dateTxt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2) ? d2 :
                 (DateTime.TryParse(dateTxt, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d3) ? d3 : DateTime.MinValue));

            return new IncomeLedgerInfo
            {
                UserId = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                Amount = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1)),
                Date = dt,
                IsPlanned = !r.IsDBNull(3) && Convert.ToInt32(r.GetValue(3)) == 1,
                PaymentKind = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4)),
                PaymentRefId = r.IsDBNull(5) ? (int?)null : Convert.ToInt32(r.GetValue(5))
            };
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

                // WYSZUKIWANIE: także po kwocie (AmountStr)
                bool textOk = true;

                if (!string.IsNullOrWhiteSpace(q))
                {
                    // 1) klasyczne pola tekstowe
                    bool textHit =
                        (t.CategoryName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        || (t.Description?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        || (t.AccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        || (t.FromAccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        || (t.ToAccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);

                    // 2) kwota: wspieramy wpisy typu: "100", "100,5", "100.50", "1 000", "1000zł"
                    bool amountHit = false;

                    var qClean = new string(q.Where(ch => char.IsDigit(ch) || ch == ',' || ch == '.' || ch == '-').ToArray());
                    if (!string.IsNullOrWhiteSpace(qClean))
                    {
                        // próbujemy zinterpretować jako liczbę
                        if (decimal.TryParse(qClean, NumberStyles.Number, CultureInfo.CurrentCulture, out var qNum)
                            || decimal.TryParse(qClean, NumberStyles.Number, CultureInfo.InvariantCulture, out qNum))
                        {
                            var amt = ParseAmountInternal(t.AmountStr); // Twoja metoda zwraca abs()
                                                                        // tolerancja 1 grosz
                            amountHit = Math.Abs(amt - Math.Abs(qNum)) < 0.01m;
                        }
                        else
                        {
                            // fallback: tekstowo po AmountStr (np. ktoś wpisze "8 877")
                            amountHit = (t.AmountStr?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
                        }
                    }

                    textOk = textHit || amountHit;
                }


                return dateOk && typeOk && accOk && catOk && textOk;
            }

            var leftRaw = AllTransactions
                .Where(t => PassCommonFilters(t) && !(t.IsPlanned || t.IsFuture));

            var leftList = (SortMode == TransactionSortMode.DateAsc)
                ? leftRaw.OrderBy(t => SortKeyDate(t, plannedList: false)).ToList()
                : leftRaw.OrderByDescending(t => SortKeyDate(t, plannedList: false)).ToList();

            TransactionsList.Clear();
            foreach (var i in leftList) TransactionsList.Add(i);

            var rightRaw = AllTransactions
                .Where(t => PassCommonFilters(t) && (t.IsPlanned || t.IsFuture));

            var rightList = (SortMode == TransactionSortMode.DateAsc)
                ? rightRaw.OrderBy(t => SortKeyDate(t, plannedList: true)).ToList()
                : rightRaw.OrderByDescending(t => SortKeyDate(t, plannedList: true)).ToList();

            PlannedTransactionsList.Clear();
            foreach (var r in rightList) PlannedTransactionsList.Add(r);

            // KPI licz z lewej (zrealizowane)
            TotalExpenses = leftList.Where(t => t.Kind == TransactionKind.Expense)
                .Select(x => ParseAmountInternal(x.AmountStr)).Sum();

            TotalIncomes = leftList.Where(t => t.Kind == TransactionKind.Income)
                .Select(x => ParseAmountInternal(x.AmountStr)).Sum();
        }

        public void SetPeriod(DateRangeMode mode, DateTime start, DateTime end)
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

        public class TransactionCardVm : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise([CallerMemberName] string? n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

            public int Id { get; set; }

            private string _categoryName = "";
            public string CategoryName
            {
                get => _categoryName;
                set { _categoryName = value; Raise(); }
            }

            private string _description = "";
            public string Description
            {
                get => _description;
                set { _description = value; Raise(); }
            }

            private string _dateDisplay = "";
            public string DateDisplay
            {
                get => _dateDisplay;
                set { _dateDisplay = value; Raise(); }
            }

            private string _amountStr = "";
            public string AmountStr
            {
                get => _amountStr;
                set { _amountStr = value; Raise(); }
            }

            public bool IsPlanned { get; set; }
            public bool IsFuture { get; set; }

            public string CategoryIcon { get; set; } = "";

            private string _accountName = "";
            public string AccountName
            {
                get => _accountName;
                set { _accountName = value; Raise(); }
            }

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

            // do księgowania przy edycji Income/Expense (planowane<->zrealizowane)
            public int PaymentKind { get; set; }
            public int? PaymentRefId { get; set; }

            public TransactionKind Kind { get; set; } = TransactionKind.Expense;

            public bool IsTransfer => Kind == TransactionKind.Transfer;
            public bool IsExpense => Kind == TransactionKind.Expense;
            public bool IsIncome => Kind == TransactionKind.Income;

            public bool ShowFromAccount => Kind == TransactionKind.Transfer || Kind == TransactionKind.Expense;
            public bool ShowToAccount => Kind == TransactionKind.Transfer || Kind == TransactionKind.Income;

            public string FromAccountLabel => $"Z konta: {FromAccountName}";
            public string ToAccountLabel => $"Na konto: {ToAccountName}";

            private bool _isEditing;
            public bool IsEditing
            {
                get => _isEditing;
                set { _isEditing = value; Raise(); }
            }

            private string _editDescription = "";
            public string EditDescription
            {
                get => _editDescription;
                set { _editDescription = value; Raise(); }
            }

            private DateTime? _editDate = DateTime.Today;
            public DateTime? EditDate
            {
                get => _editDate;
                set { _editDate = value; Raise(); }
            }

            private string _selectedCategory = "";
            public string SelectedCategory
            {
                get => _selectedCategory;
                set { _selectedCategory = value; Raise(); }
            }

            private bool _isDeleteConfirmationVisible;
            public bool IsDeleteConfirmationVisible
            {
                get => _isDeleteConfirmationVisible;
                set { _isDeleteConfirmationVisible = value; Raise(); }
            }

            public ICommand ShowDeleteConfirmationCommand { get; }
            public ICommand HideDeleteConfirmationCommand { get; }

            public TransactionCardVm()
            {
                ShowDeleteConfirmationCommand = new DelegateCommand(_ => IsDeleteConfirmationVisible = true);
                HideDeleteConfirmationCommand = new DelegateCommand(_ => IsDeleteConfirmationVisible = false);
            }

            public sealed class SortModeToPolishTextConverter : IValueConverter
            {
                public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                {
                    if (value is TransactionsViewModel.TransactionSortMode m)
                    {
                        return m switch
                        {
                            TransactionsViewModel.TransactionSortMode.DateDesc => "Od najnowszych",
                            TransactionsViewModel.TransactionSortMode.DateAsc => "Od najstarszych",
                            _ => m.ToString()
                        };
                    }
                    return value?.ToString() ?? "";
                }

                public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                    => Binding.DoNothing;
            }
        }
    }
}
