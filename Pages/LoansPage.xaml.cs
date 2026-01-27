using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.ViewModels;
using Finly.Views.Dialogs;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Finly.Pages
{
    public partial class LoansPage : UserControl
    {
        private readonly ObservableCollection<LoanCardVm> _loans = new();
        private readonly int _userId;

        // Cache runtime TYLKO dla sparsowanych wierszy
        private readonly Dictionary<int, List<LoanInstallmentRow>> _parsedSchedules = new();

        // Jeśli dalej trzymasz mapowanie kredyt->konto w RAM (OK na teraz)
        private readonly Dictionary<int, int> _loanAccounts = new();

        private List<BankAccountModel> _accounts = new();

        public LoansPage() : this(UserService.GetCurrentUserId()) { }

        public LoansPage(int userId)
        {
            InitializeComponent();

            _userId = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            LoansGrid.ItemsSource = _loans;
            Loaded += LoansPage_Loaded;
        }

        private void LoansPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAccounts();
            LoadLoans();
            RefreshKpisAndLists();
        }

        private void LoadAccounts()
        {
            try
            {
                _accounts = DatabaseService.GetAccounts(_userId) ?? new List<BankAccountModel>();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się załadować listy kont: " + ex.Message);
                _accounts = new List<BankAccountModel>();
            }
        }

        private void LoadLoans()
        {
            _loans.Clear();

            var list = DatabaseService.GetLoans(_userId) ?? new List<LoanModel>();

            foreach (var l in list)
            {
                _loans.Add(new LoanCardVm
                {
                    Id = l.Id,
                    UserId = l.UserId,
                    Name = l.Name,
                    Principal = l.Principal,
                    InterestRate = l.InterestRate,
                    StartDate = l.StartDate,
                    TermMonths = l.TermMonths,
                    PaymentDay = l.PaymentDay
                });
            }

            // kafelek "Dodaj kredyt" ma być na końcu
            _loans.Add(new AddLoanTile());

            UpdateKpiTiles();
        }

        // ===================== SCHEDULE ATTACH (CSV) =====================

        private void CardAttachSchedule_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement fe || fe.Tag is not LoanCardVm loanVm)
                {
                    ToastService.Info("Nie udało się zidentyfikować kredytu.");
                    return;
                }

                int loanId = loanVm.Id;

                var dlg = new OpenFileDialog
                {
                    Title = "Wybierz plik harmonogramu (CSV)",
                    Filter = "Pliki CSV (*.csv)|*.csv|Wszystkie pliki (*.*)|*.*",
                    Multiselect = false,
                    CheckFileExists = true
                };

                if (dlg.ShowDialog() != true)
                    return;

                string selectedPath = dlg.FileName;
                if (!File.Exists(selectedPath))
                {
                    ToastService.Info("Wybrany plik nie istnieje.");
                    return;
                }

                // 1) Skopiuj do trwałego katalogu aplikacji
                string destPath = CopyLoanScheduleToAppData(loanId, selectedPath);

                // 2) Zapisz ścieżkę w DB (jedno źródło prawdy)
                DatabaseService.SetLoanSchedulePath(loanId, _userId, destPath);

                // 3) Wyczyść cache parsowania (żeby nie trzymać starego)
                _parsedSchedules.Remove(loanId);

                // 4) Odśwież UI
                DatabaseService.NotifyDataChanged();
                RefreshKpisAndLists();

                ToastService.Success("Harmonogram został załączony.");
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się załączyć harmonogramu: " + ex.Message);
            }
        }

        private static string CopyLoanScheduleToAppData(int loanId, string sourcePath)
        {
            // %AppData%\Finly\LoanSchedules\
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Finly",
                "LoanSchedules");

            Directory.CreateDirectory(baseDir);

            // zapisujemy zawsze jako csv dla tego loanId
            string destPath = Path.Combine(baseDir, $"loan_{loanId}.csv");
            string tempPath = destPath + ".tmp";

            File.Copy(sourcePath, tempPath, overwrite: true);

            if (File.Exists(destPath))
                File.Delete(destPath);

            File.Move(tempPath, destPath);

            return destPath;
        }

        // ===================== SCHEDULE READ (shared) =====================

        private bool TryGetSchedule(
            int loanId,
            bool showToasts,
            out string? path,
            out List<LoanInstallmentRow> schedule)
        {
            path = null;
            schedule = new List<LoanInstallmentRow>();

            path = DatabaseService.GetLoanSchedulePath(loanId, _userId);
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (!File.Exists(path))
            {
                if (showToasts)
                    ToastService.Info("Nie znaleziono pliku harmonogramu. Załącz ponownie.");

                // czyścimy DB, bo ścieżka jest martwa
                DatabaseService.SetLoanSchedulePath(loanId, _userId, null);
                DatabaseService.NotifyDataChanged();
                _parsedSchedules.Remove(loanId);
                return false;
            }

            if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                return false;

            if (_parsedSchedules.TryGetValue(loanId, out var cached) && cached != null && cached.Count > 0)
            {
                schedule = cached;
                return true;
            }

            try
            {
                var parser = new LoanScheduleCsvParser();
                schedule = parser.Parse(path).ToList();
                _parsedSchedules[loanId] = schedule;
                return schedule.Count > 0;
            }
            catch (Exception ex)
            {
                _parsedSchedules.Remove(loanId);
                if (showToasts)
                    ToastService.Error("Błąd importu CSV: " + ex.Message);

                System.Diagnostics.Debug.WriteLine(ex);
                return false;
            }
        }

        // ===================== KPI / ANALYSES =====================

        private (decimal totalDebt, decimal monthlySum, decimal yearlySum, int maxRemainingMonths)
            CalculatePortfolioStats(List<LoanCardVm> loans)
        {
            decimal totalDebt = loans.Sum(x => x.Principal);
            decimal monthlySum = 0m;
            decimal yearlySum = 0m;
            int maxRemainingMonths = 0;

            foreach (var vm in loans)
            {
                decimal loanMonthly;
                decimal loanYearly;
                int remainingMonths;

                if (TryGetScheduleStats(vm, out var nextAmount, out _, out remainingMonths, out var yearSumFromSchedule))
                {
                    loanMonthly = nextAmount;
                    loanYearly = yearSumFromSchedule;
                }
                else
                {
                    if (vm.TermMonths > 0)
                    {
                        loanMonthly = LoansService.CalculateMonthlyPayment(vm.Principal, vm.InterestRate, vm.TermMonths);
                        loanYearly = loanMonthly * 12m;
                    }
                    else
                    {
                        loanMonthly = 0m;
                        loanYearly = 0m;
                    }

                    var monthsElapsed =
                        (DateTime.Today.Year - vm.StartDate.Year) * 12 +
                        DateTime.Today.Month - vm.StartDate.Month;

                    remainingMonths = Math.Max(0, vm.TermMonths - monthsElapsed);
                }

                monthlySum += loanMonthly;
                yearlySum += loanYearly;

                if (remainingMonths > maxRemainingMonths)
                    maxRemainingMonths = remainingMonths;
            }

            return (totalDebt, monthlySum, yearlySum, maxRemainingMonths);
        }

        private bool TryGetScheduleStats(
            LoanCardVm vm,
            out decimal nextAmount,
            out DateTime? nextDate,
            out int remainingMonths,
            out decimal yearSum)
        {
            nextAmount = 0m;
            nextDate = null;
            remainingMonths = 0;
            yearSum = 0m;

            if (!TryGetSchedule(vm.Id, showToasts: true, out _, out var schedule))
                return false;

            var today = DateTime.Today;

            var upcoming = schedule
                .Where(r => r.Date >= today)
                .OrderBy(r => r.Date)
                .ToList();

            if (upcoming.Count > 0)
            {
                var next = upcoming.First();
                nextAmount = next.Total;
                nextDate = next.Date;
                remainingMonths = upcoming.Count;
            }
            else
            {
                remainingMonths = 0;
            }

            var yearLimit = today.AddYears(1);
            yearSum = schedule
                .Where(r => r.Date > today && r.Date <= yearLimit)
                .Sum(r => r.Total);

            return true;
        }

        private void RefreshKpisAndLists()
        {
            var loans = _loans.Where(x => x is not AddLoanTile).ToList();

            UpdateKpiTiles();

            SetAnalysisText("—", "—", "—");

            if (!loans.Any())
                return;

            var (totalDebt, _, _, _) = CalculatePortfolioStats(loans);

            decimal weightedRate = 0m;
            if (totalDebt > 0m)
                weightedRate = loans.Sum(l => l.Principal * l.InterestRate) / totalDebt;

            var today = DateTime.Today;
            var in30 = today.AddDays(30);

            decimal interest30 = 0m;
            foreach (var l in loans)
                interest30 += LoanMathService.CalculateInterest(l.Principal, l.InterestRate, today, in30);

            decimal totalToPayFromToday = 0m;

            foreach (var vm in loans)
            {
                if (TryGetScheduleRemainingSum(vm, out var scheduleRemaining))
                {
                    totalToPayFromToday += scheduleRemaining;
                    continue;
                }

                int monthsLeft = GetRemainingMonths(vm);
                if (monthsLeft <= 0)
                    continue;

                var monthly = LoansService.CalculateMonthlyPayment(vm.Principal, vm.InterestRate, monthsLeft);
                totalToPayFromToday += monthly * monthsLeft;
            }

            SetAnalysisText(
                $"{weightedRate:N2} %",
                $"{interest30:N2} zł",
                $"{totalToPayFromToday:N2} zł"
            );
        }

        private void UpdateKpiTiles()
        {
            var loans = _loans.OfType<LoanCardVm>().ToList();
            var (totalDebt, monthlySum, _, _) = CalculatePortfolioStats(loans);

            if (FindName("TotalLoansTileAmount") is TextBlock tbTotal)
                tbTotal.Text = totalDebt.ToString("N2") + " zł";

            if (FindName("MonthlyLoansTileAmount") is TextBlock tbMonthly)
                tbMonthly.Text = monthlySum.ToString("N2") + " zł";
        }

        private void SetAnalysisText(string a1, string a2, string a3)
        {
            if (FindName("Analysis1Value") is TextBlock t1) t1.Text = a1;
            if (FindName("Analysis2Value") is TextBlock t2) t2.Text = a2;
            if (FindName("Analysis3Value") is TextBlock t3) t3.Text = a3;
        }

        private int GetRemainingMonths(LoanCardVm vm)
        {
            if (vm.TermMonths <= 0)
                return 0;

            var monthsElapsed =
                (DateTime.Today.Year - vm.StartDate.Year) * 12 +
                (DateTime.Today.Month - vm.StartDate.Month);

            return Math.Max(0, vm.TermMonths - monthsElapsed);
        }

        private bool TryGetScheduleRemainingSum(LoanCardVm vm, out decimal remainingSum)
        {
            remainingSum = 0m;

            if (!TryGetSchedule(vm.Id, showToasts: false, out _, out var schedule))
                return false;

            var today = DateTime.Today;

            remainingSum = schedule
                .Where(r => r.Date >= today)
                .Sum(r => r.Total);

            return remainingSum > 0m;
        }

        // ===================== DIALOGS =====================

        private Window? GetOwnerWindow() => Window.GetWindow(this);

        private void AddLoanCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            var dlg = new EditLoanDialog(_accounts)
            {
                Owner = GetOwnerWindow()
            };
            dlg.SetMode(EditLoanDialog.Mode.Add);

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var loan = dlg.ResultLoan;
                    if (loan == null)
                    {
                        ToastService.Error("Błąd: dialog nie zwrócił danych kredytu.");
                        return;
                    }

                    loan.UserId = _userId;

                    var id = DatabaseService.InsertLoan(loan);
                    loan.Id = id;

                    if (dlg.SelectedAccountId.HasValue)
                        _loanAccounts[loan.Id] = dlg.SelectedAccountId.Value;

                    // Harmonogram: kopiujemy do AppData i zapisujemy do DB
                    if (!string.IsNullOrWhiteSpace(dlg.AttachedSchedulePath) && File.Exists(dlg.AttachedSchedulePath))
                    {
                        var dest = CopyLoanScheduleToAppData(loan.Id, dlg.AttachedSchedulePath!);
                        DatabaseService.SetLoanSchedulePath(loan.Id, _userId, dest);
                        _parsedSchedules.Remove(loan.Id);
                    }

                    DatabaseService.NotifyDataChanged();

                    ToastService.Success("Kredyt dodany.");
                    LoadLoans();
                    RefreshKpisAndLists();
                }
                catch (Exception ex)
                {
                    ToastService.Error("Błąd dodawania kredytu: " + ex.Message);
                }
            }
        }

        private void EditLoan_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            var dlg = new EditLoanDialog(_accounts)
            {
                Owner = GetOwnerWindow()
            };

            _loanAccounts.TryGetValue(vm.Id, out var accId);

            // Harmonogram pobieramy z DB
            var schedPath = DatabaseService.GetLoanSchedulePath(vm.Id, _userId);

            var loanToEdit = new LoanModel
            {
                Id = vm.Id,
                UserId = vm.UserId,
                Name = vm.Name,
                Principal = vm.Principal,
                InterestRate = vm.InterestRate,
                StartDate = vm.StartDate,
                TermMonths = vm.TermMonths,
                PaymentDay = vm.PaymentDay
            };

            dlg.LoadLoan(loanToEdit, _loanAccounts.ContainsKey(vm.Id) ? accId : (int?)null, schedPath);

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var loan = dlg.ResultLoan;
                    if (loan == null)
                    {
                        ToastService.Error("Błąd: dialog nie zwrócił danych kredytu.");
                        return;
                    }

                    loan.Id = vm.Id;
                    loan.UserId = _userId;

                    DatabaseService.UpdateLoan(loan);

                    if (dlg.SelectedAccountId.HasValue)
                        _loanAccounts[vm.Id] = dlg.SelectedAccountId.Value;
                    else
                        _loanAccounts.Remove(vm.Id);

                    // Harmonogram: jeśli wybrano nowy, kopiujemy do AppData i podmieniamy ścieżkę w DB
                    if (!string.IsNullOrWhiteSpace(dlg.AttachedSchedulePath) && File.Exists(dlg.AttachedSchedulePath))
                    {
                        var dest = CopyLoanScheduleToAppData(vm.Id, dlg.AttachedSchedulePath!);
                        DatabaseService.SetLoanSchedulePath(vm.Id, _userId, dest);
                        _parsedSchedules.Remove(vm.Id);
                    }

                    DatabaseService.NotifyDataChanged();

                    ToastService.Success("Kredyt zaktualizowany.");
                    LoadLoans();
                    RefreshKpisAndLists();
                }
                catch (Exception ex)
                {
                    ToastService.Error("Błąd edycji kredytu: " + ex.Message);
                }
            }
        }

        private void CardOverpay_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            var dlg = new OverpayLoanDialog(vm.Name)
            {
                Owner = GetOwnerWindow()
            };

            if (dlg.ShowDialog() != true)
                return;

            var amt = dlg.Amount;
            if (amt <= 0m)
            {
                ToastService.Error("Podaj poprawną kwotę nadpłaty.");
                return;
            }

            try
            {
                int paymentDay = vm.PaymentDay;
                var today = DateTime.Today;

                var lastDue = LoansService.GetPreviousDueDate(today, paymentDay, vm.StartDate);

                var interest = LoanMathService.CalculateInterest(
                    vm.Principal,
                    vm.InterestRate,
                    lastDue,
                    today);

                if (interest < 0) interest = 0;

                var principalPart = amt - interest;
                if (principalPart < 0) principalPart = 0;

                var newPrincipal = vm.Principal - principalPart;
                if (newPrincipal < 0) newPrincipal = 0;

                var loanToUpdate = new LoanModel
                {
                    Id = vm.Id,
                    UserId = _userId,
                    Name = vm.Name,
                    Principal = newPrincipal,
                    InterestRate = vm.InterestRate,
                    StartDate = vm.StartDate,
                    TermMonths = vm.TermMonths,
                    PaymentDay = vm.PaymentDay
                };

                DatabaseService.UpdateLoan(loanToUpdate);

                var newMonthly = LoansService.CalculateMonthlyPayment(
                    newPrincipal,
                    vm.InterestRate,
                    vm.TermMonths);

                ToastService.Success(
                    $"Nadpłata {amt:N2} zł. Nowy kapitał: {newPrincipal:N2} zł. Szac. nowa rata: {newMonthly:N2} zł.");

                LoadLoans();
                RefreshKpisAndLists();
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd podczas nadpłaty: " + ex.Message);
            }
        }

        private void ShowSimDialog_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm)
                return;

            ToastService.Error("Symulacja nadpłaty nie jest jeszcze zaimplementowana.");
        }

        private void CardSchedule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            if (!TryGetSchedule(vm.Id, showToasts: true, out var path, out var rows))
            {
                ToastService.Error("Nie udało się odczytać harmonogramu. Załącz poprawny plik CSV.");
                return;
            }

            // path jest CSV (TryGetSchedule to gwarantuje)
            try
            {
                if (rows == null || rows.Count == 0)
                {
                    ToastService.Error("Plik CSV nie zawiera rat do wyświetlenia (brak poprawnych wierszy).");
                    return;
                }

                var dlg = new LoanScheduleDialog(vm.Name, rows)
                {
                    Owner = GetOwnerWindow()
                };

                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się otworzyć harmonogramu: " + ex.Message);
                System.Diagnostics.Debug.WriteLine("LoanSchedule open error: " + ex);
                _parsedSchedules.Remove(vm.Id);
            }
        }

        // ===================== DELETE =====================

        private void DeleteLoan_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            HideAllDeletePanels();

            FrameworkElement? container = fe;
            while (container != null && container is not ContentPresenter && container is not Border)
                container = VisualTreeHelper.GetParent(container) as FrameworkElement;

            if (container == null) return;

            var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
            if (panel == null) return;

            panel.Visibility = panel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ConfirmDeleteLoan_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                try
                {
                    // 1) Usuń harmonogram z DB (przed DeleteLoan, bo po usunięciu rekordu Update/Set może nie działać)
                    DatabaseService.SetLoanSchedulePath(vm.Id, _userId, null);

                    // 2) Usuń rekord kredytu
                    DatabaseService.DeleteLoan(vm.Id, _userId);

                    // 3) Czyścimy cache/powiązania w RAM
                    _loanAccounts.Remove(vm.Id);
                    _parsedSchedules.Remove(vm.Id);

                    DatabaseService.NotifyDataChanged();

                    ToastService.Success("Kredyt usunięty.");
                    LoadLoans();
                    RefreshKpisAndLists();
                }
                catch (Exception ex)
                {
                    ToastService.Error("Błąd usuwania kredytu: " + ex.Message);
                }
            }

            HideAllDeletePanels();
        }

        private void DeleteCancel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement btn) return;

            var parentBorder = FindVisualParent<Border>(btn);
            if (parentBorder != null && parentBorder.Name == "DeleteConfirmPanel")
            {
                parentBorder.Visibility = Visibility.Collapsed;
                return;
            }

            HideAllDeletePanels();
        }

        private void HideAllDeletePanels()
        {
            foreach (var item in LoansGrid.Items)
            {
                var container = LoansGrid.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                if (container == null) continue;

                var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
                if (panel != null)
                    panel.Visibility = Visibility.Collapsed;
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            if (child == null) return null;

            DependencyObject? parent = VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not T)
                parent = VisualTreeHelper.GetParent(parent);

            return parent as T;
        }

        private static T? FindDescendantByName<T>(DependencyObject? start, string name) where T : FrameworkElement
        {
            if (start == null) return null;

            int cnt = VisualTreeHelper.GetChildrenCount(start);
            for (int i = 0; i < cnt; i++)
            {
                var ch = VisualTreeHelper.GetChild(start, i) as FrameworkElement;
                if (ch == null) continue;

                if (ch is T fe && fe.Name == name)
                    return fe;

                var deeper = FindDescendantByName<T>(ch, name);
                if (deeper != null) return deeper;
            }

            return null;
        }
    }
}
