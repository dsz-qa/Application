using Finly.Models;
using Finly.ViewModels;
using Finly.Services;
using LoanScheduleRow = Finly.Services.LoanScheduleRow;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using Finly.Views;
using System.Collections.Generic;
using System.IO;



namespace Finly.Pages
{
    public partial class LoansPage : UserControl
    {
        private readonly ObservableCollection<object> _loans = new();
        private readonly int _userId;
        private LoanCardVm? _selectedVm;

        // mapowanie loanId -> ścieżka pliku harmonogramu (RAM)
        private readonly Dictionary<int, string> _loanScheduleFiles = new();
        private string? _lastChosenSchedulePath;

        // Typ aktualnie pokazywanego panelu na dole
        private enum LoanPanel
        {
            None,
            AddEdit,
            Schedule,
            Overpay,
            Sim
        }

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
            LoadLoans();
            RefreshKpisAndLists();
        }

        private void LoadLoans()
        {
            _loans.Clear();
            var list = DatabaseService.GetLoans(_userId) ?? new System.Collections.Generic.List<LoanModel>();
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

            _loans.Add(new AddLoanTile());

            UpdateKpiTiles();
        }

        private void RefreshKpisAndLists()
        {
            UpdateKpiTiles();

            decimal totalDebt = _loans.OfType<LoanCardVm>().Sum(x => x.Principal);
            var totalDebtTb = FindName("TotalDebtText") as TextBlock;
            if (totalDebtTb != null) totalDebtTb.Text = totalDebt.ToString("N0") + " zł";

            // Simple monthly payment estimate: sum of principal / remaining months
            decimal monthly = 0m;
            foreach (var l in _loans.OfType<LoanCardVm>())
            {
                var monthsElapsed = (DateTime.Today.Year - l.StartDate.Year) * 12 + DateTime.Today.Month - l.StartDate.Month;
                var monthsLeft = Math.Max(1, l.TermMonths - monthsElapsed);
                monthly += monthsLeft > 0 ? Math.Round(l.Principal / monthsLeft, 2) : l.Principal;
            }

            // Average monthly over12 months (simple)
            var avg12 = _loans.OfType<LoanCardVm>().Sum(x => x.Principal) / 12m;

            // Percent of loans that are fully paid (Principal ==0 for simplicity)
            var loanCount = _loans.OfType<LoanCardVm>().Count();
            var paidPct = loanCount == 0 ? 0 : (double)_loans.OfType<LoanCardVm>().Count(x => x.Principal <= 0) / loanCount * 100.0;

            // Build upcoming payments (dummy): next payment next month for each loan
            var upcoming = new ObservableCollection<object>();
            foreach (var l in _loans.OfType<LoanCardVm>().OrderBy(x => x.NextPaymentDate))
            {
                upcoming.Add(new { DateStr = l.NextPaymentDate.ToString("dd.MM"), LoanName = l.Name, AmountStr = l.NextPayment.ToString("N0") + " zł" });
            }
            UpcomingPaymentsList.ItemsSource = upcoming;

            // Insights (simple heuristics)
            var insights = new ObservableCollection<string>();
            if (totalDebt > 0)
            {
                var snapshot = DatabaseService.GetMoneySnapshot(_userId)?.Total ?? 1m;
                insights.Add($"Suma zadłużenia: {totalDebt:N0} zł");
                insights.Add($"Szacunkowa miesięczna rata (suma): {monthly:N0} zł");
                insights.Add($"Średnia miesięczna (12m): {avg12:N0} zł");
                insights.Add($"Pozytywny / negatywny wskaźnik spłat: {((int)paidPct)}% spłacone (uproszczone)");
            }
            else
            {
                insights.Add("Brak aktywnych kredytów.");
            }

            InsightsList.ItemsSource = insights;
        }

        private void UpdateKpiTiles()
        {
            var loans = _loans.OfType<LoanCardVm>().ToList();
            decimal total = loans.Sum(x => x.Principal);
            decimal monthly = 0m;
            foreach (var l in loans)
            {
                if (l.TermMonths > 0)
                    monthly += l.Principal / l.TermMonths; // uproszczony koszt miesięczny (sam kapitał)
            }

            if (FindName("TotalLoansTileAmount") is TextBlock tbTotal)
                tbTotal.Text = total.ToString("N2") + " zł";
            if (FindName("MonthlyLoansTileAmount") is TextBlock tbMonthly)
                tbMonthly.Text = monthly.ToString("N2") + " zł";
        }

        // ====== Panel dolny – przełączanie widoku ======

        private void ShowPanel(LoanPanel panel)
        {
            if (FormBorder == null) return;

            FormBorder.Visibility = panel == LoanPanel.None ? Visibility.Collapsed : Visibility.Visible;

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
            try { LoanFormMessage.Text = string.Empty; } catch { }
            try { LoanNameBox.Text = ""; } catch { }
            try { LoanPrincipalBox.Text = ""; } catch { }
            try { LoanInterestBox.Text = ""; } catch { }
            try { LoanTermBox.Text = ""; } catch { }
            try { LoanStartDatePicker.SelectedDate = DateTime.Today; } catch { }

            // reset payment day selector
            try
            {
                if (LoanPaymentDayBox != null) LoanPaymentDayBox.SelectedIndex = 0;
            }
            catch { }

            ComputeAndShowMonthlyBreakdown();

            ShowPanel(LoanPanel.AddEdit);
        }

        // wywoływane z kafelka "Dodaj kredyt"
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

            if (!decimal.TryParse((LoanPrincipalBox.Text ?? "").Trim(), out var principal)) principal = 0m;
            if (!decimal.TryParse((LoanInterestBox.Text ?? "").Trim(), out var interest)) interest = 0m;
            if (!int.TryParse((LoanTermBox.Text ?? "").Trim(), out var term)) term = 0;
            var start = LoanStartDatePicker.SelectedDate ?? DateTime.Today;

            int paymentDay = 0;
            try
            {
                if (LoanPaymentDayBox?.SelectedItem is ComboBoxItem ci && ci.Tag != null)
                {
                    if (!int.TryParse(ci.Tag.ToString(), out paymentDay)) paymentDay = 0;
                }
            }
            catch { paymentDay = 0; }

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

                // jeśli użytkownik w tym formularzu wybrał plik harmonogramu – przypisz go do tego kredytu
                if (!string.IsNullOrWhiteSpace(_lastChosenSchedulePath))
                {
                    _loanScheduleFiles[loan.Id] = _lastChosenSchedulePath;
                    // opcjonalnie wyczyść ostatnio wybraną ścieżkę, aby nie przypisywać jej kolejnym kredytom
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

        // reaguje na zmiany pól w formularzu
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


        // Upload harmonogramu
        private void ChooseSchedule_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Pliki CSV|*.csv|Pliki PDF|*.pdf|Wszystkie pliki|*.*"
            };
            var ok = dlg.ShowDialog();
            if (ok == true)
            {
                _lastChosenSchedulePath = dlg.FileName;
                ScheduleFileNameText.Text = System.IO.Path.GetFileName(dlg.FileName);
            }
        }

        // ====== Akcje z karty kredytu – przełączają dolny panel ======

        private void CardDetails_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                _selectedVm = vm;
                LoanNameBox.Text = vm.Name;
                LoanPrincipalBox.Text = vm.Principal.ToString();
                LoanInterestBox.Text = vm.InterestRate.ToString();
                LoanTermBox.Text = vm.TermMonths.ToString();
                LoanStartDatePicker.SelectedDate = vm.StartDate;

                // dzień pobrania raty
                try
                {
                    if (LoanPaymentDayBox != null)
                    {
                        int pd = vm.PaymentDay;
                        for (int i = 0; i < LoanPaymentDayBox.Items.Count; i++)
                        {
                            if (LoanPaymentDayBox.Items[i] is ComboBoxItem ci)
                            {
                                if (ci.Tag != null && int.TryParse(ci.Tag.ToString(), out var tagVal) && tagVal == pd)
                                {
                                    LoanPaymentDayBox.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }

                ComputeAndShowMonthlyBreakdown();
                ShowPanel(LoanPanel.AddEdit);
            }
        }

        private void EditLoan_Click(object sender, RoutedEventArgs e)
        {
            // Edycja działa identycznie jak Szczegóły – otwieramy panel Add/Edit z uzupełnionymi polami
            CardDetails_Click(sender, e);
        }

        private void CardAddPayment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                _selectedVm = vm;
                OverpayAmountBox.Text = "";
                OverpayResult.Text = string.Empty;
                ShowPanel(LoanPanel.Overpay);
            }
        }

        private void CardSchedule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                _selectedVm = vm;

                var detailsVm = new LoanDetailsWindow.DetailsVm
                {
                    Name = vm.Name,
                    Principal = vm.Principal,
                    InterestRate = vm.InterestRate,
                    StartDate = vm.StartDate,
                    TermMonths = vm.TermMonths
                };

                List<LoanDetailsWindow.ScheduleRow> schedule;

                if (_loanScheduleFiles.TryGetValue(vm.Id, out var path) &&
                    !string.IsNullOrWhiteSpace(path) &&
                    string.Equals(System.IO.Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
                {
                    schedule = ParseScheduleCsv(path);
                }
                else
                {
                    // brak pliku lub nie-CSV – generujemy prosty harmonogram
                    schedule = GenerateSimpleSchedule(vm);
                }

                detailsVm.Schedule = schedule;

                var win = new LoanDetailsWindow(detailsVm)
                {
                    Owner = Window.GetWindow(this)
                };
                win.ShowDialog();
            }
        }

        private static List<LoanDetailsWindow.ScheduleRow> ParseScheduleCsv(string path)
        {
            var result = new List<LoanDetailsWindow.ScheduleRow>();
            if (!File.Exists(path)) return result;

            var culture = new CultureInfo("pl-PL");

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // pomiń nagłówki
                if (line.StartsWith("Data", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = line.Split(';');
                if (parts.Length < 2)
                    parts = line.Split(',');              

                if (parts.Length < 2) continue;

                if (!DateTime.TryParse(parts[0], culture, DateTimeStyles.None, out var date))
                    continue;

                var amountStr = parts[1]
                    .Replace("zł", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ", "");

                if (!decimal.TryParse(amountStr, NumberStyles.Any, culture, out var amount))
                    continue;

                result.Add(new LoanDetailsWindow.ScheduleRow
                {
                    Date = date,
                    Amount = amount
                });
            }

            return result;
        }

        private static List<LoanDetailsWindow.ScheduleRow> GenerateSimpleSchedule(LoanCardVm vm)
        {
            var list = new List<LoanDetailsWindow.ScheduleRow>();
            if (vm.TermMonths <= 0 || vm.Principal <= 0) return list;

            var per = Math.Round(vm.Principal / Math.Max(1, vm.TermMonths), 2);

            for (int i = 1; i <= vm.TermMonths; i++)
            {
                var d = vm.StartDate.AddMonths(i);
                list.Add(new LoanDetailsWindow.ScheduleRow
                {
                    Date = d,
                    Amount = per
                });
            }

            return list;
        }

        // ====== Usuwanie kredytu ======

        private void DeleteLoan_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            HideAllDeletePanels();

            FrameworkElement? container = fe;
            while (container != null && container is not ContentPresenter && container is not Border)
            {
                container = VisualTreeHelper.GetParent(container) as FrameworkElement;
            }

            if (container == null) return;

            var panel = FindDescendantByName<FrameworkElement>(container, "DeleteConfirmPanel");
            if (panel == null) return;

            panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ConfirmDeleteLoan_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                try
                {
                    DatabaseService.DeleteLoan(vm.Id, _userId);
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
            ShowPanel(LoanPanel.None);
        }

        private void DeleteCancel_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            if (btn == null) return;

            var card = FindVisualParent<Border>(btn);
            if (card == null)
            {
                HideAllDeletePanels();
                return;
            }

            var panel = FindDescendantByName<FrameworkElement>(card, "DeleteConfirmPanel");
            if (panel != null) panel.Visibility = Visibility.Collapsed;
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

        // Visual tree helpers
        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            if (child == null) return null;
            DependencyObject? parent = VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not T)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
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
                if (ch is T fe && fe.Name == name) return fe;
                var deeper = FindDescendantByName<T>(ch, name);
                if (deeper != null) return deeper;
            }
            return null;
        }


        private void OverpaySave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null)
            {
                OverpayResult.Text = "Najpierw wybierz kredyt (kliknij kartę).";
                OverpayResult.Foreground = Brushes.IndianRed;
                return;
            }

            if (!decimal.TryParse((OverpayAmountBox.Text ?? "").Replace(" ", ""), out var amount) || amount <= 0)
            {
                OverpayResult.Text = "Podaj poprawną kwotę nadpłaty.";
                OverpayResult.Foreground = Brushes.IndianRed;
                return;
            }

            var vm = _selectedVm;
            var today = DateTime.Today;

            // 1) wyznaczamy „poprzedni termin raty” wg dnia płatności
            var prevDue = GetPreviousDueDate(today, vm.PaymentDay, vm.StartDate);

            // 2) liczymy odsetki dzienne między terminem raty a dniem nadpłaty
            var extraInterest = LoanMathService.CalculateInterest(
                vm.Principal,
                vm.InterestRate,
                prevDue,
                today);

            // 3) obniżamy kapitał o nadpłatę
            var newPrincipal = vm.Principal - amount;
            if (newPrincipal < 0m) newPrincipal = 0m;

            try
            {
                // aktualizujemy kredyt w bazie
                var loan = new LoanModel
                {
                    Id = vm.Id,
                    UserId = vm.UserId,
                    Name = vm.Name,
                    Principal = newPrincipal,
                    InterestRate = vm.InterestRate,
                    StartDate = vm.StartDate,
                    TermMonths = vm.TermMonths,
                    PaymentDay = vm.PaymentDay,
                    // Note – jeśli będziesz chciała, możesz tu dopisać
                };

                DatabaseService.UpdateLoan(loan);

                OverpayResult.Foreground = Brushes.Green;
                OverpayResult.Text =
                    $"Nadpłata: {amount:N2} zł\n" +
                    $"Szacunkowe odsetki za okres {prevDue:dd.MM.yyyy}–{today:dd.MM.yyyy}: {extraInterest:N2} zł\n" +
                    $"Nowe saldo kapitału: {newPrincipal:N2} zł.";

                ToastService.Success("Nadpłata zapisana – kredyt zaktualizowany.");

                LoadLoans();
                RefreshKpisAndLists();
            }
            catch (Exception ex)
            {
                OverpayResult.Foreground = Brushes.IndianRed;
                OverpayResult.Text = "Błąd: " + ex.Message;
                ToastService.Error("Błąd zapisu nadpłaty: " + ex.Message);
            }
        }


        // kliknięcie przycisku "Symulacja" na karcie
        private void ShowSimPanel_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                _selectedVm = vm;
                SimExtraBox.Text = "";
                SimResult.Text = "";
                ShowPanel(LoanPanel.Sim);
            }
        }

        // przycisk "Symuluj" w panelu na dole
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


        /// <summary>
        /// Poprzedni „umowny” termin raty:
        /// - jeśli dziś jest po terminie z tego miesiąca -> zwracamy ten termin,
        /// - jeśli przed -> idziemy miesiąc wstecz,
        /// - nie cofamy się przed datę startu kredytu.
        /// </summary>
        private static DateTime GetPreviousDueDate(DateTime today, int paymentDay, DateTime startDate)
        {
            // jeśli nie ustawiono dnia – cofamy się po prostu o miesiąc
            if (paymentDay <= 0)
                return today.Date.AddMonths(-1);

            // termin w bieżącym miesiącu
            int daysInThisMonth = DateTime.DaysInMonth(today.Year, today.Month);
            int day = Math.Min(paymentDay, daysInThisMonth);
            var thisDue = new DateTime(today.Year, today.Month, day);

            if (today.Date >= thisDue.Date)
            {
                // poprzedni termin to ten z bieżącego miesiąca
                if (thisDue.Date < startDate.Date)
                    return startDate.Date;
                return thisDue.Date;
            }

            // inaczej – cofamy się miesiąc wstecz
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




