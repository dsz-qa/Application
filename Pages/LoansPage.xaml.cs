using Finly.Models;
using Finly.ViewModels;
using Finly.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Finly.Pages
{
    public partial class LoansPage : UserControl
    {
        private readonly ObservableCollection<object> _loans = new();
        private readonly int _userId;
        private LoanCardVm? _selectedVm;

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
            if (!decimal.TryParse((LoanPrincipalBox.Text ?? "").Replace(" ", ""), out var principal)) principal = 0m;
            if (!decimal.TryParse((LoanInterestBox.Text ?? "").Replace(" ", ""), out var annualRate)) annualRate = 0m;
            if (!int.TryParse((LoanTermBox.Text ?? "").Replace(" ", ""), out var months)) months = 0;

            if (principal <= 0 || months <= 0)
            {
                MonthlyPaymentText.Text = "0,00 zł";
                FirstPrincipalText.Text = "0,00 zł";
                FirstInterestText.Text = "0,00 zł";
                return;
            }

            var r = annualRate / 100m / 12m;

            decimal payment;
            if (r == 0m)
                payment = Math.Round(principal / months, 2);
            else
            {
                var denom = 1m - (decimal)Math.Pow((double)(1m + r), -months);
                payment = Math.Round(principal * r / denom, 2);
            }

            decimal firstInterest = Math.Round(principal * r, 2);
            decimal firstPrincipal = Math.Round(payment - firstInterest, 2);

            MonthlyPaymentText.Text = payment.ToString("N2") + " zł";
            FirstPrincipalText.Text = firstPrincipal.ToString("N2") + " zł";
            FirstInterestText.Text = firstInterest.ToString("N2") + " zł";
        }

        // Upload harmonogramu
        private void ChooseSchedule_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files|*.pdf|All Files|*.*"
            };
            var ok = dlg.ShowDialog();
            if (ok == true)
            {
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
                var schedule = new ObservableCollection<string>();
                if (vm.TermMonths > 0)
                {
                    var per = Math.Round(vm.Principal / Math.Max(1, vm.TermMonths), 2);
                    for (int i = 1; i <= vm.TermMonths; i++)
                    {
                        var d = vm.StartDate.AddMonths(i);
                        schedule.Add($"{d:dd.MM.yyyy} — {per:N2} zł");
                    }
                }
                else
                {
                    schedule.Add("Brak harmonogramu (okres = 0).");
                }

                ScheduleList.ItemsSource = schedule;
                ShowPanel(LoanPanel.Schedule);
            }
        }

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

        private void OverpaySave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null) return;
            if (!decimal.TryParse((OverpayAmountBox.Text ?? "").Replace(" ", ""), out var amt) || amt <= 0)
            {
                OverpayResult.Text = "Podaj poprawną kwotę nadpłaty.";
                OverpayResult.Foreground = Brushes.IndianRed;
                return;
            }

            try
            {
                // TODO: faktyczna logika nadpłaty w bazie (osobny serwis, jak będziesz chciała)
                OverpayResult.Text = "Nadpłata zapisana (placeholder).";
                OverpayResult.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                OverpayResult.Text = "Błąd: " + ex.Message;
                OverpayResult.Foreground = Brushes.IndianRed;
            }
        }

        private void SimulateInline_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null)
            {
                SimResult.Text = "Wybierz najpierw kredyt (kliknij kartę).";
                return;
            }

            if (!decimal.TryParse((SimExtraBox.Text ?? "").Replace(" ", ""), out var extra) || extra <= 0)
            {
                SimResult.Text = "Podaj poprawną kwotę.";
                return;
            }

            var saved = Math.Round(extra * 0.05m, 2);
            var months = (int)(extra / Math.Max(1, _selectedVm.Principal));
            SimResult.Text = $"Oszczędzisz ~{saved:N2} zł na odsetkach i skrócisz kredyt o ~{months} mies. (szac.).";
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
    }
}
