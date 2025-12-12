using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Finly.Models;
using Finly.Services;
using Finly.ViewModels;
using Finly.Views;

namespace Finly.Pages
{
    public partial class LoansPage : UserControl
    {
        private readonly ObservableCollection<object> _loans = new();
        private readonly int _userId;
        private LoanCardVm? _selectedVm;

        private readonly Dictionary<int, string> _loanScheduleFiles = new();
        private readonly Dictionary<int, List<LoanInstallmentRow>> _parsedSchedules = new();
        private string? _lastChosenSchedulePath;

        private List<BankAccountModel> _accounts = new();
        private readonly Dictionary<int, int> _loanAccounts = new();

        private enum LoanPanel
        {
            None,
            AddEdit,
            Schedule,
            Overpay,
            Sim
        }

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
            LoadLoans();
            LoadAccountsForLoanForm();
            RefreshKpisAndLists();
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

        private void LoadAccountsForLoanForm()
        {
            try
            {
                _accounts = DatabaseService.GetAccounts(_userId) ?? new List<BankAccountModel>();

                if (LoanAccountBox != null)
                {
                    LoanAccountBox.ItemsSource = _accounts;
                    LoanAccountBox.DisplayMemberPath = "AccountName";
                    LoanAccountBox.SelectedValuePath = "Id";
                }
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się załadować listy kont: " + ex.Message);
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

        private (decimal totalDebt, decimal monthlySum, decimal yearlySum, int maxRemainingMonths) CalculatePortfolioStats(List<LoanCardVm> loans)
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

                if (TryGetScheduleStats(vm,
                        out var nextAmount,
                        out _,
                        out remainingMonths,
                        out var yearSumFromSchedule))
                {
                    loanMonthly = nextAmount;
                    loanYearly = yearSumFromSchedule;
                }
                else
                {
                    if (vm.TermMonths > 0)
                    {
                        loanMonthly = LoanService.CalculateMonthlyPayment(vm.Principal, vm.InterestRate, vm.TermMonths);
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

            // Prefer cache
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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TryGetScheduleStats parse error: {ex}");
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
                    Label = "Średnie oprocentowanie portfela (ważone saldem):",
                    Value = $"{weightedRate:N2} %"
                });

                insights.Add(new LoanInsightVm
                {
                    Label = "Szacowane odsetki w kolejne 30 dni (dla obecnych sald):",
                    Value = $"{interest30:N2} zł"
                });

                insights.Add(new LoanInsightVm
                {
                    Label = "Prognozowana łączna kwota rat w ciągu roku:",
                    Value = $"{yearlySum:N2} zł"
                });

                insights.Add(new LoanInsightVm
                {
                    Label = "Całkowity czas spłaty kredytów:",
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

        private void ShowPanel(LoanPanel panel)
        {
            if (FormBorder == null) return;

            FormBorder.Visibility = panel == LoanPanel.None
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (AddEditPanel != null)
                AddEditPanel.Visibility = panel == LoanPanel.AddEdit ? Visibility.Visible : Visibility.Collapsed;
            if (SchedulePanel != null)
                SchedulePanel.Visibility = panel == LoanPanel.Schedule ? Visibility.Visible : Visibility.Collapsed;
            if (OverpayPanel != null)
                OverpayPanel.Visibility = panel == LoanPanel.Overpay ? Visibility.Visible : Visibility.Collapsed;
            if (SimPanel != null)
                SimPanel.Visibility = panel == LoanPanel.Sim ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowAddForm()
        {
            _selectedVm = null;

            try { if (AddEditHeaderText != null) AddEditHeaderText.Text = "Dodaj kredyt"; } catch { }
            try { LoanFormMessage.Text = string.Empty; } catch { }
            try { LoanNameBox.Text = ""; } catch { }
            try { LoanPrincipalBox.Text = ""; } catch { }
            try { LoanInterestBox.Text = ""; } catch { }
            try { LoanTermBox.Text = ""; } catch { }
            try { LoanStartDatePicker.SelectedDate = DateTime.Today; } catch { }

            try
            {
                if (LoanPaymentDayBox != null) LoanPaymentDayBox.SelectedIndex = 0;
                if (LoanAccountBox != null) LoanAccountBox.SelectedIndex = -1;
            }
            catch { }

            ComputeAndShowMonthlyBreakdown();
            ShowPanel(LoanPanel.AddEdit);
        }

        private void AddLoanCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            ShowAddForm();
        }

        private void SaveLoan_Click(object sender, RoutedEventArgs e)
        {
            var name = (LoanNameBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                LoanFormMessage.Text = "Podaj nazwę kredytu.";
                return;
            }

            if (!decimal.TryParse((LoanPrincipalBox.Text ?? "").Trim(), out var principal))
                principal = 0m;
            if (!decimal.TryParse((LoanInterestBox.Text ?? "").Trim(), out var interest))
                interest = 0m;
            if (!int.TryParse((LoanTermBox.Text ?? "").Trim(), out var term))
                term = 0;

            var start = LoanStartDatePicker.SelectedDate ?? DateTime.Today;

            int paymentDay = 0;
            try
            {
                if (LoanPaymentDayBox?.SelectedItem is ComboBoxItem ci && ci.Tag != null)
                {
                    if (!int.TryParse(ci.Tag.ToString(), out paymentDay))
                        paymentDay = 0;
                }
            }
            catch { paymentDay = 0; }

            int? selectedAccountId = null;
            if (LoanAccountBox?.SelectedItem is BankAccountModel acc)
                selectedAccountId = acc.Id;

            try
            {
                var loan = new LoanModel
                {
                    UserId = _userId,
                    Name = name,
                    Principal = principal,
                    InterestRate = interest,
                    StartDate = start,
                    TermMonths = term,
                    PaymentDay = paymentDay
                };

                if (_selectedVm != null)
                {
                    loan.Id = _selectedVm.Id;
                    DatabaseService.UpdateLoan(loan);
                    ToastService.Success("Kredyt zaktualizowany.");
                }
                else
                {
                    var id = DatabaseService.InsertLoan(loan);
                    loan.Id = id;
                    ToastService.Success("Kredyt dodany.");
                }

                if (selectedAccountId.HasValue) _loanAccounts[loan.Id] = selectedAccountId.Value;
                else _loanAccounts.Remove(loan.Id);

                if (!string.IsNullOrWhiteSpace(_lastChosenSchedulePath))
                {
                    _loanScheduleFiles[loan.Id] = _lastChosenSchedulePath;
                    _lastChosenSchedulePath = null;
                }

                LoadLoans();
                RefreshKpisAndLists();
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd dodawania kredytu: " + ex.Message);
            }
            finally
            {
                ShowPanel(LoanPanel.None);
                _selectedVm = null;
            }
        }

        private void CancelLoan_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel(LoanPanel.None);
        }

        private void LoanFormField_Changed(object sender, TextChangedEventArgs e)
        {
            ComputeAndShowMonthlyBreakdown();
        }

        private void ComputeAndShowMonthlyBreakdown()
        {
            if (!decimal.TryParse((LoanPrincipalBox.Text ?? "").Replace(" ", ""), out var principal))
                principal = 0m;
            if (!decimal.TryParse((LoanInterestBox.Text ?? "").Replace(" ", ""), out var annualRate))
                annualRate = 0m;
            if (!int.TryParse((LoanTermBox.Text ?? "").Replace(" ", ""), out var months))
                months = 0;

            if (principal <= 0 || months <= 0)
            {
                MonthlyPaymentText.Text = "0,00 zł";
                FirstPrincipalText.Text = "0,00 zł";
                FirstInterestText.Text = "0,00 zł";
                return;
            }

            var payment = LoanService.CalculateMonthlyPayment(principal, annualRate, months);
            var (interestFirst, capitalFirst) =
                LoanService.CalculateFirstInstallmentBreakdown(principal, annualRate, months);

            MonthlyPaymentText.Text = payment.ToString("N2") + " zł";
            FirstPrincipalText.Text = capitalFirst.ToString("N2") + " zł";
            FirstInterestText.Text = interestFirst.ToString("N2") + " zł";
        }

        private void ChooseSchedule_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Pliki CSV|*.csv|Wszystkie pliki|*.*"
            };

            var ok = dlg.ShowDialog();
            if (ok == true)
            {
                _lastChosenSchedulePath = dlg.FileName;
                ScheduleFileNameText.Text = Path.GetFileName(dlg.FileName);
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

            if (string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var parser = new LoanScheduleCsvParser();
                    var rows = parser.Parse(path);

                    _loanScheduleFiles[vm.Id] = path;
                    _parsedSchedules[vm.Id] = rows.ToList();

                    ScheduleList.ItemsSource = rows.Select(r => r.ToString()).ToList();
                    _selectedVm = vm;
                    ShowPanel(LoanPanel.Schedule);

                    ToastService.Success("Harmonogram spłat został załączony i odczytany dla kredytu: " + vm.Name);

                    LoadLoans();
                    RefreshKpisAndLists();
                }
                catch (Exception ex)
                {
                    ToastService.Error("Nie udało się odczytać harmonogramu. Sprawdź format pliku CSV.");
                    System.Diagnostics.Debug.WriteLine("LoanScheduleCsvParser error: " + ex);
                }

                return;
            }

            _loanScheduleFiles[vm.Id] = path;
            ToastService.Success("Harmonogram spłat został załączony do kredytu: " + vm.Name);

            LoadLoans();
            RefreshKpisAndLists();
        }

        private void CardDetails_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            _selectedVm = vm;

            try { if (AddEditHeaderText != null) AddEditHeaderText.Text = $"Edycja kredytu \"{vm.Name}\""; } catch { }

            LoanNameBox.Text = vm.Name;
            LoanPrincipalBox.Text = vm.Principal.ToString(CultureInfo.InvariantCulture);
            LoanInterestBox.Text = vm.InterestRate.ToString(CultureInfo.InvariantCulture);
            LoanTermBox.Text = vm.TermMonths.ToString(CultureInfo.InvariantCulture);
            LoanStartDatePicker.SelectedDate = vm.StartDate;

            try
            {
                if (LoanPaymentDayBox != null)
                {
                    int pd = vm.PaymentDay;
                    for (int i = 0; i < LoanPaymentDayBox.Items.Count; i++)
                    {
                        if (LoanPaymentDayBox.Items[i] is ComboBoxItem ci &&
                            ci.Tag != null &&
                            int.TryParse(ci.Tag.ToString(), out var tagVal) &&
                            tagVal == pd)
                        {
                            LoanPaymentDayBox.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (LoanAccountBox != null)
                {
                    if (_loanAccounts.TryGetValue(vm.Id, out var accId))
                        LoanAccountBox.SelectedItem = _accounts.FirstOrDefault(a => a.Id == accId);
                    else
                        LoanAccountBox.SelectedIndex = -1;
                }
            }
            catch { }

            ComputeAndShowMonthlyBreakdown();
            ShowPanel(LoanPanel.AddEdit);
        }

        private void EditLoan_Click(object sender, RoutedEventArgs e) => CardDetails_Click(sender, e);

        private void CardAddPayment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            _selectedVm = vm;
            OverpayAmountBox.Text = "";
            OverpayResult.Text = string.Empty;
            ShowPanel(LoanPanel.Overpay);
        }

        private void CardSchedule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            _selectedVm = vm;

            if (!_loanScheduleFiles.TryGetValue(vm.Id, out var path) || string.IsNullOrWhiteSpace(path))
            {
                ToastService.Error("Nie załączyłaś jeszcze harmonogramu spłaty rat Twojego kredytu.");
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
                var ext = Path.GetExtension(path);
                if (!string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
                {
                    ToastService.Error("Aktualnie aplikacja analizuje harmonogram tylko z pliku CSV. PDF dodamy w kolejnym kroku.");
                    return;
                }

                if (_parsedSchedules.TryGetValue(vm.Id, out var cached) && cached != null && cached.Count > 0)
                {
                    ScheduleList.ItemsSource = cached.Select(r => r.ToString()).ToList();
                    ShowPanel(LoanPanel.Schedule);
                    return;
                }

                var parser = new LoanScheduleCsvParser();
                var rows = parser.Parse(path).ToList();
                _parsedSchedules[vm.Id] = rows;

                ScheduleList.ItemsSource = rows.Select(r => r.ToString()).ToList();
                ShowPanel(LoanPanel.Schedule);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się odczytać harmonogramu. Sprawdź format pliku CSV.");
                System.Diagnostics.Debug.WriteLine("LoanScheduleCsvParser error: " + ex);
            }
        }

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
            ShowPanel(LoanPanel.None);
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

        private void OverpaySave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null)
            {
                OverpayResult.Text = "Wybierz najpierw kredyt (kliknij kartę).";
                OverpayResult.Foreground = Brushes.IndianRed;
                return;
            }

            if (!decimal.TryParse((OverpayAmountBox.Text ?? "").Replace(" ", ""), out var amt) || amt <= 0)
            {
                OverpayResult.Text = "Podaj poprawną kwotę nadpłaty.";
                OverpayResult.Foreground = Brushes.IndianRed;
                return;
            }

            int? accountId = null;
            string? accountName = null;
            if (_loanAccounts.TryGetValue(_selectedVm.Id, out var accId))
            {
                accountId = accId;
                accountName = _accounts.FirstOrDefault(a => a.Id == accId)?.AccountName;
            }

            try
            {
                var today = DateTime.Today;

                var lastDue = GetPreviousDueDate(today, _selectedVm.PaymentDay, _selectedVm.StartDate);

                var interest = LoanMathService.CalculateInterest(
                    _selectedVm.Principal,
                    _selectedVm.InterestRate,
                    lastDue,
                    today);

                if (interest < 0) interest = 0;

                var principalPart = amt - interest;
                if (principalPart < 0) principalPart = 0;

                var newPrincipal = _selectedVm.Principal - principalPart;
                if (newPrincipal < 0) newPrincipal = 0;

                var loanToUpdate = new LoanModel
                {
                    Id = _selectedVm.Id,
                    UserId = _selectedVm.UserId,
                    Name = _selectedVm.Name,
                    Principal = newPrincipal,
                    InterestRate = _selectedVm.InterestRate,
                    StartDate = _selectedVm.StartDate,
                    TermMonths = _selectedVm.TermMonths,
                    PaymentDay = _selectedVm.PaymentDay
                };

                DatabaseService.UpdateLoan(loanToUpdate);

                var newMonthly = LoanService.CalculateMonthlyPayment(
                    newPrincipal,
                    _selectedVm.InterestRate,
                    _selectedVm.TermMonths);

                var accInfo = accountName != null ? $" z konta \"{accountName}\"" : "";
                OverpayResult.Text =
                    $"Nadpłata: {amt:N2} zł{accInfo}.\n" +
                    $"Odsetki narosłe od poprzedniego terminu raty ({lastDue:dd.MM.yyyy}) do dziś: {interest:N2} zł.\n" +
                    $"Część kapitałowa nadpłaty: {principalPart:N2} zł.\n" +
                    $"Nowy stan kapitału: {newPrincipal:N2} zł.\n" +
                    $"Szacowana nowa rata (przy tej samej liczbie rat): {newMonthly:N2} zł.";
                OverpayResult.Foreground = Brushes.Green;

                LoadLoans();
                RefreshKpisAndLists();
            }
            catch (Exception ex)
            {
                OverpayResult.Text = "Błąd podczas zapisu nadpłaty: " + ex.Message;
                OverpayResult.Foreground = Brushes.IndianRed;
            }
        }

        private void ShowSimPanel_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LoanCardVm vm)
                return;

            _selectedVm = vm;
            SimExtraBox.Text = "";
            SimResult.Text = "";
            ShowPanel(LoanPanel.Sim);
        }

        private void SimulateInline_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null)
            {
                SimResult.Text = "Najpierw wybierz kredyt (kliknij kartę).";
                return;
            }

            if (!decimal.TryParse((SimExtraBox.Text ?? "").Replace(" ", ""), out var extra) || extra <= 0)
            {
                SimResult.Text = "Podaj poprawną kwotę jednorazowej nadpłaty.";
                return;
            }

            var vm = _selectedVm;

            var before = LoanService.CalculateMonthlyPayment(vm.Principal, vm.InterestRate, vm.TermMonths);
            var newPrincipal = Math.Max(0m, vm.Principal - extra);
            var after = LoanService.CalculateMonthlyPayment(newPrincipal, vm.InterestRate, vm.TermMonths);

            var diff = before - after;

            SimResult.Text =
                $"Przy jednorazowej nadpłacie {extra:N2} zł:\n" +
                $"- obecna rata: {before:N2} zł\n" +
                $"- nowa rata (ta sama liczba rat): {after:N2} zł\n" +
                $"- różnica: {diff:N2} zł miesięcznie.";
        }

        private static DateTime GetPreviousDueDate(DateTime today, int paymentDay, DateTime startDate)
        {
            if (paymentDay <= 0)
                return today.Date.AddMonths(-1);

            int daysInThisMonth = DateTime.DaysInMonth(today.Year, today.Month);
            int day = Math.Min(paymentDay, daysInThisMonth);
            var thisDue = new DateTime(today.Year, today.Month, day);

            if (today.Date >= thisDue.Date)
            {
                if (thisDue.Date < startDate.Date)
                    return startDate.Date;
                return thisDue.Date;
            }

            var prevMonthFirst = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            int daysInPrevMonth = DateTime.DaysInMonth(prevMonthFirst.Year, prevMonthFirst.Month);
            day = Math.Min(paymentDay, daysInPrevMonth);
            var prevDue = new DateTime(prevMonthFirst.Year, prevMonthFirst.Month, day);

            if (prevDue.Date < startDate.Date)
                return startDate.Date;

            return prevDue.Date;
        }
    }
}
