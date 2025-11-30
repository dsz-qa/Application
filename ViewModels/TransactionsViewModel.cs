using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Finly.Services;
using System.Data;
using Finly.Models;

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

 private bool _showScheduled = true; public bool ShowScheduled { get => _showScheduled; set { _showScheduled = value; OnPropertyChanged(); /* nie filtrujemy PlannedTransactionsList, tylko UI ukrywa kolumnê */ } }

 public object? DateFrom { get; set; }
 public object? DateTo { get; set; }

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
 try { foreach (var c in DatabaseService.GetCategoriesByUser(UserId) ?? new System.Collections.Generic.List<string>()) Categories.Add(new CategoryFilterItem { Name = c }); } catch { }
 Categories.CollectionChanged += (s, e) =>
 {
 if (e.NewItems != null) foreach (CategoryFilterItem ci in e.NewItems) ci.PropertyChanged += (_, __) => ApplyFilters();
 if (e.OldItems != null) foreach (CategoryFilterItem ci in e.OldItems) ci.PropertyChanged -= (_, __) => ApplyFilters();
 ApplyFilters();
 };
 foreach (var ci in Categories) ci.PropertyChanged += (_, __) => ApplyFilters();

 Accounts.Clear();
 try { foreach (var a in DatabaseService.GetAccounts(UserId) ?? new System.Collections.Generic.List<Finly.Models.BankAccountModel>()) Accounts.Add(new AccountFilterItem { Name = a.AccountName }); } catch { }
 try { foreach (var env in DatabaseService.GetEnvelopesNames(UserId) ?? new System.Collections.Generic.List<string>()) Accounts.Add(new AccountFilterItem { Name = $"Koperta: {env}" }); } catch { }
 Accounts.Add(new AccountFilterItem { Name = "Wolna gotówka" });
 Accounts.Add(new AccountFilterItem { Name = "Od³o¿ona gotówka" });
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
 string accountName = string.Empty; if (accountId.HasValue) { try { var acc = DatabaseService.GetAccounts(UserId).FirstOrDefault(a => a.Id == accountId.Value); accountName = acc?.AccountName ?? string.Empty; } catch { } }
 if (string.IsNullOrWhiteSpace(accountName)) accountName = "Gotówka";
 AllTransactions.Add(new TransactionCardVm { Id = id, Kind = TransactionKind.Expense, CategoryName = catName, Description = desc, DateDisplay = date.ToString("yyyy-MM-dd"), AmountStr = amt.ToString("N2") + " z³", IsPlanned = planned, IsFuture = date.Date > DateTime.Today, CategoryIcon = "?", AccountName = accountName });
 }
 private void AddIncomeRow(DataRow r)
 {
 int id = Convert.ToInt32(r["Id"]); decimal amt = Convert.ToDecimal(r["Amount"]);
 DateTime date = ParseDate(r["Date"]); string desc = r["Description"]?.ToString() ?? string.Empty;
 string source = r["Source"]?.ToString() ?? string.Empty; string catName = r.Table.Columns.Contains("CategoryName") ? (r["CategoryName"]?.ToString() ?? "") : "";
 bool planned = r.Table.Columns.Contains("IsPlanned") && r["IsPlanned"] != DBNull.Value && Convert.ToInt32(r["IsPlanned"]) ==1;
 AllTransactions.Add(new TransactionCardVm { Id = id, Kind = TransactionKind.Income, CategoryName = string.IsNullOrEmpty(catName) ? "Przychód" : catName, Description = desc, DateDisplay = date.ToString("yyyy-MM-dd"), AmountStr = amt.ToString("N2") + " z³", IsPlanned = planned, IsFuture = date.Date > DateTime.Today, CategoryIcon = "+", AccountName = source });
 }
 private void AddTransferRow(DataRow r)
 {
 int id = Convert.ToInt32(r["Id"]); decimal amt = Convert.ToDecimal(r["Amount"]);
 DateTime date = ParseDate(r["Date"]); string desc = r["Description"]?.ToString() ?? "Transfer"; bool planned = r.Table.Columns.Contains("IsPlanned") && r["IsPlanned"] != DBNull.Value && Convert.ToInt32(r["IsPlanned"]) ==1;
 string fromKind = r.Table.Columns.Contains("FromKind") ? r["FromKind"]?.ToString() ?? "" : ""; string toKind = r.Table.Columns.Contains("ToKind") ? r["ToKind"]?.ToString() ?? "" : ""; string accountDisplay = fromKind + "?" + toKind;
 AllTransactions.Add(new TransactionCardVm { Id = id, Kind = TransactionKind.Transfer, CategoryName = "Transfer", Description = desc, DateDisplay = date.ToString("yyyy-MM-dd"), AmountStr = amt.ToString("N2") + " z³", IsPlanned = planned, IsFuture = date.Date > DateTime.Today, CategoryIcon = "?", AccountName = accountDisplay });
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

 private void ClearPeriods()
 {
 _isToday = _isYesterday = _isThisWeek = _isThisMonth = _isPrevMonth = _isThisYear = false;
 }

 private bool MatchesSelectedAccounts(TransactionCardVm t, System.Collections.Generic.List<string> selectedAccounts)
 {
 if (selectedAccounts.Count ==0) return true;
 string Normalize(string s) => (s ?? string.Empty).Trim();
 var set = selectedAccounts.Select(Normalize).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
 if (t.Kind == TransactionKind.Transfer)
 {
 var raw = Normalize(t.AccountName);
 var parts = raw.Split('?');
 var from = parts.Length >0 ? Normalize(parts[0]) : string.Empty;
 var to = parts.Length >1 ? Normalize(parts[1]) : string.Empty;
 return set.Contains(from) || set.Contains(to);
 }
 else
 {
 return set.Contains(Normalize(t.AccountName));
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
 from = df.Date;
 to = dt.Date;
 }

 var typeSelected = new[] { (TransactionKind.Expense, ShowExpenses), (TransactionKind.Income, ShowIncomes), (TransactionKind.Transfer, ShowTransfers) }
 .Where(t => t.Item2).Select(t => t.Item1).ToList();
 bool typeFilterActive = typeSelected.Count >0;

 var selectedAccounts = Accounts.Where(a => a.IsSelected).Select(a => a.Name).ToList();
 var selectedCategories = Categories.Where(c => c.IsSelected).Select(c => c.Name).ToList();

 var q = (SearchQuery ?? string.Empty).Trim();

 bool PassCommonFilters(TransactionCardVm t)
 {
 bool dateOk = true; if (from.HasValue && to.HasValue && DateTime.TryParse(t.DateDisplay, out var d)) dateOk = d.Date >= from.Value.Date && d.Date <= to.Value.Date;
 bool typeOk = !typeFilterActive || typeSelected.Contains(t.Kind);
 bool accOk = MatchesSelectedAccounts(t, selectedAccounts);
 bool catOk = selectedCategories.Count ==0 || selectedCategories.Any(x => string.Equals(t.CategoryName, x, StringComparison.CurrentCultureIgnoreCase));
 bool textOk = string.IsNullOrEmpty(q) || (t.CategoryName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >=0) || (t.Description?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >=0) || (t.AccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >=0);
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
 public class TransactionCardVm { public int Id { get; set; } public string CategoryName { get; set; } = ""; public string Description { get; set; } = ""; public string DateDisplay { get; set; } = ""; public string AmountStr { get; set; } = ""; public bool IsPlanned { get; set; } public bool IsFuture { get; set; } public string CategoryIcon { get; set; } = ""; public string AccountName { get; set; } = ""; public TransactionKind Kind { get; set; } = TransactionKind.Expense; }
 public sealed class AccountFilterItem : INotifyPropertyChanged { private bool _sel; public string Name { get; set; } = ""; public bool IsSelected { get => _sel; set { _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } } public event PropertyChangedEventHandler? PropertyChanged; }
 public sealed class CategoryFilterItem : INotifyPropertyChanged { private bool _sel; public string Name { get; set; } = ""; public bool IsSelected { get => _sel; set { _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } } public event PropertyChangedEventHandler? PropertyChanged; }
}
