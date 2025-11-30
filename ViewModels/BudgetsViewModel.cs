using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Finly.Models;
using Finly.Services;

namespace Finly.ViewModels
{
 internal class BudgetsViewModel : INotifyPropertyChanged
 {
 public event PropertyChangedEventHandler? PropertyChanged;
 private void OnPropertyChanged([CallerMemberName] string? n=null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

 internal int UserId { get; private set; }

 internal ObservableCollection<BudgetModel> Budgets { get; } = new();

 private DateTime _from = DateTime.Today, _to = DateTime.Today;
 internal DateTime From { get => _from; private set { _from = value; OnPropertyChanged(); } }
 internal DateTime To { get => _to; private set { _to = value; OnPropertyChanged(); } }

 private BudgetType _selectedBudgetType = BudgetType.Monthly;
 internal BudgetType SelectedBudgetType { get => _selectedBudgetType; set { _selectedBudgetType = value; OnPropertyChanged(); Reload(); } }

 private decimal _totalLimit; internal decimal TotalLimit { get => _totalLimit; private set { _totalLimit = value; OnPropertyChanged(); } }
 private decimal _totalSpent; internal decimal TotalSpent { get => _totalSpent; private set { _totalSpent = value; OnPropertyChanged(); } }
 private decimal _totalLeft; internal decimal TotalLeft { get => _totalLeft; private set { _totalLeft = value; OnPropertyChanged(); } }

 private BudgetPeriodKind _selectedPeriod = BudgetPeriodKind.Month;
 public BudgetPeriodKind SelectedPeriod { get => _selectedPeriod; set { _selectedPeriod = value; OnPropertyChanged(); UpdateCurrentPeriodName(); } }

 private string _currentPeriodName = string.Empty;
 public string CurrentPeriodName { get => _currentPeriodName; private set { _currentPeriodName = value; OnPropertyChanged(); } }

 internal void Initialize(int userId)
 {
 UserId = userId;
 // default period: this month
 var today = DateTime.Today; SetPeriod(DateRangeMode.Month, new DateTime(today.Year, today.Month,1), new DateTime(today.Year, today.Month,1).AddMonths(1).AddDays(-1));
 UpdateCurrentPeriodName();
 Reload();
 }

 internal void SetPeriod(DateRangeMode mode, DateTime start, DateTime end)
 {
 From = start.Date; To = end.Date;
 Reload();
 }

 internal void Reload()
 {
 // load all budgets then filter by SelectedBudgetType (if specific) and active range
 var all = BudgetService.LoadBudgets(UserId) ?? new System.Collections.Generic.List<BudgetModel>();
 Budgets.Clear();
 foreach (var b in all)
 {
 if (!b.Active) continue;
 if (b.Type != SelectedBudgetType) continue;
 Budgets.Add(b);
 }
 RecalculateTotals();
 }

 private void RecalculateTotals()
 {
 // TotalLimit = sum of amounts (with rollover considered)
 decimal limitSum =0m, spentSum =0m;
 foreach (var b in Budgets)
 {
 var effectiveAmount = b.Amount;
 if (b.Type == BudgetType.Rollover) effectiveAmount += b.LastRollover;
 limitSum += effectiveAmount;
 // spent from DB in current From..To
 try
 {
 System.Data.DataTable dt;
 if (b.CategoryId.HasValue) dt = DatabaseService.GetExpenses(UserId, From, To, b.CategoryId.Value);
 else dt = DatabaseService.GetExpenses(UserId, From, To);
 if (dt != null)
 {
 foreach (System.Data.DataRow r in dt.Rows)
 {
 try { spentSum += Math.Abs(Convert.ToDecimal(r["Amount"])); } catch { }
 }
 }
 }
 catch { }
 }
 TotalLimit = limitSum;
 TotalSpent = spentSum;
 TotalLeft = Math.Max(0m, limitSum - spentSum);
 }

 private void UpdateCurrentPeriodName()
 {
 var today = DateTime.Today;
 switch (SelectedPeriod)
 {
 case BudgetPeriodKind.Week:
 int week = System.Globalization.CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(today, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
 CurrentPeriodName = $"Tydzieñ {week} / {today.Year}";
 break;
 case BudgetPeriodKind.Quarter:
 int q = ((today.Month -1) /3) +1;
 string roman = q switch {1 => "I",2 => "II",3 => "III",4 => "IV", _ => q.ToString()};
 CurrentPeriodName = $"{roman} kwarta³ {today.Year}";
 break;
 case BudgetPeriodKind.Year:
 CurrentPeriodName = $"Rok {today.Year}";
 break;
 default:
 var pl = new System.Globalization.CultureInfo("pl-PL");
 var name = today.ToString("MMMM yyyy", pl);
 // capitalize first letter
 if (!string.IsNullOrEmpty(name)) name = char.ToUpper(name[0], pl) + name.Substring(1);
 CurrentPeriodName = name;
 break;
 }
 }
 }

 public enum BudgetPeriodKind { Week, Month, Quarter, Year }
}
