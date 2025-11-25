using Finly.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Finly.Pages
{
 public partial class ReportPage : UserControl
 {
 private readonly int _uid;
 private Dictionary<string, decimal>? _lastTotals;
 private decimal _lastTotalAll;

 public ReportPage() : this(UserService.GetCurrentUserId()) { }

 public ReportPage(int userId)
 {
 InitializeComponent();
 _uid = userId <=0 ? UserService.GetCurrentUserId() : userId;
 Loaded += ReportPage_Loaded;

 // wire up UI (guard nulls)
 if (ReportTypesList != null) ReportTypesList.SelectionChanged += ReportTypesList_SelectionChanged;
 if (ExportBtn != null) ExportBtn.Click += ExportBtn_Click;
 if (SavePresetBtn != null) SavePresetBtn.Click += SavePresetBtn_Click;
 if (CompareBtn != null) CompareBtn.Click += CompareBtn_Click;

 if (PresetCombo != null) PresetCombo.ItemsSource = new[] { "Dziœ", "Ten tydzieñ", "Ten miesi¹c", "Poprzedni miesi¹c", "Ostatnie3 miesi¹ce", "Ten rok", "Ca³y okres" };
 if (ViewTypeCombo != null) ViewTypeCombo.ItemsSource = new[] { "Wydatki", "Przychody", "Bilans", "Wszystko" };
 if (ReportTypesList != null) ReportTypesList.ItemsSource = new[] {
 "Struktura kategorii",
 "Trend w czasie",
 "Cashflow",
 "Heatmapa dni tygodnia",
 "Subskrypcje i zobowi¹zania",
 "Bud¿ety vs rzeczywistoœæ",
 "Cele i koperty - postêp"
 };

 if (StartDatePicker != null) StartDatePicker.SelectedDate = DateTime.Today.AddMonths(-1);
 if (EndDatePicker != null) EndDatePicker.SelectedDate = DateTime.Today;
 }

 private void ReportPage_Loaded(object? sender, RoutedEventArgs e)
 {
 LoadSelectedReport();
 }

 private void ReportTypesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadSelectedReport();

 private void ExportBtn_Click(object sender, RoutedEventArgs e) { ToastService.Info("Eksport (stub)"); }
 private void SavePresetBtn_Click(object sender, RoutedEventArgs e) { ToastService.Success("Preset zapisany (stub)"); }
 private void CompareBtn_Click(object sender, RoutedEventArgs e) { ToastService.Info("Compare (stub)"); }

 private void LoadSelectedReport()
 {
 var sel = (ReportTypesList?.SelectedItem as string) ?? "Struktura kategorii";
 switch (sel)
 {
 case "Struktura kategorii": GenerateCategoryStructureReport(); break;
 case "Trend w czasie": GenerateTrendReport(); break;
 default: GenerateCategoryStructureReport(); break;
 }
 }

 private void GenerateCategoryStructureReport()
 {
 try
 {
 var from = StartDatePicker?.SelectedDate ?? DateTime.Today.AddMonths(-1);
 var to = EndDatePicker?.SelectedDate ?? DateTime.Today;
 if (to < from) (from, to) = (to, from);

 var data = DatabaseService.GetSpendingByCategorySafe(_uid, from, to) ?? new List<DatabaseService.CategoryAmountDto>();
 var list = data.Where(x => x.Amount >0).OrderByDescending(x => x.Amount).ToList();
 var sum = list.Sum(x => x.Amount);

 var rows = list.Select(x => new ReportRow { Category = x.Name, Amount = x.Amount, SharePercent = sum >0 ? (double)(x.Amount / sum *100m) :0.0 }).ToList();
 if (ReportDataGrid != null) ReportDataGrid.ItemsSource = rows;

 BuildLegend(rows, sum);
 BuildKpis(rows, sum);
 BuildInsights_Category(rows, sum, from, to);
 }
 catch (Exception ex)
 {
 ToastService.Error("B³¹d: " + ex.Message);
 }
 }

 private void GenerateTrendReport()
 {
 if (ChartsPlaceholder != null) ChartsPlaceholder.Text = "Trend: wykres liniowy (w budowie)";
 if (ReportDataGrid != null) ReportDataGrid.ItemsSource = null;
 if (KpiItems != null) KpiItems.ItemsSource = null;
 if (InsightsList != null) InsightsList.ItemsSource = new ObservableCollection<string> { "Trend insights (stub)" };
 }

 private void BuildLegend(List<ReportRow> rows, decimal sum)
 {
 if (LegendPanel == null) return;
 LegendPanel.Children.Clear();
 var palette = new[] { "#FFED7A1A", "#FF3FA7D6", "#FF7BC96F", "#FFAF7AC5", "#FFF6BF26", "#FF56C1A7" };
 int i =0;
 foreach (var r in rows)
 {
 var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,4,0,4) };
 var rect = new System.Windows.Shapes.Rectangle { Width =12, Height =12, Fill = (Brush)new BrushConverter().ConvertFromString(palette[i % palette.Length]) };
 sp.Children.Add(rect);
 sp.Children.Add(new TextBlock { Text = $" {r.Category} — {r.Amount:N2} ({r.SharePercent:N1}%)", VerticalAlignment = VerticalAlignment.Center });
 LegendPanel.Children.Add(sp);
 i++;
 }
 }

 private void BuildKpis(List<ReportRow> rows, decimal sum)
 {
 if (KpiItems == null) return;
 var kpis = new List<object>();
 kpis.Add(new { Title = "Sumarycznie", Value = sum.ToString("N2") + " z³", Subtitle = $"{rows.Count} pozycji" });
 if (rows.Count >0)
 {
 var top = rows[0];
 kpis.Add(new { Title = "Top kategoria", Value = top.Category + " — " + top.Amount.ToString("N2") + " z³", Subtitle = $"{top.SharePercent:N1}%" });
 }
 KpiItems.ItemsSource = kpis;
 }

 private void BuildInsights_Category(List<ReportRow> rows, decimal sum, DateTime from, DateTime to)
 {
 if (InsightsList == null) return;
 var insights = new ObservableCollection<string>();
 if (sum <=0)
 {
 insights.Add("Brak wydatków w wybranym okresie.");
 }
 else
 {
 insights.Add($"£¹czne wydatki: {sum:N2} z³ w okresie {from:dd.MM.yyyy}–{to:dd.MM.yyyy}.");
 if (rows.Count >0) insights.Add($"Najwiêcej wydajesz na: {rows[0].Category} ({rows[0].SharePercent:N1}% ca³oœci).");
 if (rows.Count >=3) insights.Add($"Top3: {string.Join(", ", rows.Take(3).Select(r => r.Category))}.");
 }
 InsightsList.ItemsSource = insights;
 }

 private class ReportRow { public string Category { get; set; } = ""; public decimal Amount { get; set; } public double SharePercent { get; set; } }
 }
}
