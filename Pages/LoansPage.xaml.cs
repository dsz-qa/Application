using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.ViewModels;
using Finly.Views.Dialogs;

namespace Finly.Pages
{
    public partial class LoansPage : UserControl
    {
        private readonly ObservableCollection<object> _loans = new();
        private readonly int _userId;

        // Pamięć runtime
        private readonly Dictionary<int, string> _loanScheduleFiles = new();
        private readonly Dictionary<int, List<LoanInstallmentRow>> _parsedSchedules = new();
        private readonly Dictionary<int, int> _loanAccounts = new();

        private List<BankAccountModel> _accounts = new();

        public LoansPage()
            : this(UserService.GetCurrentUserId())
        {
        }

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

            UpdateKpiTiles();
        }

        private static string FormatMonths(int months)
        {
            if (months <= 0) return "0 mies.";

            int years = months / 12;
            int monthsLeft = months % 12;

            if (years > 0 && monthsLeft > 0) return $"{years} lat {monthsLeft} mies.";
            if (years > 0) return $"{years} lat";
            return $"{monthsLeft} mies.";
        }

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

            if (!_loanScheduleFiles.TryGetValue(vm.Id, out var path) ||
                string.IsNullOrWhiteSpace(path) ||
                !string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                return false;
            }

            List<LoanInstallmentRow> schedule;

            if (_parsedSchedules.TryGetValue(vm.Id, out var cached) && cached != null && cached.Count > 0)
            {
                schedule = cached;
            }
            else
            {
                try
                {
                    var parser = new LoanScheduleCsvParser();
                    schedule = parser.Parse(path).ToList();
                    _parsedSchedules[vm.Id] = schedule;
                }
                catch
                {
                    return false;
                }
            }

            if (schedule.Count == 0)
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
            var loans = _loans.OfType<LoanCardVm>().ToList();

            UpdateKpiTiles();

            var insights = new ObservableCollection<LoanInsightVm>();

            if (loans.Any())
            {
                var (totalDebt, _, yearlySum, maxRemainingMonths) = CalculatePortfolioStats(loans);

                decimal weightedRate = 0m;
                if (totalDebt > 0m)
                    weightedRate = loans.Sum(l => l.Principal * l.InterestRate) / totalDebt;

                var today = DateTime.Today;
                var in30 = today.AddDays(30);

                decimal interest30 = 0m;
                foreach (var l in loans)
                    interest30 += LoanMathService.CalculateInterest(l.Principal, l.InterestRate, today, in30);

                insights.Add(new LoanInsightVm
                {
                    Label = "Średnie oprocentowanie portfela (ważone saldem)",
                    Value = $"{weightedRate:N2} %"
                });

                insights.Add(new LoanInsightVm
                {
                    Label = "Szacowane odsetki w kolejne 30 dni",
                    Value = $"{interest30:N2} zł"
                });

                insights.Add(new LoanInsightVm
                {
                    Label = "Prognozowana łączna kwota rat w ciągu roku",
                    Value = $"{yearlySum:N2} zł"
                });

                insights.Add(new LoanInsightVm
                {
                    Label = "Całkowity czas spłaty kredytów",
                    Value = FormatMonths(maxRemainingMonths)
                });
            }

            InsightsList.ItemsSource = insights;
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

                    if (!string.IsNullOrWhiteSpace(dlg.AttachedSchedulePath))
                        _loanScheduleFiles[loan.Id] = dlg.AttachedSchedulePath!;

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
            _loanScheduleFiles.TryGetValue(vm.Id, out var schedPath);

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

                    if (!string.IsNullOrWhiteSpace(dlg.AttachedSchedulePath))
                        _loanScheduleFiles[vm.Id] = dlg.AttachedSchedulePath!;
                    else
                        _loanScheduleFiles.Remove(vm.Id);

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

            if (!_loanScheduleFiles.TryGetValue(vm.Id, out var path) || string.IsNullOrWhiteSpace(path))
            {
                ToastService.Error("Nie załączyłaś jeszcze harmonogramu spłaty rat dla tego kredytu.");
                return;
            }

            if (!File.Exists(path))
            {
                ToastService.Error("Nie znaleziono pliku harmonogramu. Załącz go ponownie.");
                _loanScheduleFiles.Remove(vm.Id);
                _parsedSchedules.Remove(vm.Id);
                return;
            }

            try
            {
                if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                {
                    ToastService.Error("Aktualnie harmonogram analizujemy tylko z CSV (PDF dodamy później).");
                    return;
                }

                if (!_parsedSchedules.TryGetValue(vm.Id, out var rows) || rows == null || rows.Count == 0)
                {
                    var parser = new LoanScheduleCsvParser();
                    rows = parser.Parse(path).ToList();
                    _parsedSchedules[vm.Id] = rows;
                }

                var dlg = new LoanScheduleDialog(vm.Name, rows)
                {
                    Owner = GetOwnerWindow()
                };

                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się odczytać harmonogramu. Sprawdź format pliku CSV.");
                System.Diagnostics.Debug.WriteLine("LoanSchedule open error: " + ex);
            }
        }

        private void CardAttachSchedule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Pliki CSV|*.csv|Pliki PDF|*.pdf|Wszystkie pliki|*.*"
            };

            var ok = dlg.ShowDialog();
            if (ok != true) return;

            var path = dlg.FileName;

            _loanScheduleFiles[vm.Id] = path;
            _parsedSchedules.Remove(vm.Id);

            if (string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var parser = new LoanScheduleCsvParser();
                    var rows = parser.Parse(path).ToList();
                    _parsedSchedules[vm.Id] = rows;

                    ToastService.Success("Harmonogram spłat został załączony i odczytany.");

                    LoadLoans();
                    RefreshKpisAndLists();
                    return;
                }
                catch (Exception ex)
                {
                    ToastService.Error("Nie udało się odczytać harmonogramu. Sprawdź format pliku CSV.");
                    System.Diagnostics.Debug.WriteLine("LoanScheduleCsvParser error: " + ex);
                    return;
                }
            }

            ToastService.Success("Harmonogram spłat został załączony.");
            LoadLoans();
            RefreshKpisAndLists();
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
                    DatabaseService.DeleteLoan(vm.Id, _userId);
                    ToastService.Success("Kredyt usunięty.");

                    _loanAccounts.Remove(vm.Id);
                    _loanScheduleFiles.Remove(vm.Id);
                    _parsedSchedules.Remove(vm.Id);

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
