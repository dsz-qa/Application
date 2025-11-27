using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Finly.Helpers;

namespace Finly.ViewModels
{
 public class ReportsViewModel : INotifyPropertyChanged
 {
 public event PropertyChangedEventHandler? PropertyChanged;

 protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

 public ReportsViewModel()
 {
 FromDate = DateTime.Today.AddMonths(-1);
 ToDate = DateTime.Today;

 Accounts = new ObservableCollection<string> { "Wszystkie konta", "Konto osobiste", "Karta kredytowa" };
 Categories = new ObservableCollection<string> { "Wszystkie kategorie", "Jedzenie", "Rozrywka", "Subskrypcje" };
 Envelopes = new ObservableCollection<string> { "Wszystkie koperty", "Mieszkanie", "Zakupy" };
 Tags = new ObservableCollection<string> { "Brak", "Podró¿e", "Praca" };
 Currencies = new ObservableCollection<string> { "PLN", "EUR", "USD" };
 Templates = new ObservableCollection<string> { "Domyœlny", "Miesiêczny przegl¹d" };

 Insights = new ObservableCollection<string>
 {
 "Wydatki w tym miesi¹cu wzros³y o12% wzglêdem poprzedniego.",
 "Najwiêkszy wydatek: Jedzenie —1234,00 z³",
 "Subskrypcje:3 aktywne" 
 };

 KPIList = new ObservableCollection<KeyValuePair<string, string>>
 {
 new KeyValuePair<string,string>("Suma wydatków","1234,00 z³"),
 new KeyValuePair<string,string>("Przychody","4500,00 z³"),
 new KeyValuePair<string,string>("Oszczêdnoœci","2300,00 z³")
 };

 RefreshCommand = new RelayCommand(_ => Refresh());
 SavePresetCommand = new RelayCommand(_ => SavePreset());
 ExportCsvCommand = new RelayCommand(_ => ExportCsv());
 ExportExcelCommand = new RelayCommand(_ => ExportExcel());
 ExportPdfCommand = new RelayCommand(_ => ExportPdf());
 }

 public DateTime FromDate { get; set; }
 public DateTime ToDate { get; set; }

 public ObservableCollection<string> Accounts { get; }
 public ObservableCollection<string> Categories { get; }
 public ObservableCollection<string> Envelopes { get; }
 public ObservableCollection<string> Tags { get; }
 public ObservableCollection<string> Currencies { get; }
 public ObservableCollection<string> Templates { get; }

 public string SelectedAccount { get; set; } = "Wszystkie konta";
 public string SelectedCategory { get; set; } = "Wszystkie kategorie";
 public string SelectedTemplate { get; set; } = "Domyœlny";

 public ObservableCollection<string> Insights { get; }
 public ObservableCollection<KeyValuePair<string, string>> KPIList { get; }

 public ICommand RefreshCommand { get; }
 public ICommand SavePresetCommand { get; }
 public ICommand ExportCsvCommand { get; }
 public ICommand ExportExcelCommand { get; }
 public ICommand ExportPdfCommand { get; }

 private void Refresh()
 {
 // TODO: load actual report data from services / database
 MessageBox.Show("Odœwie¿ono raporty (mock).", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
 }

 private void SavePreset()
 {
 // Simple preset save placeholder — real app should persist user presets
 MessageBox.Show($"Zapisano preset: {SelectedTemplate}", "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
 }

 private void ExportCsv()
 {
 try
 {
 var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "report_export.csv");
 var sb = new StringBuilder();
 sb.AppendLine("Category,Amount,SharePercent");
 sb.AppendLine("Jedzenie,1234.00,50.0");
 sb.AppendLine("Rozrywka,740.00,30.0");
 sb.AppendLine("Pozosta³e,480.00,20.0");
 File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
 MessageBox.Show($"Eksport CSV zapisano na pulpicie: {path}", "Eksport", MessageBoxButton.OK, MessageBoxImage.Information);
 }
 catch (Exception ex)
 {
 MessageBox.Show($"B³¹d eksportu CSV: {ex.Message}", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
 }
 }

 private void ExportExcel()
 {
 // For simplicity export as CSV with .xlsx extension is not a real Excel file — placeholder
 try
 {
 var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "report_export.xlsx");
 var sb = new StringBuilder();
 sb.AppendLine("Category\tAmount\tSharePercent");
 sb.AppendLine("Jedzenie\t1234.00\t50.0");
 sb.AppendLine("Rozrywka\t740.00\t30.0");
 sb.AppendLine("Pozosta³e\t480.00\t20.0");
 File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
 MessageBox.Show($"Eksport Excel (TSV) zapisano na pulpicie: {path}", "Eksport", MessageBoxButton.OK, MessageBoxImage.Information);
 }
 catch (Exception ex)
 {
 MessageBox.Show($"B³¹d eksportu Excel: {ex.Message}", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
 }
 }

 private void ExportPdf()
 {
 // Placeholder: real implementation should use QuestPDF to generate styled PDF
 MessageBox.Show("Eksport PDF nie zosta³ jeszcze zaimplementowany.", "Funkcja w przygotowaniu", MessageBoxButton.OK, MessageBoxImage.Information);
 }
 }
}
