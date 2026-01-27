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

            // klucz: po załadowaniu kart wstrzykujemy snapshoty z harmonogramów
            ApplyScheduleSnapshotsToLoanVms();

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

            UpdatePaidStatusForCurrentMonth(_loans);

        }

        // ===================== SNAPSHOT FROM SCHEDULE -> VM =====================

        private void ApplyScheduleSnapshotsToLoanVms()
        {
            foreach (var vm in _loans.OfType<LoanCardVm>())
            {
                ApplyScheduleSnapshotToVm(vm);
            }
        }

        private void ApplyScheduleSnapshotToVm(LoanCardVm vm)
        {
            try
            {
                // jeśli nie ma harmonogramu / nie da się odczytać -> czyścimy snapshot
                if (!TryGetSchedule(vm.Id, showToasts: false, out _, out var schedule) || schedule == null || schedule.Count == 0)
                {
                    vm.ClearScheduleSnapshot();
                    return;
                }

                var today = DateTime.Today;

                // sort pewności (parser już sortuje, ale defensywnie)
                var ordered = schedule.OrderBy(r => r.Date).ToList();

                // next installment (>= today)
                var upcoming = ordered.Where(r => r.Date >= today).OrderBy(r => r.Date).ToList();
                var next = upcoming.FirstOrDefault();

                decimal? nextAmount = next != null ? next.Total : (decimal?)null;
                DateTime? nextDate = next != null ? next.Date : (DateTime?)null;

                decimal? nextCap = next?.Principal;
                decimal? nextInt = next?.Interest;


                int? remainingInstallments = upcoming.Count;

                // ORIGINAL principal: najlepsze źródło to największe Remaining (zwykle na początku harmonogramu),
                // ale banki różnie zapisują saldo (przed/po). Bezpiecznie: max(Remaining) jeśli istnieje, inaczej vm.Principal
                decimal? originalPrincipal = null;
                var remainingValsAll = ordered.Where(x => x.Remaining.HasValue && x.Remaining.Value >= 0m).Select(x => x.Remaining!.Value).ToList();
                if (remainingValsAll.Count > 0)
                    originalPrincipal = remainingValsAll.Max();

                // REMAINING principal:
                // 1) jeśli wiersz "next" ma Remaining -> bierzemy go
                // 2) inaczej bierzemy ostatni Remaining z przyszłości
                // 3) inaczej (brak Remaining) -> sum(przyszły Principal) jeśli jest
                // 4) fallback -> vm.Principal
                decimal? remainingPrincipal = null;

                if (next != null && next.Remaining.HasValue && next.Remaining.Value >= 0m)
                {
                    remainingPrincipal = next.Remaining.Value;
                }
                else
                {
                    var remFuture = upcoming
                        .Where(x => x.Remaining.HasValue && x.Remaining.Value >= 0m)
                        .Select(x => x.Remaining!.Value)
                        .ToList();

                    if (remFuture.Count > 0)
                    {
                        // bierzemy wartość z najbliższego sensownego wiersza (pierwszy z Remaining)
                        remainingPrincipal = remFuture.First();
                    }
                    else
                    {
                        // brak Remaining w ogóle -> próbujemy zsumować przyszły kapitał
                        var capFuture = upcoming
                            .Where(x => x.Principal.HasValue && x.Principal.Value >= 0m)
                            .Select(x => x.Principal!.Value)
                            .ToList();

                        if (capFuture.Count > 0)
                            remainingPrincipal = capFuture.Sum();
                    }
                }

                // jeśli nadal null, fallback do pola w DB
                if (!remainingPrincipal.HasValue)
                    remainingPrincipal = vm.Principal;

                // jeśli nadal null/<=0, to chociaż nie rozwalaj %:
                if (remainingPrincipal < 0m) remainingPrincipal = 0m;

                vm.ApplyScheduleSnapshot(
                    originalPrincipal: originalPrincipal,
                    remainingPrincipal: remainingPrincipal,
                    nextPaymentAmount: nextAmount,
                    nextPaymentDate: nextDate,
                    remainingInstallments: remainingInstallments,
                    nextPaymentPrincipalPart: nextCap,
                    nextPaymentInterestPart: nextInt);

            }
            catch
            {
                // w razie jakiegokolwiek błędu snapshotu nie wywracamy UI
                vm.ClearScheduleSnapshot();
            }
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

                // 4) Zastosuj snapshot natychmiast dla tej karty (bez czekania)
                ApplyScheduleSnapshotToVm(loanVm);

                // 5) Odśwież KPI/analizy
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
            // totalDebt ma być “pozostało” — z harmonogramu jeśli jest
            decimal totalDebt = loans.Sum(x => x.DisplayRemainingPrincipal);

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
                    // fallback tylko gdy nie ma schedule
                    if (vm.TermMonths > 0)
                    {
                        loanMonthly = LoansService.CalculateMonthlyPayment(vm.DisplayRemainingPrincipal, vm.InterestRate, vm.TermMonths);
                        loanYearly = loanMonthly * 12m;
                    }
                    else
                    {
                        loanMonthly = 0m;
                        loanYearly = 0m;
                    }

                    remainingMonths = GetRemainingMonths(vm);
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

            if (!TryGetSchedule(vm.Id, showToasts: false, out _, out var schedule))
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

        private void UpdatePaidStatusForCurrentMonth(IReadOnlyCollection<LoanCardVm> loans)
        {
            if (loans == null || loans.Count == 0)
            {
                if (FindName("Analysis1PaidStatus") is TextBlock s1) s1.Text = "";
                if (FindName("Analysis2PaidStatus") is TextBlock s2) s2.Text = "";
                return;
            }

            var today = DateTime.Today.Date;

            // "Zapłacone" pokazujemy dopiero, gdy minął termin ostatniej raty z bieżącego miesiąca
            var dueDates = loans
                .Select(vm => GetDueDateForMonth(vm, today.Year, today.Month))
                .ToList();

            var lastDueThisMonth = dueDates.Max().Date;

            bool monthInstallmentsAlreadyPaid = today > lastDueThisMonth;

            if (FindName("Analysis1PaidStatus") is TextBlock t1)
                t1.Text = monthInstallmentsAlreadyPaid ? "✓ Zapłacone w tym miesiącu" : "";

            if (FindName("Analysis2PaidStatus") is TextBlock t2)
                t2.Text = monthInstallmentsAlreadyPaid ? "✓ Zapłacone w tym miesiącu" : "";
        }



        private void RefreshKpisAndLists()
        {
            var loans = _loans.OfType<LoanCardVm>().ToList();

            ApplyScheduleSnapshotsToLoanVms();
            UpdateKpiTiles();

            // domyślnie
            SetAnalysisText("—", "—", "—");

            

            if (!loans.Any())
                return;

            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var monthEnd = monthStart.AddMonths(1);

            decimal capitalThisMonth = 0m;
            decimal interestThisMonth = 0m;

            // A3: koszt całkowity od dziś do końca
            decimal totalCostAllLoans = 0m;

            foreach (var vm in loans)
            {
                // ==== jeśli jest harmonogram ====
                if (TryGetSchedule(vm.Id, showToasts: false, out _, out var schedule) && schedule.Count > 0)
                {
                    // A1/A2: raty w tym miesiącu
                    var rowsThisMonth = schedule
                        .Where(r => r.Date >= monthStart && r.Date < monthEnd)
                        .ToList();

                    // jeśli bank podał breakdown — używamy
                    var capSum = rowsThisMonth.Sum(r => r.Principal ?? 0m);
                    var intSum = rowsThisMonth.Sum(r => r.Interest ?? 0m);
                    var totalSum = rowsThisMonth.Sum(r => r.Total);

                    // gdy breakdown nie istnieje, ale Total jest:
                    // wylicz interes dzienny, kapitał = total - interest
                    if (totalSum > 0m && capSum == 0m && intSum == 0m)
                    {
                        foreach (var row in rowsThisMonth.OrderBy(x => x.Date))
                        {
                            var prevDue = LoansService.GetPreviousDueDate(row.Date, vm.PaymentDay, vm.StartDate);

                            // baza do odsetek: najlepsze co mamy
                            var principalBase = vm.DisplayRemainingPrincipal;
                            if (row.Remaining.HasValue && row.Remaining.Value > 0m)
                                principalBase = row.Remaining.Value;

                            var i = LoanMathService.CalculateInterest(principalBase, vm.InterestRate, prevDue, row.Date);
                            if (i < 0m) i = 0m;

                            var c = row.Total - i;
                            if (c < 0m) c = 0m;

                            interestThisMonth += i;
                            capitalThisMonth += c;
                        }
                    }
                    else
                    {
                        capitalThisMonth += capSum;
                        interestThisMonth += intSum;
                    }

                    // A3: suma rat od dziś do końca (kapitał + odsetki = total)
                    totalCostAllLoans += schedule
                        .Where(r => r.Date >= today)
                        .Sum(r => r.Total);

                    continue;
                }

                // ==== fallback bez harmonogramu ====
                int monthsLeft = GetRemainingMonths(vm);
                if (monthsLeft <= 0) continue;

                var (interestPart, principalPart) =
                    LoansService.CalculateFirstInstallmentBreakdown(vm.DisplayRemainingPrincipal, vm.InterestRate, monthsLeft);

                capitalThisMonth += principalPart;
                interestThisMonth += interestPart;

                // A3: przybliżenie kosztu = rata * liczba miesięcy pozostałych
                var monthly = LoansService.CalculateMonthlyPayment(vm.DisplayRemainingPrincipal, vm.InterestRate, monthsLeft);
                totalCostAllLoans += monthly * monthsLeft;
            }

            // ustaw analizy w UI
            SetAnalysisText(
                $"{capitalThisMonth:N2} zł",
                $"{interestThisMonth:N2} zł",
                $"{totalCostAllLoans:N2} zł"
            );

            // status “zapłacone w tym miesiącu”
            UpdatePaidStatusForCurrentMonth(loans);
        }



        private static DateTime GetDueDateForMonth(LoanCardVm vm, int year, int month)
        {
            int pd = vm.PaymentDay <= 0 ? 1 : vm.PaymentDay;
            int dim = DateTime.DaysInMonth(year, month);
            int day = Math.Min(pd, dim);

            var due = new DateTime(year, month, day);

            // weekend -> następny roboczy (spójnie z LoansService.GetNextDueDate)
            if (due.DayOfWeek == DayOfWeek.Saturday) due = due.AddDays(2);
            else if (due.DayOfWeek == DayOfWeek.Sunday) due = due.AddDays(1);

            // nie wcześniej niż start
            if (due.Date < vm.StartDate.Date) due = vm.StartDate.Date;

            return due.Date;
        }


        private void UpdateKpiTiles()
        {
            var loans = _loans.OfType<LoanCardVm>().ToList();
            var (_, monthlySum, _, _) = CalculatePortfolioStats(loans);

            // Przyznana kwota = “oryginalny kapitał” z harmonogramu jeśli jest, inaczej Principal z DB
            decimal grantedSum = loans.Sum(x => x.DisplayOriginalPrincipal);

            if (FindName("TotalLoansTileAmount") is TextBlock tbTotal)
                tbTotal.Text = grantedSum.ToString("N2") + " zł";

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
            // jeśli VM ma harmonogram – to jest lepsze źródło
            if (vm.HasSchedule)
            {
                // RemainingTermStr jest tekstem, więc tu bierzemy z harmonogramu przez TryGetScheduleStats
                if (TryGetScheduleStats(vm, out _, out _, out var remainingMonths, out _))
                    return remainingMonths;
            }

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
                    ApplyScheduleSnapshotsToLoanVms();
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
                    ApplyScheduleSnapshotsToLoanVms();
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
                // nadpłata działa na "Principal" w DB (saldo),
                // ale jeśli masz harmonogram, to on się rozjedzie dopóki go nie podmienisz/odświeżysz.
                int paymentDay = vm.PaymentDay;
                var today = DateTime.Today;

                var lastDue = LoansService.GetPreviousDueDate(today, paymentDay, vm.StartDate);

                var interest = LoanMathService.CalculateInterest(
                    vm.DisplayRemainingPrincipal, // ważne: od pozostałego salda
                    vm.InterestRate,
                    lastDue,
                    today);

                if (interest < 0) interest = 0;

                var principalPart = amt - interest;
                if (principalPart < 0) principalPart = 0;

                var newPrincipal = vm.Principal - principalPart; // DB principal
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
                ApplyScheduleSnapshotsToLoanVms();
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
                vm.ClearScheduleSnapshot();
                RefreshKpisAndLists();
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
                    DatabaseService.SetLoanSchedulePath(vm.Id, _userId, null);
                    DatabaseService.DeleteLoan(vm.Id, _userId);

                    _loanAccounts.Remove(vm.Id);
                    _parsedSchedules.Remove(vm.Id);

                    DatabaseService.NotifyDataChanged();

                    ToastService.Success("Kredyt usunięty.");
                    LoadLoans();
                    ApplyScheduleSnapshotsToLoanVms();
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
