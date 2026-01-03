using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Finly.Models;
using Finly.Services.Features;

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

        // Listy do edycji (ComboBox)
        public ObservableCollection<string> AvailableCategories { get; } = new();
        public ObservableCollection<string> AvailableAccounts { get; } = new();

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
                if (obj is TransactionCardVm vm)
                {
                    DeleteTransaction(vm);
                    vm.IsDeleteConfirmationVisible = false;
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

            AvailableAccounts.Clear();
            try
            {
                var accs = DatabaseService.GetAccounts(UserId) ?? new List<BankAccountModel>();
                foreach (var a in accs) AvailableAccounts.Add(a.AccountName);

                var envs = DatabaseService.GetEnvelopesNames(UserId) ?? new List<string>();
                foreach (var e in envs) AvailableAccounts.Add($"Koperta: {e}");

                if (!AvailableAccounts.Contains("Wolna gotówka")) AvailableAccounts.Add("Wolna gotówka");
                if (!AvailableAccounts.Contains("Odłożona gotówka")) AvailableAccounts.Add("Odłożona gotówka");
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
                bool isPlannedOrFuture = t.IsPlanned ||
                                         (DateTime.TryParse(t.DateDisplay, out var d) && d.Date > today);

                if (isPlannedOrFuture) PlannedTransactions.Add(t);
                else Transactions.Add(t);
            }
        }

        // ------------------ WYŚWIETLANIE KONT (PaymentKind/Ref) ------------------

        private string ResolvePaymentDisplay(int paymentKind, int? paymentRefId, int? fallbackAccountId = null)
        {
            if (paymentKind == 0) return "Wolna gotówka";
            if (paymentKind == 1) return "Odłożona gotówka";

            // Jeśli mamy refId, rozpoznaj po słownikach (bezpieczne).
            if (paymentRefId.HasValue)
            {
                if (_accountNameById.TryGetValue(paymentRefId.Value, out var acc))
                    return acc;

                if (_envelopeNameById.TryGetValue(paymentRefId.Value, out var env))
                    return $"Koperta: {env}";
            }

            // Zakładamy: 2=Bank, 3=Envelope (nie ruszamy Twojej logiki).
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

        // ------------------ DODAWANIE WIERSZY Z DB ------------------

        private void AddExpenseRow(DataRow r)
        {
            int id = Convert.ToInt32(r["Id"]);
            double amt = Convert.ToDouble(r["Amount"]);
            DateTime date = ParseDate(r["Date"]);
            string desc = r["Description"]?.ToString() ?? string.Empty;
            string catName = r["CategoryName"]?.ToString() ?? string.Empty;

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

            // WYDATEK: “Z konta: ...”
            string fromName = ResolvePaymentDisplay(pk, pr, fallbackAccountId: accountId);

            AllTransactions.Add(new TransactionCardVm
            {
                Id = id,
                Kind = TransactionKind.Expense,
                CategoryName = string.IsNullOrWhiteSpace(catName) ? "(brak)" : catName,
                Description = desc,
                DateDisplay = date.ToString("yyyy-MM-dd"),
                AmountStr = amt.ToString("N2") + " zł",
                IsPlanned = planned,
                IsFuture = date.Date > DateTime.Today,
                CategoryIcon = "?",

                // kompatybilność / filtry (trzymamy w AccountName też tę nazwę)
                AccountName = fromName,

                FromAccountName = fromName,
                ToAccountName = null,

                SelectedCategory = string.IsNullOrWhiteSpace(catName) ? "(brak)" : catName,
                SelectedAccount = fromName,
                EditAmountText = amt.ToString("N2"),
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

            // PRZYCHÓD: “Na konto: ...”
            string toName = string.Empty;

            // 1) AccountId -> słownik
            if (r.Table.Columns.Contains("AccountId") && r["AccountId"] != DBNull.Value)
            {
                var accId = Convert.ToInt32(r["AccountId"]);
                if (_accountNameById.TryGetValue(accId, out var accName))
                    toName = accName;
            }

            // 2) legacy Source
            if (string.IsNullOrWhiteSpace(toName) &&
                r.Table.Columns.Contains("Source") &&
                r["Source"] != DBNull.Value)
            {
                toName = r["Source"]?.ToString() ?? string.Empty;
            }

            // 3) default
            if (string.IsNullOrWhiteSpace(toName))
                toName = "Wolna gotówka";

            AllTransactions.Add(new TransactionCardVm
            {
                Id = id,
                Kind = TransactionKind.Income,
                CategoryName = string.IsNullOrEmpty(catName) ? "Przychód" : catName,
                Description = desc,
                DateDisplay = date.ToString("yyyy-MM-dd"),
                AmountStr = amt.ToString("N2") + " zł",
                IsPlanned = planned,
                IsFuture = date.Date > DateTime.Today,
                CategoryIcon = "+",

                // kompatybilność / filtry
                AccountName = toName,

                FromAccountName = null,
                ToAccountName = toName,

                SelectedCategory = string.IsNullOrWhiteSpace(catName) ? "Przychód" : catName,
                SelectedAccount = toName,
                EditAmountText = amt.ToString("N2"),
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

            // Transfery też mogą być planowane
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
                DateDisplay = date.ToString("yyyy-MM-dd"),
                AmountStr = amt.ToString("N2") + " zł",
                IsPlanned = planned,
                IsFuture = date.Date > DateTime.Today,
                CategoryIcon = "⇄",

                // dla transferu AccountName nie jest istotne; ale dla filtrów i wyszukiwarki
                // wygodnie dać “from -> to”
                AccountName = $"{fromName} -> {toName}",

                FromAccountName = fromName,
                ToAccountName = toName,

                SelectedCategory = "Transfer",
                SelectedAccount = string.Empty,
                EditAmountText = amt.ToString("N2"),
                EditDescription = desc,
                EditDate = date
            });
        }

        private string ResolveAccountDisplay(string kind, int? id)
        {
            var k = (kind ?? string.Empty).Trim().ToLowerInvariant();

            return k switch
            {
                "bank" => (id.HasValue && _accountNameById.TryGetValue(id.Value, out var acc)) ? acc : "Konto bankowe",
                "envelope" => (id.HasValue && _envelopeNameById.TryGetValue(id.Value, out var env)) ? $"Koperta: {env}" : "Koperta",
                "freecash" => "Wolna gotówka",
                "cash" => "Wolna gotówka", // legacy
                "savedcash" => "Odłożona gotówka",
                "saved" => "Odłożona gotówka", // legacy
                _ => "?"
            };
        }

        private DateTime ParseDate(object? raw)
        {
            if (raw is DateTime dt) return dt;
            if (DateTime.TryParse(raw?.ToString(), out var p)) return p;
            return DateTime.MinValue;
        }

        public void RefreshData() => ApplyFilters();

        private decimal ParseAmountInternal(string amountStr)
        {
            if (string.IsNullOrWhiteSpace(amountStr)) return 0m;

            var txt = new string(amountStr.Where(ch => char.IsDigit(ch) || ch == ',' || ch == '.' || ch == '-').ToArray());
            if (decimal.TryParse(txt, out var v)) return Math.Abs(v);

            return 0m;
        }

        public void DeleteTransaction(TransactionCardVm vm)
        {
            if (vm == null) return;

            try
            {
                switch (vm.Kind)
                {
                    case TransactionKind.Expense:
                        DatabaseService.DeleteExpense(vm.Id);
                        break;

                    case TransactionKind.Income:
                        DatabaseService.DeleteIncome(vm.Id);
                        break;

                    case TransactionKind.Transfer:
                        // Bezpiecznie: jeśli metoda istnieje, użyj. Jeśli nie – nie wywal UI.
                        try
                        {
                            var m = typeof(DatabaseService).GetMethod("DeleteTransfer");
                            if (m != null) m.Invoke(null, new object[] { vm.Id });
                        }
                        catch { }
                        break;
                }
            }
            catch { }

            LoadFromDatabase();
        }

        // ------------------ INLINE EDIT (jak miałaś, bez rozwalania księgowania) ------------------

        public void StartEdit(TransactionCardVm vm)
        {
            if (vm == null) return;

            LoadAvailableLists();

            vm.EditDescription = vm.Description ?? string.Empty;
            vm.EditAmountText = ParseAmountInternal(vm.AmountStr).ToString("N2");
            vm.EditDate = DateTime.TryParse(vm.DateDisplay, out var d) ? d : DateTime.Today;

            vm.SelectedCategory = string.IsNullOrWhiteSpace(vm.CategoryName)
                ? (vm.Kind == TransactionKind.Income ? "Przychód" :
                   vm.Kind == TransactionKind.Transfer ? "Transfer" : "(brak)")
                : vm.CategoryName;

            // Transfer nie ma pojedynczego konta do edycji w tym VM
            vm.SelectedAccount = vm.Kind == TransactionKind.Transfer ? string.Empty : (vm.AccountName ?? string.Empty);

            vm.IsEditing = true;
        }

        public void SaveEdit(TransactionCardVm vm)
        {
            if (vm == null) return;

            try
            {
                var amount = ParseAmountInternal(vm.EditAmountText ?? string.Empty);
                var date = vm.EditDate == default ? DateTime.Today : vm.EditDate;
                var desc = vm.EditDescription ?? string.Empty;
                var selectedCat = vm.SelectedCategory?.Trim();
                var selectedAcc = vm.SelectedAccount?.Trim();

                switch (vm.Kind)
                {
                    case TransactionKind.Expense:
                        {
                            var exp = DatabaseService.GetExpenseById(vm.Id);
                            if (exp != null)
                            {
                                exp.UserId = UserId;
                                exp.Amount = (double)amount;
                                exp.Date = date;
                                exp.Description = desc;

                                int? cid = null;
                                if (!string.IsNullOrWhiteSpace(selectedCat) &&
                                    !string.Equals(selectedCat, "(brak)", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    try
                                    {
                                        var id = DatabaseService.GetOrCreateCategoryId(UserId, selectedCat!);
                                        if (id > 0) cid = id;
                                    }
                                    catch { cid = null; }
                                }
                                exp.CategoryId = cid ?? 0;

                                DatabaseService.UpdateExpense(exp);
                            }

                            vm.AmountStr = amount.ToString("N2") + " zł";
                            vm.DateDisplay = date.ToString("yyyy-MM-dd");
                            vm.Description = desc;
                            vm.CategoryName = string.IsNullOrWhiteSpace(selectedCat) ? "(brak)" : selectedCat!;

                            // UWAGA: tu nie zmieniamy księgowania (PaymentKind/Ref),
                            // tylko tekst pomocniczy. Nie ruszamy sald.
                            if (!string.IsNullOrWhiteSpace(selectedAcc))
                            {
                                vm.AccountName = selectedAcc!;
                                vm.FromAccountName = selectedAcc!;
                            }

                            break;
                        }

                    case TransactionKind.Income:
                        {
                            int? cid = null;
                            if (!string.IsNullOrWhiteSpace(selectedCat) &&
                                !string.Equals(selectedCat, "Przychód", StringComparison.CurrentCultureIgnoreCase))
                            {
                                try
                                {
                                    var id = DatabaseService.GetOrCreateCategoryId(UserId, selectedCat!);
                                    if (id > 0) cid = id;
                                }
                                catch { cid = null; }
                            }

                            string? source = string.IsNullOrWhiteSpace(selectedAcc) ? null : selectedAcc;

                            // Zakładam Twoją sygnaturę UpdateIncome jak w kodzie bazowym.
                            DatabaseService.UpdateIncome(vm.Id, UserId, amount, desc, null, date, cid, source);

                            vm.AmountStr = amount.ToString("N2") + " zł";
                            vm.DateDisplay = date.ToString("yyyy-MM-dd");
                            vm.Description = desc;
                            vm.CategoryName = string.IsNullOrWhiteSpace(selectedCat) ? "Przychód" : selectedCat!;
                            if (!string.IsNullOrWhiteSpace(source))
                            {
                                vm.AccountName = source!;
                                vm.ToAccountName = source!;
                            }

                            break;
                        }

                    case TransactionKind.Transfer:
                        {
                            // Edycja transferów wymaga DB/Ledger – tu tylko UI.
                            vm.AmountStr = amount.ToString("N2") + " zł";
                            vm.DateDisplay = date.ToString("yyyy-MM-dd");
                            vm.Description = desc;
                            break;
                        }
                }
            }
            catch
            {
                // nie blokuj UI
            }
            finally
            {
                vm.IsEditing = false;
                ApplyFilters();
            }
        }

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
                // dla wydatku/przychodu filtrujemy po AccountName
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
                if (from.HasValue && to.HasValue && DateTime.TryParse(t.DateDisplay, out var d))
                    dateOk = d.Date >= from.Value.Date && d.Date <= to.Value.Date;

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

                bool textOk = string.IsNullOrEmpty(q)
                    || (t.CategoryName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    || (t.Description?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    || (t.AccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    || (t.FromAccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    || (t.ToAccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);

                return dateOk && typeOk && accOk && catOk && textOk;
            }

            var left = AllTransactions.Where(t => PassCommonFilters(t) && !(t.IsPlanned || t.IsFuture)).ToList();
            TransactionsList.Clear();
            foreach (var i in left) TransactionsList.Add(i);

            var right = AllTransactions.Where(t => PassCommonFilters(t) && (t.IsPlanned || t.IsFuture))
                .OrderBy(t => t.DateDisplay)
                .ToList();
            PlannedTransactionsList.Clear();
            foreach (var r in right) PlannedTransactionsList.Add(r);

            TotalExpenses = left.Where(t => t.Kind == TransactionKind.Expense)
                .Select(x => ParseAmountInternal(x.AmountStr)).Sum();

            TotalIncomes = left.Where(t => t.Kind == TransactionKind.Income)
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

            public TransactionKind Kind { get; set; } = TransactionKind.Expense;

            public bool IsTransfer => Kind == TransactionKind.Transfer;
            public bool IsExpense => Kind == TransactionKind.Expense;
            public bool IsIncome => Kind == TransactionKind.Income;

            // Co pokazujemy w UI:
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

            public string EditAmountText { get; set; } = "";
            public string EditDescription { get; set; } = "";
            public DateTime EditDate { get; set; } = DateTime.Today;
            public string SelectedCategory { get; set; } = "";
            public string SelectedAccount { get; set; } = "";

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
        }
    }
}
