using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Finly.Helpers;
using Finly.Models;
using Finly.Services;
using System.Collections.Generic;
using System.Data;

namespace Finly.ViewModels
{
 public class ReportsViewModel : INotifyPropertyChanged
 {
 public event PropertyChangedEventHandler? PropertyChanged;
 protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

 public ReportsViewModel()
 {
 _fromDate = DateTime.Today;
 _toDate = DateTime.Today;

 Accounts = new ObservableCollection<string> { "Wszystkie konta", "Konto osobiste", "Karta kredytowa" };
 Categories = new ObservableCollection<string> { "Wszystkie kategorie", "Jedzenie", "Rozrywka", "Subskrypcje" };
 Envelopes = new ObservableCollection<string> { "Wszystkie koperty", "Mieszkanie", "Zakupy" };
 Tags = new ObservableCollection<string> { "Brak", "Podró¿e", "Praca" };
 Currencies = new ObservableCollection<string> { "PLN", "EUR", "USD" };
 Templates = new ObservableCollection<string> { "Domyœlny", "Miesiêczny przegl¹d" };

 Insights = new ObservableCollection<string>();
 KPIList = new ObservableCollection<KeyValuePair<string, string>>();

 SelectedSource = SourceType.All;

 RefreshCommand = new RelayCommand(_ => Refresh());
 SavePresetCommand = new RelayCommand(_ => SavePreset());
 ExportCsvCommand = new RelayCommand(_ => ExportCsv());
 ExportExcelCommand = new RelayCommand(_ => ExportExcel());
 ExportPdfCommand = new RelayCommand(_ => ExportPdf());
 BackCommand = new RelayCommand(_ => BackToSummary());

 BankAccounts = new ObservableCollection<BankAccountModel>();
 Details = new ObservableCollection<CategoryAmount>();
 FilteredTransactions = new ObservableCollection<TransactionDto>();
 _transactionsSnapshot = new List<TransactionDto>();

 LoadAccountsAndEnvelopes();
 }

 private void LoadAccountsAndEnvelopes()
 {
 try
 {
 var uid = UserService.GetCurrentUserId();
 BankAccounts = new ObservableCollection<BankAccountModel>();
 BankAccounts.Add(new BankAccountModel { Id =0, UserId = uid, AccountName = "Wszystkie konta bankowe", BankName = "" });
 foreach (var a in DatabaseService.GetAccounts(uid)) BankAccounts.Add(a);

 try
 {
 var envs = DatabaseService.GetEnvelopesNames(uid) ?? new List<string>();
 Envelopes.Clear();
 Envelopes.Add("Wszystkie koperty");
 foreach (var e in envs) Envelopes.Add(e);
 }
 catch { }
 }
 catch { }
 }

 public enum SourceType { All =0, FreeCash =1, SavedCash =2, BankAccounts =3, Envelopes =4 }

 private DateTime _fromDate;
 private DateTime _toDate;
 public DateTime FromDate { get => _fromDate; set { if (_fromDate != value) { _fromDate = value; Raise(nameof(FromDate)); } } }
 public DateTime ToDate { get => _toDate; set { if (_toDate != value) { _toDate = value; Raise(nameof(ToDate)); } } }

 public ObservableCollection<string> Accounts { get; }
 public ObservableCollection<string> Categories { get; }
 public ObservableCollection<string> Envelopes { get; }
 public ObservableCollection<string> Tags { get; }
 public ObservableCollection<string> Currencies { get; }
 public ObservableCollection<string> Templates { get; }

 private string _selectedAccount = "Wszystkie konta";
 public string SelectedAccount { get => _selectedAccount; set { if (_selectedAccount != value) { _selectedAccount = value; Raise(nameof(SelectedAccount)); } } }

 private string _selectedCategory = "Wszystkie kategorie";
 public string SelectedCategory { get => _selectedCategory; set { if (_selectedCategory != value) { _selectedCategory = value; Raise(nameof(SelectedCategory)); } } }

 private string _selectedTemplate = "Domyœlny";
 public string SelectedTemplate { get => _selectedTemplate; set { if (_selectedTemplate != value) { _selectedTemplate = value; Raise(nameof(SelectedTemplate)); } } }

 private SourceType _selectedSource;
 public SourceType SelectedSource { get => _selectedSource; set { if (_selectedSource != value) { _selectedSource = value; Raise(nameof(SelectedSource)); Raise(nameof(ShowBankSelector)); Raise(nameof(ShowEnvelopeSelector)); } } }
 public bool ShowBankSelector => SelectedSource == SourceType.BankAccounts;
 public bool ShowEnvelopeSelector => SelectedSource == SourceType.Envelopes;

 private ObservableCollection<BankAccountModel> _bankAccounts = new();
 public ObservableCollection<BankAccountModel> BankAccounts { get => _bankAccounts; set { _bankAccounts = value; Raise(nameof(BankAccounts)); } }
 private BankAccountModel? _selectedBankAccount;
 public BankAccountModel? SelectedBankAccount { get => _selectedBankAccount; set { _selectedBankAccount = value; Raise(nameof(SelectedBankAccount)); } }

 private string _selectedEnvelope = "Wszystkie koperty";
 public string SelectedEnvelope { get => _selectedEnvelope; set { _selectedEnvelope = value; Raise(nameof(SelectedEnvelope)); } }

 public ObservableCollection<string> Insights { get; }
 public ObservableCollection<KeyValuePair<string, string>> KPIList { get; }

 public ICommand RefreshCommand { get; }
 public ICommand SavePresetCommand { get; }
 public ICommand ExportCsvCommand { get; }
 public ICommand ExportExcelCommand { get; }
 public ICommand ExportPdfCommand { get; }
 public ICommand BackCommand { get; }

 public void ResetFilters()
 {
 SelectedAccount = Accounts.Count >0 ? Accounts[0] : string.Empty;
 SelectedCategory = Categories.Count >0 ? Categories[0] : string.Empty;
 SelectedTemplate = Templates.Count >0 ? Templates[0] : string.Empty;
 SelectedSource = SourceType.All;
 SelectedBankAccount = BankAccounts.FirstOrDefault();
 SelectedEnvelope = Envelopes.Count >0 ? Envelopes[0] : "Wszystkie koperty";
 }

 // Data models
 public class CategoryAmount { public string Name = ""; public decimal Amount; public double SharePercent; }
 public class TransactionDto { public int Id; public DateTime Date; public decimal Amount; public string Description = ""; public string Category = ""; }

 public ObservableCollection<CategoryAmount> Details { get; private set; } = new();
 public ObservableCollection<TransactionDto> FilteredTransactions { get; private set; } = new();
 private List<TransactionDto> _transactionsSnapshot;

 private Dictionary<string, decimal> _chartTotals = new();
 public Dictionary<string, decimal> ChartTotals { get => _chartTotals; private set { _chartTotals = value; Raise(nameof(ChartTotals)); } }

 private decimal _chartTotalAll =0m;
 public decimal ChartTotalAll { get => _chartTotalAll; private set { _chartTotalAll = value; Raise(nameof(ChartTotalAll)); } }

 private bool _isDrilldown = false;
 public bool IsDrilldownActive { get => _isDrilldown; private set { _isDrilldown = value; Raise(nameof(IsDrilldownActive)); Raise(nameof(IsSummaryActive)); } }
 public bool IsSummaryActive => !IsDrilldownActive;

 // Right panel totals
 private decimal _expensesTotal =0m;
 public decimal ExpensesTotal { get => _expensesTotal; private set { if (_expensesTotal != value) { _expensesTotal = value; Raise(nameof(ExpensesTotal)); Raise(nameof(ExpensesTotalStr)); } } }
 public string ExpensesTotalStr => ExpensesTotal.ToString("N2", CultureInfo.CurrentCulture) + " z³";

 private decimal _incomesTotal =0m;
 public decimal IncomesTotal { get => _incomesTotal; private set { if (_incomesTotal != value) { _incomesTotal = value; Raise(nameof(IncomesTotal)); Raise(nameof(IncomesTotalStr)); } } }
 public string IncomesTotalStr => IncomesTotal.ToString("N2", CultureInfo.CurrentCulture) + " z³";

 private decimal _balanceTotal =0m;
 public decimal BalanceTotal { get => _balanceTotal; private set { if (_balanceTotal != value) { _balanceTotal = value; Raise(nameof(BalanceTotal)); Raise(nameof(BalanceTotalStr)); } } }
 public string BalanceTotalStr => BalanceTotal.ToString("N2", CultureInfo.CurrentCulture) + " z³";

 private void PopulateFromDataTable(DataTable dt)
 {
 _transactionsSnapshot.Clear();
 FilteredTransactions.Clear();

 foreach (DataRow r in dt.Rows)
 {
 var t = new TransactionDto
 {
 Id = dt.Columns.Contains("Id") && r["Id"] != DBNull.Value ? Convert.ToInt32(r["Id"]) :0,
 Date = dt.Columns.Contains("Date") && r["Date"] != DBNull.Value ? DateTime.Parse(r["Date"].ToString()!) : DateTime.MinValue,
 Amount = dt.Columns.Contains("Amount") && r["Amount"] != DBNull.Value ? Convert.ToDecimal(r["Amount"]) :0m,
 Description = dt.Columns.Contains("Description") && r["Description"] != DBNull.Value ? r["Description"].ToString()! : "",
 Category = dt.Columns.Contains("CategoryName") && r["CategoryName"] != DBNull.Value ? r["CategoryName"].ToString()! : "(brak)"
 };

 _transactionsSnapshot.Add(t);
 FilteredTransactions.Add(t);
 }

 _detailsClearAndGroup(dt);
 }

 private void _detailsClearAndGroup(DataTable dt)
 {
 Details.Clear();
 var groups = dt.AsEnumerable()
 .GroupBy(r => r.Field<string>("CategoryName") ?? "(brak)")
 .Select(g => new { Name = g.Key, Total = g.Sum(r => Convert.ToDecimal(r.Field<object>("Amount"))) })
 .OrderByDescending(x => x.Total)
 .ToList();

 var total = groups.Sum(x => x.Total);
 ChartTotals = groups.ToDictionary(x => x.Name, x => x.Total);
 ChartTotalAll = total;

 foreach (var g in groups) Details.Add(new CategoryAmount { Name = g.Name, Amount = g.Total, SharePercent = total >0 ? (double)(g.Total / total *100m) :0.0 });
 }

 public void ShowDrilldown(string category)
 {
 if (string.IsNullOrWhiteSpace(category)) return;
 IsDrilldownActive = true;
 var list = _transactionsSnapshot.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase)).OrderByDescending(t => t.Date).ToList();
 FilteredTransactions.Clear();
 foreach (var t in list) FilteredTransactions.Add(t);
 }

 public void BackToSummary()
 {
 IsDrilldownActive = false;
 FilteredTransactions.Clear();
 foreach (var t in _transactionsSnapshot.OrderByDescending(t => t.Date)) FilteredTransactions.Add(t);
 }

 private void Refresh()
 {
 try
 {
 var uid = UserService.GetCurrentUserId();
 if (uid <=0)
 {
 MessageBox.Show("Brak zalogowanego u¿ytkownika.", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Warning);
 return;
 }

 int? accountId = null;
 if (SelectedSource == SourceType.BankAccounts && SelectedBankAccount != null && SelectedBankAccount.Id >0) accountId = SelectedBankAccount.Id;

 DataTable dt = DatabaseService.GetExpenses(uid, FromDate, ToDate, null, null, accountId);

 IEnumerable<DataRow> rows = dt.AsEnumerable();
 if (SelectedSource == SourceType.FreeCash) rows = rows.Where(r => r.IsNull("AccountId"));
 else if (SelectedSource == SourceType.SavedCash) rows = rows.Where(r => r.IsNull("AccountId"));
 else if (SelectedSource == SourceType.Envelopes)
 {
 if (!string.IsNullOrWhiteSpace(SelectedEnvelope) && SelectedEnvelope != "Wszystkie koperty") rows = rows.Where(r => (r.Field<string>("Description") ?? "").IndexOf(SelectedEnvelope, StringComparison.OrdinalIgnoreCase) >=0);
 else rows = rows.Where(r => r.IsNull("AccountId"));
 }

 var filteredDt = rows.Any() ? rows.CopyToDataTable() : new DataTable();

 FilteredTransactions.Clear();
 Details.Clear();
 if (filteredDt.Rows.Count >0) PopulateFromDataTable(filteredDt);
 else { ChartTotals = new Dictionary<string, decimal>(); ChartTotalAll =0m; }

 // KPIs/Insights
 KPIList.Clear();
 KPIList.Add(new KeyValuePair<string, string>("Suma wydatków", ChartTotalAll.ToString("N2") + " z³"));

 Insights.Clear();
 Insights.Add($"Filtrowanie: {SelectedSource}");

 // compute totals
 decimal expenses =0m;
 if (filteredDt.Rows.Count >0 && filteredDt.Columns.Contains("Amount"))
 {
 foreach (DataRow r in filteredDt.Rows) if (r["Amount"] != DBNull.Value) expenses += Convert.ToDecimal(r["Amount"]);
 }
 ExpensesTotal = expenses;

 decimal incomes =0m;
 try { var inc = DatabaseService.GetIncomeBySourceSafe(uid, FromDate, ToDate) ?? new List<DatabaseService.CategoryAmountDto>(); incomes = inc.Sum(x => x.Amount); } catch { incomes =0m; }
 IncomesTotal = incomes;

 BalanceTotal = IncomesTotal - ExpensesTotal;

 IsDrilldownActive = false;

 Raise(nameof(Details));
 Raise(nameof(ChartTotals));
 Raise(nameof(ChartTotalAll));
 Raise(nameof(FilteredTransactions));
 }
 catch (Exception ex)
 {
 MessageBox.Show("B³¹d odczytu raportu: " + ex.Message, "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
 }
 }

 private void SavePreset() { MessageBox.Show($"Zapisano preset: {SelectedTemplate}", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information); }

 private void ExportCsv()
 {
 try
 {
 var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "report_export.csv");
 var sb = new StringBuilder();
 sb.AppendLine("Category,Amount,SharePercent");
 foreach (var d in Details) sb.AppendLine($"{d.Name},{d.Amount:N2},{d.SharePercent:N1}");
 File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
 MessageBox.Show($"Eksport CSV zapisano na pulpicie: {path}", "Eksport", MessageBoxButton.OK, MessageBoxImage.Information);
 }
 catch (Exception ex) { MessageBox.Show($"B³¹d eksportu CSV: {ex.Message}", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error); }
 }

 private void ExportExcel()
 {
 try
 {
 var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "report_export.xlsx");
 var sb = new StringBuilder();
 sb.AppendLine("Category\tAmount\tSharePercent");
 foreach (var d in Details) sb.AppendLine($"{d.Name}\t{d.Amount:N2}\t{d.SharePercent:N1}");
 File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
 MessageBox.Show($"Eksport Excel (TSV) zapisano na pulpicie: {path}", "Eksport", MessageBoxButton.OK, MessageBoxImage.Information);
 }
 catch (Exception ex) { MessageBox.Show($"B³¹d eksportu Excel: {ex.Message}", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error); }
 }

 private void ExportPdf() { MessageBox.Show("Eksport PDF nie zosta³ jeszcze zaimplementowany.", "Funkcja w przygotowaniu", MessageBoxButton.OK, MessageBoxImage.Information); }
 }

 static class DataTableExtensions
 {
 public static DataTable CopyToDataTableOrEmpty(this IEnumerable<DataRow> rows)
 {
 var list = rows.ToList();
 if (list.Count ==0) return new DataTable();
 return list.CopyToDataTable();
 }
 }
}
