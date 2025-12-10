using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Finly.Services;
using System.Data;
using Finly.Models;
using System.Collections.Generic;
using System.Windows.Input;

namespace Finly.ViewModels
{
 public class TransactionsViewModel : INotifyPropertyChanged
 {
 public event PropertyChangedEventHandler? PropertyChanged;
 private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

 public int UserId { get; private set; }

 // Wszystkie transakcje za³adowane z DB (bez filtrów)
 public ObservableCollection<TransactionCardVm> AllTransactions { get; } = new();

 // Nowe: podstawowe kolekcje po podziale na normalne i zaplanowane
 public ObservableCollection<TransactionCardVm> Transactions { get; } = new(); // zwyk³e (nieplanowane, nieprzysz³e)
 public ObservableCollection<TransactionCardVm> PlannedTransactions { get; } = new(); // zaplanowane (IsPlanned || przysz³a data)

 // Oddzielne listy do wyœwietlenia w lewej i prawej kolumnie (zachowane dla kompatybilnoœci)
 public ObservableCollection<TransactionCardVm> TransactionsList { get; } = new(); // zwyk³e
 public ObservableCollection<TransactionCardVm> PlannedTransactionsList { get; } = new(); // zaplanowane

 // Zachowane istniej¹ce: nadal u¿ywane przez starszy XAML (alias do lewej listy)
 public ObservableCollection<TransactionCardVm> FilteredTransactions => TransactionsList;

 private decimal _totalExpenses; public decimal TotalExpenses { get => _totalExpenses; private set { _totalExpenses = value; OnPropertyChanged(); OnPropertyChanged(nameof(Balance)); } }
 private decimal _totalIncomes; public decimal TotalIncomes { get => _totalIncomes; private set { _totalIncomes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Balance)); } }
 public decimal Balance => TotalIncomes - TotalExpenses;

 private string? _searchQuery; public string? SearchQuery { get => _searchQuery; set { _searchQuery = value; OnPropertyChanged(); ApplyFilters(); } }

 private bool _isToday; public bool IsToday { get => _isToday; set { if (value) { if (!_isToday) { ClearPeriods(); _isToday = true; OnPropertyChanged(); ApplyFilters(); } } } }
 private bool _isYesterday; public bool IsYesterday { get => _isYesterday; set { if (value) { if (!_isYesterday) { ClearPeriods(); _isYesterday = true; OnPropertyChanged(); ApplyFilters(); } } } }
 private bool _isThisWeek; public bool IsThisWeek { get => _isThisWeek; set { if (value) { if (!_isThisWeek) { ClearPeriods(); _isThisWeek = true; OnPropertyChanged(); ApplyFilters(); } } } }
 private bool _isThisMonth; public bool IsThisMonth { get => _isThisMonth; set { if (value) { if (!_isThisMonth) { ClearPeriods(); _isThisMonth = true; OnPropertyChanged(); ApplyFilters(); } } } }
 private bool _isPrevMonth; public bool IsPrevMonth { get => _isPrevMonth; set { if (value) { if (!_isPrevMonth) { ClearPeriods(); _isPrevMonth = true; OnPropertyChanged(); ApplyFilters(); } } } }
 private bool _isThisYear; public bool IsThisYear { get => _isThisYear; set { if (value) { if (!_isThisYear) { ClearPeriods(); _isThisYear = true; OnPropertyChanged(); ApplyFilters(); } } } }

 private bool _showExpenses = true; public bool ShowExpenses { get => _showExpenses; set { _showExpenses = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _showIncomes = true; public bool ShowIncomes { get => _showIncomes; set { _showIncomes = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _showTransfers = true; public bool ShowTransfers { get => _showTransfers; set { _showTransfers = value; OnPropertyChanged(); ApplyFilters(); } }

 public ObservableCollection<CategoryFilterItem> Categories { get; } = new();
 public ObservableCollection<AccountFilterItem> Accounts { get; } = new();

 private bool _showScheduled = true; public bool ShowScheduled { get => _showScheduled; set { _showScheduled = value; OnPropertyChanged(); /* UI ukrywa kolumnê */ } }

 public object? DateFrom { get; set; }
 public object? DateTo { get; set; }

 // lokalne s³owniki nazw kont/kopert do budowania nazw transferów i wydatków
 private Dictionary<int, string> _accountNameById = new();
 private Dictionary<int, string> _envelopeNameById = new();

 // Command: delete transaction (used by confirmation panel)
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
 LoadFromDatabase();
 if (!_isToday && !_isYesterday && !_isThisWeek && !_isThisMonth && !_isPrevMonth && !_isThisYear)
 {
 IsThisMonth = true; // domyœlny okres
 }
 DatabaseService.DataChanged += (_, __) => LoadFromDatabase();
 }

 private void LoadLookupData()
 {
 Categories.Clear();
 try { foreach (var c in DatabaseService.GetCategoriesByUser(UserId) ?? new System.Collections.Generic.List<string>()) Categories.Add(new CategoryFilterItem { Name = c, IsSelected = true }); } catch { }
 // ensure special categories exist
 void EnsureCat(string name)
 {
 if (!Categories.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
 Categories.Add(new CategoryFilterItem { Name = name, IsSelected = true });
 }
 EnsureCat("(brak)"); // expenses without category
 EnsureCat("Przychód"); // incomes without category
 EnsureCat("Transfer"); // transfers pseudo-category
 Categories.CollectionChanged += (s, e) =>
 {
 if (e.NewItems != null) foreach (CategoryFilterItem ci in e.NewItems) ci.PropertyChanged += (_, __) => ApplyFilters();
 if (e.OldItems != null) foreach (CategoryFilterItem ci in e.OldItems) ci.PropertyChanged -= (_, __) => ApplyFilters();
 ApplyFilters();
 };
 foreach (var ci in Categories) ci.PropertyChanged += (_, __) => ApplyFilters();

 Accounts.Clear();
 try { foreach (var a in DatabaseService.GetAccounts(UserId) ?? new System.Collections.Generic.List<Finly.Models.BankAccountModel>()) Accounts.Add(new AccountFilterItem { Name = a.AccountName, IsSelected = true }); } catch { }
 try { foreach (var env in DatabaseService.GetEnvelopesNames(UserId) ?? new System.Collections.Generic.List<string>()) Accounts.Add(new AccountFilterItem { Name = $"Koperta: {env}", IsSelected = true }); } catch { }
 Accounts.Add(new AccountFilterItem { Name = "Wolna gotówka", IsSelected = true });
 Accounts.Add(new AccountFilterItem { Name = "Od³o¿ona gotówka", IsSelected = true });
 // subscribe to account selection changes
 Accounts.CollectionChanged += (s, e) =>
 {
 if (e.NewItems != null) foreach (AccountFilterItem ai in e.NewItems) ai.PropertyChanged += (_, __) => ApplyFilters();
 if (e.OldItems != null) foreach (AccountFilterItem ai in e.OldItems) ai.PropertyChanged -= (_, __) => ApplyFilters();
 ApplyFilters();
 };
 foreach (var ai in Accounts) ai.PropertyChanged += (_, __) => ApplyFilters();
 }

 public void LoadFromDatabase()
 {
 if (UserId <=0) return;
 AllTransactions.Clear();

 // zbuduj s³owniki pomocnicze
 try { _accountNameById = DatabaseService.GetAccounts(UserId).ToDictionary(a => a.Id, a => a.AccountName); } catch { _accountNameById = new(); }
 try { var envDt = DatabaseService.GetEnvelopesTable(UserId); _envelopeNameById = new(); if (envDt != null) foreach (DataRow rr in envDt.Rows) { try { var id = Convert.ToInt32(rr["Id"]); var nm = rr["Name"]?.ToString() ?? ""; if (id >0) _envelopeNameById[id] = nm; } catch { } } } catch { _envelopeNameById = new(); }

 DataTable expDt = null; try { expDt = DatabaseService.GetExpenses(UserId); } catch { }
 if (expDt != null) foreach (DataRow r in expDt.Rows) AddExpenseRow(r);

 DataTable incDt = null; try { incDt = DatabaseService.GetIncomes(UserId); } catch { }
 if (incDt != null) foreach (DataRow r in incDt.Rows) AddIncomeRow(r);

 DataTable trDt = null; try { trDt = DatabaseService.GetTransfers(UserId); } catch { }
 if (trDt != null) foreach (DataRow r in trDt.Rows) AddTransferRow(r);

 // Podzia³ na dwie kolekcje (normalne vs zaplanowane) zaraz po pobraniu z DB
 SplitTransactionsIntoPrimaryCollections();

 // Nastêpnie zastosuj istniej¹ce filtry do kolekcji widokowych (kompatybilnoœæ UI)
 ApplyFilters();
 }

 private void SplitTransactionsIntoPrimaryCollections()
 {
 Transactions.Clear();
 PlannedTransactions.Clear();
 var today = DateTime.Today;
 foreach (var t in AllTransactions)
 {
 bool isPlannedOrFuture = t.IsPlanned || (DateTime.TryParse(t.DateDisplay, out var d) && d.Date > today);
 if (isPlannedOrFuture) PlannedTransactions.Add(t); else Transactions.Add(t);
 }
 }

 private void AddExpenseRow(DataRow r)
 {
 int id = Convert.ToInt32(r["Id"]); double amt = Convert.ToDouble(r["Amount"]);
 DateTime date = ParseDate(r["Date"]); string desc = r["Description"]?.ToString() ?? string.Empty;
 string catName = r["CategoryName"]?.ToString() ?? string.Empty; bool planned = r.Table.Columns.Contains("IsPlanned") && r["IsPlanned"] != DBNull.Value && Convert.ToInt32(r["IsPlanned"]) ==1;
 int? accountId = r.Table.Columns.Contains("AccountId") && r["AccountId"] != DBNull.Value ? (int?)Convert.ToInt32(r["AccountId"]) : null;
 string accountName = string.Empty;
 if (accountId.HasValue && _accountNameById.TryGetValue(accountId.Value, out var accn)) accountName = accn; else accountName = "Wolna gotówka"; // domyœlnie gotówka wolna
 AllTransactions.Add(new TransactionCardVm {
 Id = id,
 Kind = TransactionKind.Expense,
 CategoryName = catName,
 Description = desc,
 DateDisplay = date.ToString("yyyy-MM-dd"),
 AmountStr = amt.ToString("N2") + " z³",
 IsPlanned = planned,
 IsFuture = date.Date > DateTime.Today,
 CategoryIcon = "?",

 AccountName = accountName,
 // inline edit defaults
 SelectedCategory = string.IsNullOrWhiteSpace(catName) ? "(brak)" : catName,
 SelectedAccount = accountName,
 EditAmountText = amt.ToString("N2"),
 EditDescription = desc,
 EditDate = date
 });
 }
 private void AddIncomeRow(DataRow r)
 {
 int id = Convert.ToInt32(r["Id"]); decimal amt = Convert.ToDecimal(r["Amount"]);
 DateTime date = ParseDate(r["Date"]); string desc = r["Description"]?.ToString() ?? string.Empty;

 string catName = r.Table.Columns.Contains("CategoryName")
 ? (r["CategoryName"]?.ToString() ?? string.Empty)
 : string.Empty;

 bool planned = r.Table.Columns.Contains("IsPlanned")
 && r["IsPlanned"] != DBNull.Value
 && Convert.ToInt32(r["IsPlanned"]) ==1;

 // --- NOWE: pobierz nazwê konta tak jak dla wydatków ---
 string accountName = string.Empty;

 //1) je¿eli jest kolumna AccountId – u¿yj s³ownika _accountNameById
 if (r.Table.Columns.Contains("AccountId") && r["AccountId"] != DBNull.Value)
 {
 var accId = Convert.ToInt32(r["AccountId"]);
 if (_accountNameById.TryGetValue(accId, out var accName))
 accountName = accName;
 }

 //2) wsteczna kompatybilnoœæ: je¿eli jest kolumna Source z nazw¹ konta
 if (string.IsNullOrWhiteSpace(accountName)
 && r.Table.Columns.Contains("Source")
 && r["Source"] != DBNull.Value)
 {
 accountName = r["Source"]?.ToString() ?? string.Empty;
 }

 //3) je¿eli dalej pusto – traktuj jako woln¹ gotówkê
 if (string.IsNullOrWhiteSpace(accountName))
 accountName = "Wolna gotówka";

 AllTransactions.Add(new TransactionCardVm {
 Id = id,
 Kind = TransactionKind.Income,
 CategoryName = string.IsNullOrEmpty(catName) ? "Przychód" : catName,
 Description = desc,
 DateDisplay = date.ToString("yyyy-MM-dd"),
 AmountStr = amt.ToString("N2") + " z³",
 IsPlanned = planned,
 IsFuture = date.Date > DateTime.Today,
 CategoryIcon = "+",
 AccountName = accountName,
 SelectedCategory = string.IsNullOrWhiteSpace(catName) ? "Przychód" : catName,
 SelectedAccount = accountName,
 EditAmountText = amt.ToString("N2"),
 EditDescription = desc,
 EditDate = date
 });
 }
 private void AddTransferRow(DataRow r)
 {
 int id = Convert.ToInt32(r["Id"]); decimal amt = Convert.ToDecimal(r["Amount"]);
 DateTime date = ParseDate(r["Date"]); string desc = r["Description"]?.ToString() ?? "Transfer"; bool planned = r.Table.Columns.Contains("IsPlanned") && r["IsPlanned"] != DBNull.Value && Convert.ToInt32(r["IsPlanned"]) ==1;
 string fromKind = r.Table.Columns.Contains("FromKind") ? r["FromKind"]?.ToString() ?? "" : ""; string toKind = r.Table.Columns.Contains("ToKind") ? r["ToKind"]?.ToString() ?? "" : "";
 int? fromRef = null; try { if (r.Table.Columns.Contains("FromRefId") && r["FromRefId"] != DBNull.Value) fromRef = Convert.ToInt32(r["FromRefId"]); } catch { }
 int? toRef = null; try { if (r.Table.Columns.Contains("ToRefId") && r["ToRefId"] != DBNull.Value) toRef = Convert.ToInt32(r["ToRefId"]); } catch { }
 string fromName = ResolveAccountDisplay(fromKind, fromRef);
 string toName = ResolveAccountDisplay(toKind, toRef);
 string accountDisplay = fromName + " ? " + toName;
 AllTransactions.Add(new TransactionCardVm {
 Id = id,
 Kind = TransactionKind.Transfer,
 CategoryName = "Transfer",
 Description = desc,
 DateDisplay = date.ToString("yyyy-MM-dd"),
 AmountStr = amt.ToString("N2") + " z³",
 IsPlanned = planned,
 IsFuture = date.Date > DateTime.Today,
 CategoryIcon = "?",

 AccountName = accountDisplay,
 SelectedCategory = "Transfer",
 SelectedAccount = accountDisplay,
 EditAmountText = amt.ToString("N2"),
 EditDescription = desc,
 EditDate = date
 });
 }

 private string ResolveAccountDisplay(string kind, int? id)
 {
 switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
 {
 case "bank":
 if (id.HasValue && _accountNameById.TryGetValue(id.Value, out var acc)) return acc;
 return "Konto bankowe";
 case "cash":
 return "Wolna gotówka";
 case "saved":
 return "Od³o¿ona gotówka";
 case "envelope":
 if (id.HasValue && _envelopeNameById.TryGetValue(id.Value, out var env)) return $"Koperta: {env}";
 return "Koperta";
 default:
 return string.Empty;
 }
 }

 private DateTime ParseDate(object? raw) { if (raw is DateTime dt) return dt; if (DateTime.TryParse(raw?.ToString(), out var p)) return p; return DateTime.MinValue; }

 public void RefreshData() => ApplyFilters();

 private decimal ParseAmountInternal(string amountStr)
 {
 if (string.IsNullOrWhiteSpace(amountStr)) return 0m;
 var txt = new string(amountStr.Where(ch => char.IsDigit(ch) || ch == ',' || ch == '.' || ch == '-').ToArray());
 if (decimal.TryParse(txt, out var v)) return Math.Abs(v);
 return 0m;
 }

 public void DeleteTransaction(TransactionCardVm vm) { try { if (vm.Kind == TransactionKind.Expense) DatabaseService.DeleteExpense(vm.Id); else if (vm.Kind == TransactionKind.Income) DatabaseService.DeleteIncome(vm.Id); } catch { } LoadFromDatabase(); }
 public void AddExpense(decimal amount, DateTime date, string description, string? categoryName, bool planned = false) { int catId =0; if (!string.IsNullOrWhiteSpace(categoryName)) { try { catId = DatabaseService.GetOrCreateCategoryId(UserId, categoryName); } catch { } } var exp = new Finly.Models.Expense { UserId = UserId, Amount = (double)amount, Date = date, Description = description, CategoryId = catId, IsPlanned = planned }; try { DatabaseService.InsertExpense(exp); } catch { } LoadFromDatabase(); }
 public void AddIncome(decimal amount, DateTime date, string description, string? source, string? categoryName, bool planned = false){ int? catId = null; if(!string.IsNullOrWhiteSpace(categoryName)) { try { var id = DatabaseService.GetOrCreateCategoryId(UserId, categoryName); if(id>0) catId = id; } catch { } } try { DatabaseService.InsertIncome(UserId, amount, date, description, source, catId, planned); } catch { } LoadFromDatabase(); }
 public void UpdateTransaction(TransactionCardVm vm, decimal? newAmount = null, string? newDescription = null, bool? planned = null) { if (vm.Kind == TransactionKind.Expense) { var exp = DatabaseService.GetExpenseById(vm.Id); if (exp != null) { if (newAmount.HasValue) exp.Amount = (double)newAmount.Value; if (newDescription != null) exp.Description = newDescription; if (planned.HasValue) exp.IsPlanned = planned.Value; try { DatabaseService.UpdateExpense(exp); } catch { } } } else if (vm.Kind == TransactionKind.Income) { try { DatabaseService.UpdateIncome(vm.Id, UserId, newAmount, newDescription, planned); } catch { } } LoadFromDatabase(); }

 // ===== Inline edit API =====
 public void StartEdit(TransactionCardVm vm)
 {
 if (vm == null) return;
 // kopiuj aktualne dane do pól edycyjnych i w³¹cz tryb edycji
 vm.EditDescription = vm.Description ?? string.Empty;
 vm.EditAmountText = ParseAmountInternal(vm.AmountStr).ToString("N2");
 if (DateTime.TryParse(vm.DateDisplay, out var d)) vm.EditDate = d; else vm.EditDate = DateTime.Today;
 vm.SelectedCategory = string.IsNullOrWhiteSpace(vm.CategoryName) ? (vm.Kind == TransactionKind.Income ? "Przychód" : vm.Kind == TransactionKind.Transfer ? "Transfer" : "(brak)") : vm.CategoryName;
 vm.SelectedAccount = vm.AccountName ?? string.Empty;
 vm.IsEditing = true;
 }

 public void SaveEdit(TransactionCardVm vm)
 {
 if (vm == null) return;
 try
 {
 //1) parsowanie kwoty
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
 // kategoria: (brak) => null
 int? cid = null;
 if (!string.IsNullOrWhiteSpace(selectedCat) && !string.Equals(selectedCat, "(brak)", StringComparison.CurrentCultureIgnoreCase))
 {
 try { var id = DatabaseService.GetOrCreateCategoryId(UserId, selectedCat!); if (id >0) cid = id; } catch { cid = null; }
 }
 exp.CategoryId = cid ??0;
 DatabaseService.UpdateExpense(exp);
 }
 // odœwie¿ widok elementu
 vm.AmountStr = amount.ToString("N2") + " z³";
 vm.DateDisplay = date.ToString("yyyy-MM-dd");
 vm.Description = desc;
 vm.CategoryName = string.IsNullOrWhiteSpace(selectedCat) ? "(brak)" : selectedCat!;
 // konto na razie tylko lokalnie (brak kolumny do aktualizacji)
 if (!string.IsNullOrWhiteSpace(selectedAcc)) vm.AccountName = selectedAcc!;
 break;
 }
 case TransactionKind.Income:
 {
 int? cid = null;
 if (!string.IsNullOrWhiteSpace(selectedCat) && !string.Equals(selectedCat, "Przychód", StringComparison.CurrentCultureIgnoreCase))
 {
 try { var id = DatabaseService.GetOrCreateCategoryId(UserId, selectedCat!); if (id >0) cid = id; } catch { cid = null; }
 }
 string? source = string.IsNullOrWhiteSpace(selectedAcc) ? null : selectedAcc;
 DatabaseService.UpdateIncome(vm.Id, UserId, amount, desc, null, date, cid, source);
 // odœwie¿ widok elementu
 vm.AmountStr = amount.ToString("N2") + " z³";
 vm.DateDisplay = date.ToString("yyyy-MM-dd");
 vm.Description = desc;
 vm.CategoryName = string.IsNullOrWhiteSpace(selectedCat) ? "Przychód" : selectedCat!;
 vm.AccountName = source ?? vm.AccountName;
 break;
 }
 case TransactionKind.Transfer:
 {
 // Brak wsparcia DB na edycjê transferów – aktualizujemy tylko lokalne pola
 vm.AmountStr = amount.ToString("N2") + " z³"; // je¿eli UI pozwala edytowaæ kwotê
 vm.DateDisplay = date.ToString("yyyy-MM-dd");
 vm.Description = desc;
 // AccountName budowane z2 stron – zostawiamy bez zmian
 break;
 }
 }
 }
 catch { /* zignoruj jednostkowe b³êdy aby nie blokowaæ interfejsu */ }
 finally
 {
 vm.IsEditing = false;
 // Odswierz KPI i listy – wykonaj lokalnie bez pe³nego prze³adowania DB
 ApplyFilters();
 }
 }

 private void ClearPeriods()
 {
 _isToday = _isYesterday = _isThisWeek = _isThisMonth = _isPrevMonth = _isThisYear = false;
 }

 private bool MatchesSelectedAccounts(TransactionCardVm t, System.Collections.Generic.List<string> selectedAccounts, int totalAccounts)
{
 //1) Je¿eli nic nie zaznaczone – pokazujemy NIC
 if (selectedAccounts.Count ==0)
 return false;

 //2) Je¿eli zaznaczone s¹ wszystkie konta – brak filtra kont
 if (selectedAccounts.Count == totalAccounts)
 return true;

 string Normalize(string s) => (s ?? string.Empty).Trim();
 var set = selectedAccounts
 .Select(Normalize)
 .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

 // „gotówka” – traktuj woln¹ / od³o¿on¹ jako jedno logiczne konto
 bool MatchesCash(string name) =>
 name.IndexOf("gotówka", StringComparison.CurrentCultureIgnoreCase) >=0 &&
 (set.Contains("Wolna gotówka") || set.Contains("Od³o¿ona gotówka"));

 if (t.Kind == TransactionKind.Transfer)
 {
 var raw = Normalize(t.AccountName);
 var parts = raw.Split('?');
 var from = parts.Length >0 ? Normalize(parts[0]) : string.Empty;
 var to = parts.Length >1 ? Normalize(parts[1]) : string.Empty;

 return set.Contains(from) || set.Contains(to)
 || MatchesCash(from) || MatchesCash(to);
 }
 else
 {
 var n = Normalize(t.AccountName);
 return set.Contains(n) || MatchesCash(n);
 }
}
 private void ApplyFilters()
{
 if (AllTransactions.Count ==0) return;

 DateTime? from = null, to = null; var today = DateTime.Today;
 if (IsToday) { from = today; to = today; }
 else if (IsYesterday) { var y = today.AddDays(-1); from = y; to = y; }
 else if (IsThisWeek) { int diff = ((int)today.DayOfWeek +6) %7; var start = today.AddDays(-diff).Date; from = start; to = start.AddDays(6); }
 else if (IsThisMonth) { var start = new DateTime(today.Year, today.Month,1); from = start; to = start.AddMonths(1).AddDays(-1); }
 else if (IsPrevMonth) { var start = new DateTime(today.Year, today.Month,1).AddMonths(-1); from = start; to = start.AddMonths(1).AddDays(-1); }
 else if (IsThisYear) { from = new DateTime(today.Year,1,1); to = new DateTime(today.Year,12,31); }

 // Custom override
 if (DateFrom is DateTime df && DateTo is DateTime dt)
 {
 from = df.Date; to = dt.Date;
 }

 var selectedAccounts = Accounts.Where(a => a.IsSelected).Select(a => a.Name).ToList();
 var selectedCategories = Categories.Where(c => c.IsSelected).Select(c => c.Name).ToList();
 int totalAccounts = Accounts.Count;
 int totalCategories = Categories.Count;

 var q = (SearchQuery ?? string.Empty).Trim();

 bool PassCommonFilters(TransactionCardVm t)
 {
 bool dateOk = true; if (from.HasValue && to.HasValue && DateTime.TryParse(t.DateDisplay, out var d)) dateOk = d.Date >= from.Value.Date && d.Date <= to.Value.Date;

 // ===== TYP TRANSAKCJI =====
 bool typeOk;
 bool allTypesSelected = ShowExpenses && ShowIncomes && ShowTransfers;
 bool noTypesSelected = !ShowExpenses && !ShowIncomes && !ShowTransfers;
 if (noTypesSelected)
 {
 typeOk = false;
 }
 else if (allTypesSelected)
 {
 typeOk = true;
 }
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

 // ===== KONTA =====
 bool accOk = MatchesSelectedAccounts(t, selectedAccounts, totalAccounts);

 // ===== KATEGORIE =====
 bool catOk;
 if (totalCategories ==0)
 {
 catOk = true;
 }
 else if (selectedCategories.Count ==0)
 {
 catOk = false;
 }
 else if (selectedCategories.Count == totalCategories)
 {
 catOk = true;
 }
 else
 {
 catOk = selectedCategories.Any(x => string.Equals(t.CategoryName, x, StringComparison.CurrentCultureIgnoreCase));
 }

 // ===== WYSZUKIWARKA =====
 bool textOk = string.IsNullOrEmpty(q)
 || (t.CategoryName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >=0)
 || (t.Description?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >=0)
 || (t.AccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >=0);

 return dateOk && typeOk && accOk && catOk && textOk;
 }

 // Left: non-planned/future
 var left = AllTransactions.Where(t => PassCommonFilters(t) && !(t.IsPlanned || t.IsFuture)).ToList();
 TransactionsList.Clear(); foreach (var i in left) TransactionsList.Add(i);

 // Right: planned/future with the same filters applied
 var right = AllTransactions.Where(t => PassCommonFilters(t) && (t.IsPlanned || t.IsFuture)).OrderBy(t => t.DateDisplay).ToList();
 PlannedTransactionsList.Clear(); foreach (var r in right) PlannedTransactionsList.Add(r);

 // KPI from left
 TotalExpenses = left.Where(t => t.Kind == TransactionKind.Expense).Select(x => ParseAmountInternal(x.AmountStr)).Sum();
 TotalIncomes = left.Where(t => t.Kind == TransactionKind.Income).Select(x => ParseAmountInternal(x.AmountStr)).Sum();
 }

 public void SetPeriod(DateRangeMode mode, DateTime start, DateTime end)
 {
 // Reset preset flags
 ClearPeriods();
 // Custom vs preset
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
 }

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

 public class TransactionCardVm : INotifyPropertyChanged {
 public int Id { get; set; }
 private string _categoryName = ""; public string CategoryName { get => _categoryName; set { _categoryName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryName))); } }
 private string _description = ""; public string Description { get => _description; set { _description = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description))); } }
 private string _dateDisplay = ""; public string DateDisplay { get => _dateDisplay; set { _dateDisplay = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateDisplay))); } }
 private string _amountStr = ""; public string AmountStr { get => _amountStr; set { _amountStr = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmountStr))); } }
 public bool IsPlanned { get; set; }
 public bool IsFuture { get; set; }
 public string CategoryIcon { get; set; } = "";
 private string _accountName = ""; public string AccountName { get => _accountName; set { _accountName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountName))); } }
 public TransactionKind Kind { get; set; } = TransactionKind.Expense;
 private bool _isEditing; public bool IsEditing { get=>_isEditing; set { _isEditing=value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsEditing))); } }
 public string EditAmountText { get; set; } = "";
 public string EditDescription { get; set; } = "";
 public DateTime EditDate { get; set; } = DateTime.Today;
 public string SelectedCategory { get; set; } = "";
 public string SelectedAccount { get; set; } = "";

 // Potwierdzenie usuwania
 private bool _isDeleteConfirmationVisible; public bool IsDeleteConfirmationVisible { get => _isDeleteConfirmationVisible; set { _isDeleteConfirmationVisible = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDeleteConfirmationVisible))); } }
 public ICommand ShowDeleteConfirmationCommand { get; }
 public ICommand HideDeleteConfirmationCommand { get; }

 public TransactionCardVm()
 {
     ShowDeleteConfirmationCommand = new DelegateCommand(_ => IsDeleteConfirmationVisible = true);
     HideDeleteConfirmationCommand = new DelegateCommand(_ => IsDeleteConfirmationVisible = false);
 }

 public event PropertyChangedEventHandler? PropertyChanged;
 }
}
