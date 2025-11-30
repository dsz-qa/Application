using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Finly.Services;
using System.Data;

namespace Finly.ViewModels
{
 public class TransactionsViewModel : INotifyPropertyChanged
 {
 public event PropertyChangedEventHandler? PropertyChanged;
 private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
 public int UserId { get; private set; }
 public ObservableCollection<TransactionCardVm> AllTransactions { get; } = new();
 public ObservableCollection<TransactionCardVm> FilteredTransactions { get; } = new();
 private decimal _totalExpenses; public decimal TotalExpenses { get => _totalExpenses; private set { _totalExpenses = value; OnPropertyChanged(); OnPropertyChanged(nameof(Balance)); } }
 private decimal _totalIncomes; public decimal TotalIncomes { get => _totalIncomes; private set { _totalIncomes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Balance)); } }
 public decimal Balance => TotalIncomes - TotalExpenses;
 private string? _searchQuery; public string? SearchQuery { get => _searchQuery; set { _searchQuery = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _isToday; public bool IsToday { get => _isToday; set { _isToday = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _isYesterday; public bool IsYesterday { get => _isYesterday; set { _isYesterday = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _isThisWeek; public bool IsThisWeek { get => _isThisWeek; set { _isThisWeek = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _isThisMonth; public bool IsThisMonth { get => _isThisMonth; set { _isThisMonth = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _isPrevMonth; public bool IsPrevMonth { get => _isPrevMonth; set { _isPrevMonth = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _isThisYear; public bool IsThisYear { get => _isThisYear; set { _isThisYear = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _showExpenses = true; public bool ShowExpenses { get => _showExpenses; set { _showExpenses = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _showIncomes = true; public bool ShowIncomes { get => _showIncomes; set { _showIncomes = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _showTransfers = true; public bool ShowTransfers { get => _showTransfers; set { _showTransfers = value; OnPropertyChanged(); ApplyFilters(); } }
 public ObservableCollection<string> Categories { get; } = new();
 private object? _selectedCategory; public object? SelectedCategory { get => _selectedCategory; set { _selectedCategory = value; OnPropertyChanged(); ApplyFilters(); } }
 public ObservableCollection<string> Accounts { get; } = new();
 private object? _selectedAccount; public object? SelectedAccount { get => _selectedAccount; set { _selectedAccount = value; OnPropertyChanged(); ApplyFilters(); } }
 private bool _showScheduled; public bool ShowScheduled { get => _showScheduled; set { _showScheduled = value; OnPropertyChanged(); ApplyFilters(); } }
 public object? DateFrom { get; set; }
 public object? DateTo { get; set; }
 public void Initialize(int userId) { UserId = userId; LoadLookupData(); LoadFromDatabase(); DatabaseService.DataChanged += (_, __) => LoadFromDatabase(); }
 private void LoadLookupData() { Categories.Clear(); try { foreach (var c in DatabaseService.GetCategoriesByUser(UserId) ?? new System.Collections.Generic.List<string>()) Categories.Add(c); } catch { } Accounts.Clear(); try { foreach (var a in DatabaseService.GetAccounts(UserId) ?? new System.Collections.Generic.List<Finly.Models.BankAccountModel>()) Accounts.Add(a.AccountName); } catch { } }
 public void LoadFromDatabase() { if (UserId <=0) return; AllTransactions.Clear(); DataTable expDt = null; try { expDt = DatabaseService.GetExpenses(UserId); } catch { } if (expDt != null) { foreach (DataRow r in expDt.Rows) { int id = Convert.ToInt32(r["Id"]); double amt = Convert.ToDouble(r["Amount"]); var dateObj = r["Date"]; DateTime date = dateObj is DateTime d ? d : DateTime.TryParse(dateObj?.ToString(), out var dp) ? dp : DateTime.MinValue; string desc = r["Description"]?.ToString() ?? string.Empty; string catName = r["CategoryName"]?.ToString() ?? string.Empty; bool planned = r["IsPlanned"] != DBNull.Value && Convert.ToInt32(r["IsPlanned"]) ==1; int? accountId = r["AccountId"] == DBNull.Value ? null : (int?)Convert.ToInt32(r["AccountId"]); string accountName = string.Empty; if (accountId.HasValue) { try { var acc = DatabaseService.GetAccounts(UserId).FirstOrDefault(a => a.Id == accountId.Value); accountName = acc?.AccountName ?? string.Empty; } catch { } } AllTransactions.Add(new TransactionCardVm { Id = id, Kind = TransactionKind.Expense, CategoryName = catName, Description = desc, DateDisplay = date.ToString("yyyy-MM-dd"), AmountStr = amt.ToString("N2") + " z³", IsPlanned = planned, CategoryIcon = "?", AccountName = accountName }); } } RefreshData(); }
 public void RefreshData() => ApplyFilters();
 private decimal ParseAmountInternal(string amountStr)
{
 if (string.IsNullOrWhiteSpace(amountStr)) return 0m;
 var txt = new string(amountStr.Where(ch => char.IsDigit(ch) || ch == ',' || ch == '.' || ch == '-').ToArray());
 if (decimal.TryParse(txt, out var v)) return Math.Abs(v);
 return 0m;
 }
 public void DeleteTransaction(TransactionCardVm vm) { if (vm.Kind == TransactionKind.Expense) { try { DatabaseService.DeleteExpense(vm.Id); } catch { } } LoadFromDatabase(); }
 public void AddExpense(decimal amount, DateTime date, string description, string? categoryName, bool planned = false) { int catId =0; if (!string.IsNullOrWhiteSpace(categoryName)) { try { catId = DatabaseService.GetOrCreateCategoryId(UserId, categoryName); } catch { } } var exp = new Finly.Models.Expense { UserId = UserId, Amount = (double)amount, Date = date, Description = description, CategoryId = catId, IsPlanned = planned }; try { DatabaseService.InsertExpense(exp); } catch { } LoadFromDatabase(); }
 public void UpdateTransaction(TransactionCardVm vm, decimal? newAmount = null, string? newDescription = null, bool? planned = null) { if (vm.Kind == TransactionKind.Expense) { var exp = DatabaseService.GetExpenseById(vm.Id); if (exp != null) { if (newAmount.HasValue) exp.Amount = (double)newAmount.Value; if (newDescription != null) exp.Description = newDescription; if (planned.HasValue) exp.IsPlanned = planned.Value; try { DatabaseService.UpdateExpense(exp); } catch { } } } LoadFromDatabase(); }
 private void ApplyFilters() { DateTime? from = null, to = null; var today = DateTime.Today; if (IsToday) { from = today; to = today; } else if (IsYesterday) { var y = today.AddDays(-1); from = y; to = y; } else if (IsThisWeek) { int diff = ((int)today.DayOfWeek +6) %7; var start = today.AddDays(-diff).Date; from = start; to = start.AddDays(6); } else if (IsThisMonth) { var start = new DateTime(today.Year, today.Month,1); from = start; to = start.AddMonths(1).AddDays(-1); } else if (IsPrevMonth) { var start = new DateTime(today.Year, today.Month,1).AddMonths(-1); from = start; to = start.AddMonths(1).AddDays(-1); } else if (IsThisYear) { from = new DateTime(today.Year,1,1); to = new DateTime(today.Year,12,31); } var q = (SearchQuery ?? string.Empty).Trim(); var cat = SelectedCategory?.ToString(); var acc = SelectedAccount?.ToString(); var result = AllTransactions.Where(t => { bool textOk = string.IsNullOrEmpty(q) || (t.CategoryName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >=0) || (t.Description?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >=0) || (t.AccountName?.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >=0); bool dateOk = true; if (from.HasValue && to.HasValue && DateTime.TryParse(t.DateDisplay, out var d)) dateOk = d.Date >= from.Value.Date && d.Date <= to.Value.Date; bool typeOk = (t.Kind == TransactionKind.Expense && ShowExpenses) || (t.Kind == TransactionKind.Income && ShowIncomes) || (t.Kind == TransactionKind.Transfer && ShowTransfers); bool catOk = string.IsNullOrEmpty(cat) || string.Equals(t.CategoryName, cat, StringComparison.CurrentCultureIgnoreCase); bool accOk = string.IsNullOrEmpty(acc) || string.Equals(t.AccountName, acc, StringComparison.CurrentCultureIgnoreCase); bool plannedOk = ShowScheduled || !t.IsPlanned; return textOk && dateOk && typeOk && catOk && accOk && plannedOk; }).ToList(); FilteredTransactions.Clear(); foreach (var item in result) FilteredTransactions.Add(item); TotalExpenses = result.Where(t => t.Kind == TransactionKind.Expense).Select(x => ParseAmountInternal(x.AmountStr)).Sum(); TotalIncomes = result.Where(t => t.Kind == TransactionKind.Income).Select(x => ParseAmountInternal(x.AmountStr)).Sum(); }
 }
 public enum TransactionKind { Expense, Income, Transfer }
 public class TransactionCardVm { public int Id { get; set; } public string CategoryName { get; set; } = ""; public string Description { get; set; } = ""; public string DateDisplay { get; set; } = ""; public string AmountStr { get; set; } = ""; public bool IsPlanned { get; set; } public string CategoryIcon { get; set; } = "??"; public string AccountName { get; set; } = ""; public TransactionKind Kind { get; set; } = TransactionKind.Expense; }
}
