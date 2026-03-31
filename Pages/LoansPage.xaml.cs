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
using Windows.System;
using static Finly.Services.Features.DatabaseService;

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

            Unloaded += LoansPage_Unloaded;
            DatabaseService.DataChanged += DatabaseService_DataChanged;
        }

        private void LoansPage_Unloaded(object sender, RoutedEventArgs e)
        {
            try { DatabaseService.DataChanged -= DatabaseService_DataChanged; } catch { }
        }

        private void DatabaseService_DataChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    LoadAccounts();
                    LoadLoans();
                    ApplyScheduleSnapshotsToLoanVms();
                    RefreshKpisAndLists();
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }


        private void LoansPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAccounts();
            LoadLoans();
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
                    PaymentDay = l.PaymentDay,

                    OverrideMonthlyPayment = l.OverrideMonthlyPayment,
                    OverrideRemainingMonths = l.OverrideRemainingMonths
                });

            }

            // kafelek "Dodaj kredyt" ma być na końcu
            _loans.Add(new AddLoanTile());



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
                var today = DateTime.Today;

                var dbRows = DatabaseService.GetLoanInstallments(_userId, vm.Id)
                             ?? new List<DatabaseService.LoanInstallmentDb>();

                var orderedDb = dbRows
                    .Where(x => x.DueDate != DateTime.MinValue)
                    .OrderBy(x => x.DueDate.Date)
                    .ThenBy(x => x.InstallmentNo)
                    .ToList();

                DatabaseService.LoanInstallmentDb? nextUnpaidDb = orderedDb
                    .Where(x => x.Status == 0)
                    .OrderBy(x => x.DueDate.Date)
                    .ThenBy(x => x.InstallmentNo)
                    .FirstOrDefault();

                int remainingInstallmentsDb = orderedDb.Count(x => x.Status == 0);

                DateTime? nextPaymentDateDb = nextUnpaidDb?.DueDate.Date;
                decimal? nextPaymentAmountDb = nextUnpaidDb?.TotalAmount;
                decimal? nextPaymentPrincipalDb = nextUnpaidDb?.PrincipalAmount;
                decimal? nextPaymentInterestDb = nextUnpaidDb?.InterestAmount;

                decimal? originalPrincipalDb = null;
                var firstRow = orderedDb.FirstOrDefault();
                if (firstRow != null)
                {
                    if (firstRow.RemainingBalance.HasValue && firstRow.PrincipalAmount.HasValue)
                    {
                        // saldo po spłacie pierwszej raty + kapitał tej raty = stan przed pierwszą ratą
                        originalPrincipalDb = Math.Max(
                            0m,
                            firstRow.RemainingBalance.Value + firstRow.PrincipalAmount.Value);
                    }
                    else if (firstRow.RemainingBalance.HasValue)
                    {
                        originalPrincipalDb = Math.Max(0m, firstRow.RemainingBalance.Value);
                    }
                }

                decimal? currentRemainingPrincipalDb = null;

                if (nextUnpaidDb != null)
                {
                    if (nextUnpaidDb.RemainingBalance.HasValue)
                    {
                        // UWAGA:
                        // RemainingBalance z harmonogramu bankowego to zwykle saldo PO spłacie tej konkretnej raty.
                        // Jeżeli rata jest nadal nieopłacona (Status=0), to aktualne saldo kredytu musi zawierać
                        // jeszcze kapitał tej raty.
                        currentRemainingPrincipalDb = nextUnpaidDb.RemainingBalance.Value;

                        if (nextUnpaidDb.PrincipalAmount.HasValue)
                            currentRemainingPrincipalDb += nextUnpaidDb.PrincipalAmount.Value;
                    }
                    else
                    {
                        // fallback
                        currentRemainingPrincipalDb = Math.Max(0m, vm.Principal);
                    }
                }
                else
                {
                    // brak nieopłaconych rat = kredyt spłacony
                    currentRemainingPrincipalDb = 0m;
                }

                if (currentRemainingPrincipalDb < 0m)
                    currentRemainingPrincipalDb = 0m;

                // ===== MANUAL OVERRIDE nadal ma priorytet dla raty / liczby miesięcy =====
                if (vm.OverrideMonthlyPayment.HasValue || vm.OverrideRemainingMonths.HasValue)
                {
                    DateTime? nextDate = nextPaymentDateDb;
                    if (!nextDate.HasValue)
                        nextDate = LoansService.GetNextDueDate(today, vm.PaymentDay, vm.StartDate).Date;

                    int? remainingInstallments = vm.OverrideRemainingMonths.HasValue
                        ? vm.OverrideRemainingMonths.Value
                        : (remainingInstallmentsDb > 0 ? remainingInstallmentsDb : null);

                    decimal? remainingPrincipal = Math.Max(0m, vm.Principal);

                    decimal? nextPaymentAmount = null;
                    decimal? nextCap = nextPaymentPrincipalDb;
                    decimal? nextInt = nextPaymentInterestDb;

                    if (vm.OverrideMonthlyPayment.HasValue)
                    {
                        nextPaymentAmount = vm.OverrideMonthlyPayment.Value;
                    }
                    else if (nextPaymentAmountDb.HasValue)
                    {
                        nextPaymentAmount = nextPaymentAmountDb.Value;
                    }
                    else if (vm.OverrideRemainingMonths.HasValue && vm.OverrideRemainingMonths.Value > 0)
                    {
                        nextPaymentAmount = LoansService.CalculateMonthlyPayment(
                            principal: remainingPrincipal ?? 0m,
                            annualRatePercent: vm.InterestRate,
                            termMonths: vm.OverrideRemainingMonths.Value);
                    }

                    vm.ApplyScheduleSnapshot(
                        originalPrincipal: originalPrincipalDb,
                        remainingPrincipal: remainingPrincipal,
                        nextPaymentAmount: nextPaymentAmount,
                        nextPaymentDate: nextDate,
                        remainingInstallments: remainingInstallments,
                        nextPaymentPrincipalPart: nextCap,
                        nextPaymentInterestPart: nextInt);

                    return;
                }

                // ===== normalny tryb z DB =====
                if (orderedDb.Count == 0)
                {
                    vm.ClearScheduleSnapshot();
                    return;
                }

                vm.ApplyScheduleSnapshot(
                    originalPrincipal: originalPrincipalDb,
                    remainingPrincipal: currentRemainingPrincipalDb,
                    nextPaymentAmount: nextPaymentAmountDb,
                    nextPaymentDate: nextPaymentDateDb,
                    remainingInstallments: remainingInstallmentsDb,
                    nextPaymentPrincipalPart: nextPaymentPrincipalDb,
                    nextPaymentInterestPart: nextPaymentInterestDb);
            }
            catch
            {
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

                string originalFileName = Path.GetFileName(selectedPath);

                string destPath = CopyLoanScheduleToAppData(loanId, selectedPath);
                DatabaseService.SetLoanSchedulePath(loanId, _userId, destPath);

                _parsedSchedules.Remove(loanId);

                ImportScheduleIntoDb(loanId, destPath, originalFileName);

                ApplyScheduleSnapshotToVm(loanVm);

                DatabaseService.NotifyDataChanged();
                RefreshKpisAndLists();

                ToastService.Success($"Harmonogram „{originalFileName}” został załączony i zaimportowany.");
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

        private static (DateTime from, DateTime to) GetMonthRange(DateTime anyDayInMonth)
        {
            var first = new DateTime(anyDayInMonth.Year, anyDayInMonth.Month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            return (first.Date, last.Date);
        }

        /// <summary>
        /// Status raty "dla bieżącego miesiąca" liczymy po DueDate wpadającym w miesiąc,
        /// a nie po tym czy termin minął.
        /// Zwraca:
        /// - hasInstallmentInMonth: czy w ogóle jest rata w tym miesiącu
        /// - isPaidInMonth: czy ta rata (lub raty) mają Status=1
        /// - isOverdue: czy jest rata w miesiącu, która jest po DueDate i nadal Status=0
        /// - dueDate: najwcześniejszy DueDate raty w miesiącu (do tekstu "do zapłaty dnia ...")
        /// </summary>
        private static (bool hasInstallmentInMonth, bool isPaidInMonth, bool isOverdue, DateTime? dueDate)
            UpdatePaidStatusForCurrentMonth(int userId, int loanId, DateTime month)
        {
            if (userId <= 0 || loanId <= 0)
                return (false, false, false, null);

            var (from, to) = GetMonthRange(month);

            // Źródło prawdy: DB, a nie heurystyki "czy termin minął"
            var installments = DatabaseService.GetInstallmentsByDueDate(userId, loanId, from, to);


            if (installments == null || installments.Count == 0)
                return (false, false, false, null);

            // Zwykle 1 rata/miesiąc, ale obsługujemy też >1
            var due = installments
                .Where(x => x.DueDate != DateTime.MinValue)
                .OrderBy(x => x.DueDate)
                .Select(x => (DateTime?)x.DueDate.Date)
                .FirstOrDefault();

            bool paid = installments.Any(x => x.Status == 1);

            // zaległość: istnieje rata w miesiącu, jest po DueDate i nadal nieopłacona
            var today = DateTime.Today;
            bool overdue = installments.Any(x =>
                x.Status == 0 &&
                x.DueDate != DateTime.MinValue &&
                x.DueDate.Date < today);

            return (true, paid, overdue, due);
        }

        private void RefreshKpisAndLists()
        {
            var loans = _loans.OfType<LoanCardVm>().ToList();

            ApplyScheduleSnapshotsToLoanVms();
            LoadScheduleHistoryCombo();
            UpdateKpiTiles();

            if (!loans.Any())
            {
                if (FindName("MonthlyLoansPaidStatus") is TextBlock tm0) tm0.Text = "";
                if (FindName("Analysis1PaidStatus") is TextBlock s10) s10.Text = "";
                if (FindName("Analysis2PaidStatus") is TextBlock s20) s20.Text = "";
                return;
            }

            var snapshot = BuildPortfolioKpiSnapshot(loans);

            string statusText;
            if (snapshot.HasOverdue)
            {
                statusText = "Masz zaległe raty w zapisanych harmonogramach.";
            }
            else if (snapshot.NearestDueDate.HasValue)
            {
                statusText = $"Najbliższa rata: {snapshot.NearestDueDate.Value:dd.MM.yyyy}";
            }
            else
            {
                statusText = "Brak przyszłych rat w zapisanych harmonogramach.";
            }

            if (FindName("MonthlyLoansPaidStatus") is TextBlock tm) tm.Text = statusText;
            if (FindName("Analysis1PaidStatus") is TextBlock t1) t1.Text = statusText;
            if (FindName("Analysis2PaidStatus") is TextBlock t2) t2.Text = statusText;
        }

        private sealed class PortfolioKpiSnapshot
        {
            public decimal MonthlyTotal { get; set; }
            public decimal MonthlyPrincipal { get; set; }
            public decimal MonthlyInterest { get; set; }

            public decimal RemainingCost { get; set; }

            public decimal PaidTotal { get; set; }
            public decimal PaidPrincipal { get; set; }
            public decimal PaidInterest { get; set; }

            public DateTime? NearestDueDate { get; set; }
            public bool HasOverdue { get; set; }
        }

        private PortfolioKpiSnapshot BuildPortfolioKpiSnapshot(List<LoanCardVm> loans)
        {
            var snapshot = new PortfolioKpiSnapshot();
            var today = DateTime.Today;

            foreach (var vm in loans)
            {
                var rows = DatabaseService.GetLoanInstallments(_userId, vm.Id)
                           ?? new List<DatabaseService.LoanInstallmentDb>();

                // już spłacone
                var paidRows = rows
                    .Where(x => x.Status == 1)
                    .ToList();

                snapshot.PaidTotal += paidRows.Sum(x => x.TotalAmount);
                snapshot.PaidPrincipal += paidRows.Sum(x => x.PrincipalAmount ?? 0m);
                snapshot.PaidInterest += paidRows.Sum(x => x.InterestAmount ?? 0m);

                // zaległości
                if (rows.Any(x => x.Status == 0 &&
                                  x.DueDate != DateTime.MinValue &&
                                  x.DueDate.Date < today))
                {
                    snapshot.HasOverdue = true;
                }

                // najbliższa nieopłacona rata tego kredytu
                var nextUnpaid = rows
                    .Where(x => x.Status == 0 && x.DueDate != DateTime.MinValue)
                    .OrderBy(x => x.DueDate.Date)
                    .ThenBy(x => x.InstallmentNo)
                    .FirstOrDefault();

                if (nextUnpaid != null)
                {
                    snapshot.MonthlyTotal += nextUnpaid.TotalAmount;
                    snapshot.MonthlyPrincipal += nextUnpaid.PrincipalAmount ?? 0m;
                    snapshot.MonthlyInterest += nextUnpaid.InterestAmount ?? 0m;

                    snapshot.RemainingCost += rows
                        .Where(x => x.Status == 0)
                        .Sum(x => x.TotalAmount);

                    if (!snapshot.NearestDueDate.HasValue || nextUnpaid.DueDate.Date < snapshot.NearestDueDate.Value)
                        snapshot.NearestDueDate = nextUnpaid.DueDate.Date;

                    continue;
                }

                // fallback dla kredytu bez harmonogramu
                int monthsLeft = GetRemainingMonths(vm);
                if (monthsLeft <= 0 || vm.DisplayRemainingPrincipal <= 0m)
                    continue;

                decimal monthly = vm.OverrideMonthlyPayment
                    ?? LoansService.CalculateMonthlyPayment(vm.DisplayRemainingPrincipal, vm.InterestRate, monthsLeft);

                var breakdown = LoansService.CalculateFirstInstallmentBreakdown(
                    vm.DisplayRemainingPrincipal,
                    vm.InterestRate,
                    monthsLeft);

                snapshot.MonthlyTotal += monthly;
                snapshot.MonthlyPrincipal += breakdown.PrincipalPart;
                snapshot.MonthlyInterest += breakdown.InterestPart;
                snapshot.RemainingCost += monthly * monthsLeft;

                var nextDue = LoansService.GetNextDueDate(today, vm.PaymentDay, vm.StartDate).Date;
                if (!snapshot.NearestDueDate.HasValue || nextDue < snapshot.NearestDueDate.Value)
                    snapshot.NearestDueDate = nextDue;
            }

            return snapshot;
        }

        private sealed class ScheduleHistoryItemVm
        {
            public int ScheduleId { get; init; }
            public int LoanId { get; init; }
            public string LoanName { get; init; } = "";
            public string SourceName { get; init; } = "";
            public DateTime ImportedAt { get; init; }

            public string DisplayText =>
                string.IsNullOrWhiteSpace(SourceName)
                    ? $"harmonogram_{ScheduleId}.csv"
                    : SourceName;

            public override string ToString() => DisplayText;
        }

        private void LoadScheduleHistoryCombo()
        {
            try
            {
                _savedSchedules.Clear();

                var items = DatabaseService.GetLoanScheduleHistory(_userId)
                    .OrderByDescending(x => x.ImportedAt)
                    .Select(x => new ScheduleHistoryItemVm
                    {
                        ScheduleId = x.Id,
                        LoanId = x.LoanId,
                        LoanName = x.LoanName ?? $"Kredyt #{x.LoanId}",
                        SourceName = !string.IsNullOrWhiteSpace(x.SourceName)
                            ? x.SourceName!
                            : (!string.IsNullOrWhiteSpace(x.SchedulePath)
                                ? Path.GetFileName(x.SchedulePath)
                                : $"harmonogram_{x.Id}.csv"),
                        ImportedAt = x.ImportedAt
                    })
                    .ToList();

                foreach (var item in items)
                    _savedSchedules.Add(item);

                if (SavedSchedulesCombo != null)
                {
                    SavedSchedulesCombo.ItemsSource = _savedSchedules;
                    SavedSchedulesCombo.DisplayMemberPath = nameof(ScheduleHistoryItemVm.DisplayText);

                    if (SavedSchedulesCombo.SelectedIndex < 0 && _savedSchedules.Count > 0)
                        SavedSchedulesCombo.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadScheduleHistoryCombo failed: " + ex);
            }
        }

        private static LoanInstallmentRow ToLoanInstallmentRow(DatabaseService.LoanInstallmentDb x)
        {
            return new LoanInstallmentRow
            {
                Date = x.DueDate.Date,
                Total = x.TotalAmount,
                Principal = x.PrincipalAmount,
                Interest = x.InterestAmount,
                Remaining = x.RemainingBalance,
                InstallmentNo = x.InstallmentNo > 0 ? x.InstallmentNo : (int?)null
            };
        }

        private void OpenSavedSchedule_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SavedSchedulesCombo.SelectedItem is not ScheduleHistoryItemVm item)
                {
                    ToastService.Info("Wybierz harmonogram z listy.");
                    return;
                }

                var rowsDb = DatabaseService.GetInstallmentsBySchedule(_userId, item.ScheduleId)
                            ?? new List<DatabaseService.LoanInstallmentDb>();

                if (rowsDb.Count == 0)
                {
                    ToastService.Info("Wybrany harmonogram nie ma zapisanych rat.");
                    return;
                }

                var rows = rowsDb
                    .OrderBy(x => x.InstallmentNo)
                    .ThenBy(x => x.DueDate)
                    .Select(ToLoanInstallmentRow)
                    .ToList();

                var dlg = new LoanScheduleDialog(
                    $"{item.LoanName} – {item.SourceName}",
                    rows,
                    _userId,
                    item.LoanId)
                {
                    Owner = GetOwnerWindow()
                };

                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się otworzyć zapisanego harmonogramu: " + ex.Message);
            }
        }

        private readonly ObservableCollection<ScheduleHistoryItemVm> _savedSchedules = new();

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
            var snapshot = BuildPortfolioKpiSnapshot(loans);

            decimal totalDebt = loans.Sum(x => x.DisplayRemainingPrincipal);

            if (FindName("TotalLoansTileAmount") is TextBlock tbTotal)
                tbTotal.Text = totalDebt.ToString("N2") + " zł";

            if (FindName("MonthlyLoansTileAmount") is TextBlock tbMonthly)
                tbMonthly.Text = snapshot.MonthlyTotal.ToString("N2") + " zł";

            if (FindName("Analysis1Value") is TextBlock t1)
                t1.Text = snapshot.MonthlyPrincipal.ToString("N2") + " zł";

            if (FindName("Analysis2Value") is TextBlock t2)
                t2.Text = snapshot.MonthlyInterest.ToString("N2") + " zł";

            if (FindName("Analysis3Value") is TextBlock t3)
                t3.Text = snapshot.RemainingCost.ToString("N2") + " zł";

            if (FindName("TotalPaidLoansTileAmount") is TextBlock tp1)
                tp1.Text = snapshot.PaidTotal.ToString("N2") + " zł";

            if (FindName("TotalPaidPrincipalTileAmount") is TextBlock tp2)
                tp2.Text = snapshot.PaidPrincipal.ToString("N2") + " zł";

            if (FindName("TotalPaidInterestTileAmount") is TextBlock tp3)
                tp3.Text = snapshot.PaidInterest.ToString("N2") + " zł";
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

                    if (!string.IsNullOrWhiteSpace(dlg.AttachedSchedulePath) && File.Exists(dlg.AttachedSchedulePath))
                    {
                        string originalFileName = Path.GetFileName(dlg.AttachedSchedulePath);
                        var dest = CopyLoanScheduleToAppData(loan.Id, dlg.AttachedSchedulePath!);

                        DatabaseService.SetLoanSchedulePath(loan.Id, _userId, dest);
                        _parsedSchedules.Remove(loan.Id);

                        ImportScheduleIntoDb(loan.Id, dest, originalFileName);
                    }

                    DatabaseService.NotifyDataChanged();

                    ToastService.Success("Kredyt dodany.");
                    LoadLoans();
                    ApplyScheduleSnapshotsToLoanVms();
                    RefreshKpisAndLists();
                    SyncLoanPlannedExpenses(loan.Id);
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

                    if (!string.IsNullOrWhiteSpace(dlg.AttachedSchedulePath) && File.Exists(dlg.AttachedSchedulePath))
                    {
                        string originalFileName = Path.GetFileName(dlg.AttachedSchedulePath);
                        var dest = CopyLoanScheduleToAppData(vm.Id, dlg.AttachedSchedulePath!);

                        DatabaseService.SetLoanSchedulePath(vm.Id, _userId, dest);
                        _parsedSchedules.Remove(vm.Id);

                        ImportScheduleIntoDb(vm.Id, dest, originalFileName);
                    }

                    DatabaseService.NotifyDataChanged();

                    ToastService.Success("Kredyt zaktualizowany.");
                    LoadLoans();
                    ApplyScheduleSnapshotsToLoanVms();
                    RefreshKpisAndLists();
                    SyncLoanPlannedExpenses(vm.Id);
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

            var totalPaid = dlg.Amount;
            var capitalPart = dlg.CapitalPaid;
            var interestPart = totalPaid - capitalPart;

            if (totalPaid <= 0m || capitalPart <= 0m || capitalPart > totalPaid)
            {
                ToastService.Error("Nieprawidłowe dane nadpłaty.");
                return;
            }

            try
            {
                // 1) Aktualizujemy saldo kredytu WYŁĄCZNIE o kapitał
                var newPrincipal = vm.Principal - capitalPart;
                if (newPrincipal < 0m) newPrincipal = 0m;

                // 2) Jeśli tryb MANUAL ma zmienić ratę/okres, musimy to zapisać w LoanModel (NOWE pola w DB)
                //    (szczegóły migracji poniżej w pkt 4)
                decimal? overrideMonthlyPayment = null;
                int? overrideRemainingMonths = null;

                if (dlg.Mode == OverpayLoanDialog.OverpayMode.Manual)
                {
                    if (dlg.ManualLowerPayment && dlg.ManualNewPayment.HasValue)
                        overrideMonthlyPayment = dlg.ManualNewPayment.Value;

                    if (!dlg.ManualLowerPayment && dlg.ManualRemainingMonths.HasValue)
                        overrideRemainingMonths = dlg.ManualRemainingMonths.Value;
                }

                var loanToUpdate = new LoanModel
                {
                    Id = vm.Id,
                    UserId = _userId,
                    Name = vm.Name,
                    Principal = newPrincipal,
                    InterestRate = vm.InterestRate,
                    StartDate = vm.StartDate,
                    TermMonths = vm.TermMonths,
                    PaymentDay = vm.PaymentDay,

                    // ===== NOWE: override'y (A2) =====
                    OverrideMonthlyPayment = overrideMonthlyPayment,
                    OverrideRemainingMonths = overrideRemainingMonths
                };

                DatabaseService.UpdateLoan(loanToUpdate);

                // 3) Historia operacji
                try
                {
                    DatabaseService.InsertLoanOperation(_userId, new LoanOperationModel
                    {
                        LoanId = vm.Id,
                        Date = DateTime.Today,
                        Type = LoanOperationType.Overpayment,
                        TotalAmount = totalPaid,
                        CapitalPart = capitalPart,
                        InterestPart = interestPart,
                        RemainingPrincipal = newPrincipal
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("InsertLoanOperation failed: " + ex);
                }

                // 4) A1: CSV -> import jak “Załącz harmonogram”
                if (dlg.Mode == OverpayLoanDialog.OverpayMode.Csv)
                {
                    var selectedPath = dlg.AttachedSchedulePath!;
                    if (!File.Exists(selectedPath))
                    {
                        ToastService.Error("Wybrany plik CSV nie istnieje.");
                        return;
                    }

                    string destPath = CopyLoanScheduleToAppData(vm.Id, selectedPath);
                    DatabaseService.SetLoanSchedulePath(vm.Id, _userId, destPath);
                    _parsedSchedules.Remove(vm.Id);

                    // po CSV nadpisujemy manual override (bank jest źródłem prawdy)
                    try
                    {
                        var cleared = new LoanModel
                        {
                            Id = vm.Id,
                            UserId = _userId,
                            Name = vm.Name,
                            Principal = newPrincipal,
                            InterestRate = vm.InterestRate,
                            StartDate = vm.StartDate,
                            TermMonths = vm.TermMonths,
                            PaymentDay = vm.PaymentDay,
                            OverrideMonthlyPayment = null,
                            OverrideRemainingMonths = null
                        };
                        DatabaseService.UpdateLoan(cleared);
                    }
                    catch { }

                    ImportScheduleIntoDb(vm.Id, destPath, Path.GetFileName(selectedPath));

                    ToastService.Success($"Nadpłata {totalPaid:N2} zł zapisana + zaimportowano nowy harmonogram.");
                }
                else
                {
                    // A2: Manual
                    if (overrideMonthlyPayment.HasValue)
                        ToastService.Success($"Nadpłata {totalPaid:N2} zł zapisana. Nowa rata: {overrideMonthlyPayment.Value:N2} zł.");
                    else if (overrideRemainingMonths.HasValue)
                        ToastService.Success($"Nadpłata {totalPaid:N2} zł zapisana. Pozostało rat: {overrideRemainingMonths.Value}.");
                    else
                        ToastService.Success($"Nadpłata {totalPaid:N2} zł zapisana.");
                }

                // 5) Refresh UI
                LoadLoans();
                ApplyScheduleSnapshotsToLoanVms();
                RefreshKpisAndLists();
                DatabaseService.NotifyDataChanged();
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd podczas nadpłaty: " + ex.Message);
            }
        }


        private void ShowPaidHistory_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            var dlg = new Finly.Views.Dialogs.LoanPaidHistoryDialog(
                loanName: vm.Name,
                paidScheduleRows: Array.Empty<LoanInstallmentRow>(), // nie opieramy historii na aktualnym CSV
                userId: _userId,
                loanId: vm.Id)
            {
                Owner = GetOwnerWindow()
            };

            dlg.ShowDialog();
        }


        private void CardSchedule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            try
            {
                var history = DatabaseService.GetLoanScheduleHistory(_userId)
                    .Where(x => x.LoanId == vm.Id)
                    .OrderByDescending(x => x.ImportedAt)
                    .ThenByDescending(x => x.Id)
                    .ToList();

                var latest = history.FirstOrDefault();
                if (latest == null)
                {
                    ToastService.Info("Ten kredyt nie ma jeszcze zapisanego harmonogramu.");
                    return;
                }

                var dbRows = DatabaseService.GetInstallmentsBySchedule(_userId, latest.Id)
                            ?? new List<DatabaseService.LoanInstallmentDb>();

                if (dbRows.Count == 0)
                {
                    ToastService.Info("Brak zapisanych rat dla tego harmonogramu.");
                    return;
                }

                var rows = dbRows
                    .OrderBy(x => x.InstallmentNo)
                    .ThenBy(x => x.DueDate)
                    .Select(ToLoanInstallmentRow)
                    .ToList();

                var dlg = new LoanScheduleDialog(vm.Name, rows, _userId, vm.Id)
                {
                    Owner = GetOwnerWindow()
                };

                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się otworzyć harmonogramu: " + ex.Message);
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

        private void SyncLoanPlannedExpenses(int loanId)
        {
            // zakres: np. od dziś - 1 miesiąc (dla “bieżącego” miesiąca) do dziś + 2 lata
            var from = DateTime.Today.AddMonths(-1);
            var to = DateTime.Today.AddYears(2);

            DatabaseService.SyncLoanInstallmentsToPlannedExpenses(_userId, loanId, from, to);
        }

        private void ImportScheduleIntoDb(int loanId, string schedulePath, string? sourceName = null)
        {
            if (loanId <= 0)
                throw new ArgumentException("Nieprawidłowe loanId.");

            if (string.IsNullOrWhiteSpace(schedulePath))
                throw new ArgumentException("Nieprawidłowa ścieżka harmonogramu.");

            if (!File.Exists(schedulePath))
                throw new FileNotFoundException("Nie znaleziono pliku harmonogramu.", schedulePath);

            Finly.Models.PaymentKind paymentKind = Finly.Models.PaymentKind.FreeCash;
            int? paymentRefId = null;

            try
            {
                var loan = (DatabaseService.GetLoans(_userId) ?? new List<LoanModel>())
                    .FirstOrDefault(x => x.Id == loanId);

                if (loan != null)
                {
                    paymentKind = loan.PaymentKind;
                    paymentRefId = loan.PaymentRefId;
                }
            }
            catch
            {
            }

            if (paymentKind == Finly.Models.PaymentKind.FreeCash &&
                !paymentRefId.HasValue &&
                _loanAccounts.TryGetValue(loanId, out var accId))
            {
                paymentKind = Finly.Models.PaymentKind.BankAccount;
                paymentRefId = accId;
            }

            var parser = new LoanScheduleCsvParser();
            var parsed = parser.Parse(schedulePath)?.ToList() ?? new List<LoanInstallmentRow>();

            if (parsed.Count == 0)
                throw new InvalidOperationException("Plik CSV nie zawiera rat do importu.");

            int scheduleId = DatabaseService.InsertLoanSchedule(
                userId: _userId,
                loanId: loanId,
                sourceName: string.IsNullOrWhiteSpace(sourceName) ? Path.GetFileName(schedulePath) : sourceName,
                schedulePath: schedulePath,
                note: null
            );

            var ordered = parsed.OrderBy(x => x.Date).ToList();

            var dbRows = ordered
                .Select((x, idx) => new DatabaseService.LoanInstallmentDb
                {
                    UserId = _userId,
                    LoanId = loanId,
                    ScheduleId = scheduleId,
                    InstallmentNo = GetInstallmentNoOrFallback(x, idx),
                    DueDate = x.Date.Date,
                    TotalAmount = x.Total,
                    PrincipalAmount = (x.Principal.HasValue && x.Principal.Value >= 0m) ? x.Principal.Value : (decimal?)null,
                    InterestAmount = (x.Interest.HasValue && x.Interest.Value >= 0m) ? x.Interest.Value : (decimal?)null,
                    RemainingBalance = (x.Remaining.HasValue && x.Remaining.Value >= 0m) ? x.Remaining.Value : (decimal?)null,
                    Status = 0,
                    PaymentKind = (int)paymentKind,
                    PaymentRefId = paymentRefId
                })
                .ToList();

            DatabaseService.ReplaceFutureUnpaidInstallmentsFromSchedule(
                userId: _userId,
                loanId: loanId,
                scheduleId: scheduleId,
                rows: dbRows,
                from: DateTime.Today
            );

            var from = DateTime.Today;
            var to = from.AddMonths(18);

            DatabaseService.SyncLoanInstallmentsToPlannedExpenses(_userId, loanId, from, to);

            _parsedSchedules.Remove(loanId);
        }

        private static int GetInstallmentNoOrFallback(LoanInstallmentRow row, int indexOrderedByDate)
        {
            // Jeśli LoanInstallmentRow ma InstallmentNo (prop) — użyj go.
            // Jeśli nie ma / 0 / null — nadaj kolejno: 1..N
            try
            {
                var prop = typeof(LoanInstallmentRow).GetProperty("InstallmentNo");
                if (prop != null)
                {
                    var val = prop.GetValue(row);
                    if (val is int n && n > 0) return n;
                }
            }
            catch { }

            return indexOrderedByDate + 1;
        }

        private static (DateTime from, DateTime to) GetMonthRangeInclusive(DateTime today)
        {
            var from = new DateTime(today.Year, today.Month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            return (from, to);
        }

        private static string BuildMonthInstallmentStatusText(LoanInstallmentDb? inst)
        {
            if (inst == null || inst.DueDate == DateTime.MinValue) return "";

            var today = DateTime.Today.Date;

            if (inst.Status == 1)
                return "✓ Zapłacone w tym miesiącu";

            if (today <= inst.DueDate.Date)
                return $"Do zapłaty dnia {inst.DueDate:dd.MM}";

            return $"Zaległe (termin {inst.DueDate:dd.MM})";
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
