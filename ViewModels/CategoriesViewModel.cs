using System;using System.Collections.ObjectModel;using System.ComponentModel;using System.Runtime.CompilerServices;using System.Linq;using Finly.Services;using System.Data;using System.Windows.Input;using System.Windows.Media;using System.Collections.Generic;

namespace Finly.ViewModels
{
 public class CategoriesViewModel : INotifyPropertyChanged
 {
 public event PropertyChangedEventHandler? PropertyChanged; private void OnPropertyChanged([CallerMemberName] string? n=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(n));

 public int UserId { get; private set; }

 // List of category names for left panel (existing UI can bind ItemsSource)
 public ObservableCollection<CategoryItem> Categories { get; } = new();

 private CategoryItem? _selectedCategory; public CategoryItem? SelectedCategory { get => _selectedCategory; set { if (_selectedCategory!=value){ _selectedCategory=value; OnPropertyChanged(); RefreshSelectedCategoryData(); UpdateCategoryComparison(); } } }

 // Stats properties (selected category)
 private decimal _selectedCategoryTotalAmount; public decimal SelectedCategoryTotalAmount { get=>_selectedCategoryTotalAmount; private set{_selectedCategoryTotalAmount=value; OnPropertyChanged();} }
 private decimal _selectedCategoryAverageMonthlyAmount; public decimal SelectedCategoryAverageMonthlyAmount { get=>_selectedCategoryAverageMonthlyAmount; private set{_selectedCategoryAverageMonthlyAmount=value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedCategoryAverageMonthly)); } }
 private int _selectedCategoryTransactionsCount; public int SelectedCategoryTransactionsCount { get=>_selectedCategoryTransactionsCount; private set{_selectedCategoryTransactionsCount=value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedCategoryTransactionCount)); } }

 // Aliases for XAML naming
 public decimal SelectedCategoryAverageMonthly => SelectedCategoryAverageMonthlyAmount;
 public int SelectedCategoryTransactionCount => SelectedCategoryTransactionsCount;

 // Recent transactions
 public ObservableCollection<CategoryTransactionRow> SelectedCategoryRecentTransactions { get; } = new();

 // Settings properties
 private string? _selectedCategoryDescription; public string? SelectedCategoryDescription { get=>_selectedCategoryDescription; set{ _selectedCategoryDescription=value; OnPropertyChanged(); } }
 private string? _selectedCategoryColor; public string? SelectedCategoryColor { get=>_selectedCategoryColor; set{ _selectedCategoryColor=value; OnPropertyChanged(); } }

 // Period range
 private DateTime _periodStart=DateTime.Today.AddMonths(-1); public DateTime PeriodStart { get=>_periodStart; set{ _periodStart=value; OnPropertyChanged(); RefreshSelectedCategoryData(); UpdateCategoryStats(); UpdateCategoryComparison(); } }
 private DateTime _periodEnd=DateTime.Today; public DateTime PeriodEnd { get=>_periodEnd; set{ _periodEnd=value; OnPropertyChanged(); RefreshSelectedCategoryData(); UpdateCategoryStats(); UpdateCategoryComparison(); } }

 // Command to save category settings
 public ICommand SaveCategorySettingsCommand { get; }

 // Overall usage structure
 public ObservableCollection<CategoryShareItem> CategoryShares { get; } = new();
 public ObservableCollection<CategoryShareItem> TopCategories { get; } = new();
 public ObservableCollection<CategoryShareItem> BottomCategories { get; } = new();

 private int _activeCategoriesCount; public int ActiveCategoriesCount { get=>_activeCategoriesCount; private set{ _activeCategoriesCount=value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveCategoriesSummary)); } }
 private int _totalCategoriesCount; public int TotalCategoriesCount { get=>_totalCategoriesCount; private set{ _totalCategoriesCount=value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveCategoriesSummary)); } }
 public string ActiveCategoriesSummary => $"Aktywne kategorie: {ActiveCategoriesCount}/{TotalCategoriesCount}";

 // Comparison with previous period for selected category
 private decimal _currentPeriodAmount; public decimal CurrentPeriodAmount { get=>_currentPeriodAmount; private set{ _currentPeriodAmount=value; OnPropertyChanged(); OnPropertyChanged(nameof(AmountDifference)); OnPropertyChanged(nameof(PercentageDifference)); } }
 private decimal _previousPeriodAmount; public decimal PreviousPeriodAmount { get=>_previousPeriodAmount; private set{ _previousPeriodAmount=value; OnPropertyChanged(); OnPropertyChanged(nameof(AmountDifference)); OnPropertyChanged(nameof(PercentageDifference)); } }
 public decimal AmountDifference => CurrentPeriodAmount - PreviousPeriodAmount;
 public double PercentageDifference
 {
 get
 {
 if (PreviousPeriodAmount ==0) return 0; // avoid div by zero; define as 0 when previous is 0
 return (double)((CurrentPeriodAmount - PreviousPeriodAmount) / PreviousPeriodAmount) *100.0;
 }
 }

 public CategoriesViewModel() { SaveCategorySettingsCommand = new RelayCommand(_=>SaveCategorySettings(), _=>SelectedCategory!=null); Categories.CollectionChanged += (_, __) => UpdateCategoryStats(); }

 public void Initialize(int userId){ UserId = userId<=0 ? UserService.GetCurrentUserId() : userId; LoadCategories(); }

 public void SetPeriod(DateTime start, DateTime end){ PeriodStart=start; PeriodEnd=end; }

 private void LoadCategories(){ Categories.Clear(); try { foreach(var name in DatabaseService.GetCategoriesByUser(UserId) ?? new System.Collections.Generic.List<string>()) { string color = GetStoredColor(name); Categories.Add(new CategoryItem { Name=name, ColorHex=color }); } } catch { } UpdateCategoryStats(); UpdateCategoryComparison(); }

 private string GetStoredColor(string categoryName){ try { var id = DatabaseService.GetCategoryIdByName(UserId, categoryName); if(id.HasValue){ using var con = DatabaseService.GetConnection(); using var cmd = con.CreateCommand(); cmd.CommandText="SELECT Color FROM Categories WHERE Id=@id LIMIT1;"; cmd.Parameters.AddWithValue("@id", id.Value); var obj = cmd.ExecuteScalar(); if(obj!=null && obj!=DBNull.Value) return obj.ToString()??string.Empty; } } catch { } return string.Empty; }

 private void RefreshSelectedCategoryData(){ SelectedCategoryRecentTransactions.Clear(); SelectedCategoryTotalAmount=0m; SelectedCategoryAverageMonthlyAmount=0m; SelectedCategoryTransactionsCount=0; if(SelectedCategory==null) return; // Load description & color
 LoadSelectedCategorySettings();
 int? catId = null; try { catId = DatabaseService.GetCategoryIdByName(UserId, SelectedCategory.Name); } catch { }
 if(!catId.HasValue){ return; }
 DateTime from = PeriodStart; DateTime to = PeriodEnd; if(from>to) (from,to)=(to,from);
 // Load expenses for period
 DataTable dt = null; try { dt = DatabaseService.GetExpenses(UserId, from, to, catId, null, null); } catch { }
 if(dt!=null){ decimal sum=0m; int count=0; foreach(DataRow r in dt.Rows){ try { sum += Math.Abs(Convert.ToDecimal(r[3])); count++; } catch { } } SelectedCategoryTotalAmount=sum; SelectedCategoryTransactionsCount=count; // monthly average
 double monthsSpan = Math.Max(1.0,(to-from).TotalDays/30.0); SelectedCategoryAverageMonthlyAmount = monthsSpan<=0 ?0 : sum/(decimal)monthsSpan; }
 // Recent transactions (limit5)
 try { var recent = DatabaseService.GetLastTransactionsForCategory(UserId, catId.Value,5); foreach(var t in recent){ SelectedCategoryRecentTransactions.Add(new CategoryTransactionRow { Date=t.Date, Amount=t.Amount, Description=t.Description }); } } catch { }
 }

 private void LoadSelectedCategorySettings(){ if(SelectedCategory==null){ SelectedCategoryDescription=null; SelectedCategoryColor=null; return;} try { var id = DatabaseService.GetCategoryIdByName(UserId, SelectedCategory.Name); if(id.HasValue){ using var con = DatabaseService.GetConnection(); using var cmd = con.CreateCommand(); cmd.CommandText="SELECT Description, Color FROM Categories WHERE Id=@id LIMIT1;"; cmd.Parameters.AddWithValue("@id", id.Value); using var reader = cmd.ExecuteReader(); if(reader.Read()){ SelectedCategoryDescription = reader.IsDBNull(0)? null : reader.GetString(0); SelectedCategoryColor = reader.IsDBNull(1)? null : reader.GetString(1); SelectedCategoryColor ??= SelectedCategory.ColorHex; } } } catch { }
 }

 private void SaveCategorySettings(){ if(SelectedCategory==null) return; try { var id = DatabaseService.GetCategoryIdByName(UserId, SelectedCategory.Name); if(!id.HasValue) return; using var con = DatabaseService.GetConnection(); using var cmd = con.CreateCommand(); cmd.CommandText="UPDATE Categories SET Description=@d, Color=@c WHERE Id=@id;"; cmd.Parameters.AddWithValue("@d", (object?)SelectedCategoryDescription ?? DBNull.Value); cmd.Parameters.AddWithValue("@c", (object?)SelectedCategoryColor ?? DBNull.Value); cmd.Parameters.AddWithValue("@id", id.Value); cmd.ExecuteNonQuery(); // Refresh color in left list
 SelectedCategory.ColorHex = SelectedCategoryColor ?? string.Empty; OnPropertyChanged(nameof(SelectedCategory)); LoadCategories(); } catch { } }

 // Compute usage structure and top/bottom categories for selected period
 public void UpdateCategoryStats(){
 try {
 DateTime from = PeriodStart; DateTime to = PeriodEnd; if(from>to) (from,to)=(to,from);
 TotalCategoriesCount = Categories.Count;
 var list = new List<(string name, decimal amount, int count)>();
 foreach(var cat in Categories){
 int? catId=null; try { catId = DatabaseService.GetCategoryIdByName(UserId, cat.Name); } catch { }
 if(!catId.HasValue){ list.Add((cat.Name,0m,0)); continue; }
 DataTable dt=null; try { dt = DatabaseService.GetExpenses(UserId, from, to, catId, null, null); } catch { }
 decimal sum=0m; int cnt=0; if(dt!=null){ foreach(DataRow r in dt.Rows){ try { sum += Math.Abs(Convert.ToDecimal(r[3])); cnt++; } catch { } } }
 list.Add((cat.Name,sum,cnt));
 }
 int active = list.Count(x=>x.count>0);
 ActiveCategoriesCount = active;
 decimal totalAmount = list.Sum(x=>x.amount);
 // Fill CategoryShares
 CategoryShares.Clear();
 if(totalAmount>0){
 foreach(var it in list.OrderByDescending(x=>x.amount)){
 double pct = (double)(it.amount/totalAmount)*100.0;
 CategoryShares.Add(new CategoryShareItem{ Name = it.name, Amount = it.amount, Percent = pct});
 }
 }
 else{
 foreach(var it in list){ CategoryShares.Add(new CategoryShareItem{ Name=it.name, Amount=0m, Percent=0}); }
 }
 // Top3 and Bottom3 (amount>0)
 var positive = list.Where(x=>x.amount>0).ToList();
 TopCategories.Clear();
 foreach(var it in positive.OrderByDescending(x=>x.amount).Take(3)){
 double pct = totalAmount>0 ? (double)(it.amount/totalAmount)*100.0 :0.0;
 TopCategories.Add(new CategoryShareItem{ Name=it.name, Amount=it.amount, Percent=pct});
 }
 BottomCategories.Clear();
 foreach(var it in positive.OrderBy(x=>x.amount).Take(3)){
 double pct = totalAmount>0 ? (double)(it.amount/totalAmount)*100.0 :0.0;
 BottomCategories.Add(new CategoryShareItem{ Name=it.name, Amount=it.amount, Percent=pct});
 }
 OnPropertyChanged(nameof(CategoryShares));
 OnPropertyChanged(nameof(TopCategories));
 OnPropertyChanged(nameof(BottomCategories));
 }
 catch {
 // reset to defaults on error
 CategoryShares.Clear(); TopCategories.Clear(); BottomCategories.Clear();
 ActiveCategoriesCount =0; TotalCategoriesCount = Categories.Count;
 }
 }

 // Compute comparison of current vs previous period for selected category
 public void UpdateCategoryComparison()
 {
 try
 {
 if (SelectedCategory == null) { CurrentPeriodAmount =0; PreviousPeriodAmount =0; return; }
 int? catId = null; try { catId = DatabaseService.GetCategoryIdByName(UserId, SelectedCategory.Name); } catch { }
 if(!catId.HasValue) { CurrentPeriodAmount =0; PreviousPeriodAmount =0; return; }
 DateTime from = PeriodStart; DateTime to = PeriodEnd; if(from>to) (from,to)=(to,from);
 var span = to - from;
 DateTime prevTo = from; DateTime prevFrom = from - span;
 // Current
 decimal curr =0m; DataTable cdt=null; try { cdt = DatabaseService.GetExpenses(UserId, from, to, catId, null, null); } catch { }
 if(cdt!=null){ foreach(DataRow r in cdt.Rows){ try { curr += Math.Abs(Convert.ToDecimal(r[3])); } catch { } } }
 // Previous
 decimal prev =0m; DataTable pdt=null; try { pdt = DatabaseService.GetExpenses(UserId, prevFrom, prevTo, catId, null, null); } catch { }
 if(pdt!=null){ foreach(DataRow r in pdt.Rows){ try { prev += Math.Abs(Convert.ToDecimal(r[3])); } catch { } } }
 CurrentPeriodAmount = curr; PreviousPeriodAmount = prev;
 }
 catch
 {
 CurrentPeriodAmount =0; PreviousPeriodAmount =0;
 }
 }

 // Helper RelayCommand
 private class RelayCommand : ICommand { private readonly Action<object?> _exec; private readonly Func<object?,bool> _can; public RelayCommand(Action<object?> e, Func<object?,bool> c){ _exec=e; _can=c; } public bool CanExecute(object? p)=>_can(p); public void Execute(object? p)=>_exec(p); public event EventHandler? CanExecuteChanged; public void Raise()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty); }

 public class CategoryItem : INotifyPropertyChanged { private string _colorHex=string.Empty; public string Name { get; set; } = string.Empty; public string ColorHex { get=>_colorHex; set { _colorHex=value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(ColorHex))); } } public event PropertyChangedEventHandler? PropertyChanged; }
 public class CategoryTransactionRow { public DateTime Date { get; set; } public decimal Amount { get; set; } public string Description { get; set; } = string.Empty; }
 public class CategoryShareItem { public string Name { get; set; } = string.Empty; public double Percent { get; set; } public decimal Amount { get; set; } }
 }
}
