using Finly.Models;
using Finly.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Pages
{
    public partial class LoansPage : UserControl
    {
        private readonly ObservableCollection<object> _loans = new();
        private readonly int _userId;

        // currently selected loan in the inline form
        private LoanCardVm? _selectedVm;

        public LoansPage() : this(UserService.GetCurrentUserId()) { }

        public LoansPage(int userId)
        {
            InitializeComponent();
            _userId = userId <=0 ? UserService.GetCurrentUserId() : userId;

            // bind collections
            LoansGrid.ItemsSource = _loans;
            UpcomingPaymentsList.ItemsSource = new ObservableCollection<object>();
            InsightsList.ItemsSource = new ObservableCollection<string>();
            Loaded += LoansPage_Loaded;

            // start with form hidden
            try { FormBorder.Visibility = Visibility.Collapsed; } catch { }
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
                    TermMonths = l.TermMonths
                });
            }

            // add "Add" tile at the end
            _loans.Add(new AddLoanTile());
        }

        private void RefreshKpisAndLists()
        {
            decimal totalDebt = _loans.OfType<LoanCardVm>().Sum(x => x.Principal);

            // Set KPI if TextBlock exists in XAML
            var totalDebtTb = FindName("TotalDebtText") as TextBlock;
            if (totalDebtTb != null) totalDebtTb.Text = totalDebt.ToString("N0") + " zł";

            // Simple monthly payment estimate: sum of principal / remaining months
            decimal monthly =0m;
            foreach (var l in _loans.OfType<LoanCardVm>())
            {
                var monthsElapsed = (DateTime.Today.Year - l.StartDate.Year) *12 + DateTime.Today.Month - l.StartDate.Month;
                var monthsLeft = Math.Max(1, l.TermMonths - monthsElapsed);
                monthly += monthsLeft >0 ? Math.Round(l.Principal / monthsLeft,2) : l.Principal;
            }

            // Average monthly over12 months (simple)
            var avg12 = _loans.OfType<LoanCardVm>().Sum(x => x.Principal) /12m;

            // Percent of loans that are fully paid (Principal ==0 for simplicity)
            var loanCount = _loans.OfType<LoanCardVm>().Count();
            var paidPct = loanCount ==0 ?0 : (double)_loans.OfType<LoanCardVm>().Count(x => x.Principal <=0) / loanCount *100.0;

            // Build upcoming payments (dummy): next payment next month for each loan
            var upcoming = new ObservableCollection<object>();
            foreach (var l in _loans.OfType<LoanCardVm>().OrderBy(x => x.NextPaymentDate))
            {
                upcoming.Add(new { DateStr = l.NextPaymentDate.ToString("dd.MM"), LoanName = l.Name, AmountStr = l.NextPayment.ToString("N0") + " zł" });
            }
            UpcomingPaymentsList.ItemsSource = upcoming;

            // Insights (simple heuristics)
            var insights = new ObservableCollection<string>();
            if (totalDebt >0)
            {
                var snapshot = DatabaseService.GetMoneySnapshot(_userId)?.Total ??1m;
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

        private void ShowAddForm()
        {
            _selectedVm = null;
            try { LoanFormMessage.Text = string.Empty; } catch { }
            try { LoanNameBox.Text = ""; } catch { }
            try { LoanPrincipalBox.Text = "0"; } catch { }
            try { LoanInterestBox.Text = "0"; } catch { }
            try { LoanTermBox.Text = "0"; } catch { }
            try { LoanStartDatePicker.SelectedDate = DateTime.Today; } catch { }
            try { FormTabs.SelectedIndex =0; } catch { }
            try { FormBorder.Visibility = Visibility.Visible; } catch { }
        }

        // Header button - show add form
        private void ShowAddLoan_Click(object sender, RoutedEventArgs e) => ShowAddForm();

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

            if (!decimal.TryParse((LoanPrincipalBox.Text ?? "").Trim(), out var principal)) principal =0m;
            if (!decimal.TryParse((LoanInterestBox.Text ?? "").Trim(), out var interest)) interest =0m;
            if (!int.TryParse((LoanTermBox.Text ?? "").Trim(), out var term)) term =0;
            var start = LoanStartDatePicker.SelectedDate ?? DateTime.Today;

            try
            {
                var loan = new LoanModel
                {
                    UserId = _userId,
                    Name = name,
                    Principal = principal,
                    InterestRate = interest,
                    StartDate = start,
                    TermMonths = term
                };

                var id = DatabaseService.InsertLoan(loan);
                loan.Id = id;
                LoadLoans();
                RefreshKpisAndLists();
                ToastService.Success("Kredyt dodany.");
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd dodawania kredytu: " + ex.Message);
            }
            finally
            {
                try { FormBorder.Visibility = Visibility.Collapsed; } catch { }
            }
        }

        private void CancelLoan_Click(object sender, RoutedEventArgs e)
        {
            try { FormBorder.Visibility = Visibility.Collapsed; } catch { }
        }

        private void CardDetails_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                // show inline form and populate for selected loan
                _selectedVm = vm;
                try { LoanNameBox.Text = vm.Name; } catch { }
                try { LoanPrincipalBox.Text = vm.Principal.ToString(); } catch { }
                try { LoanInterestBox.Text = vm.InterestRate.ToString(); } catch { }
                try { LoanTermBox.Text = vm.TermMonths.ToString(); } catch { }
                try { LoanStartDatePicker.SelectedDate = vm.StartDate; } catch { }

                // build simple schedule
                var schedule = new ObservableCollection<string>();
                if (vm.TermMonths >0)
                {
                    var per = Math.Round(vm.Principal / Math.Max(1, vm.TermMonths),2);
                    for (int i =1; i <= vm.TermMonths; i++)
                    {
                        var d = vm.StartDate.AddMonths(i);
                        schedule.Add($"{d:dd.MM.yyyy} — {per:N2} zł");
                    }
                }
                else
                {
                    schedule.Add("Brak harmonogramu (okres =0)");
                }
                try { ScheduleList.ItemsSource = schedule; } catch { }

                try { FormTabs.SelectedIndex =1; } catch { }
                try { FormBorder.Visibility = Visibility.Visible; } catch { }
            }
        }

        private void CardAddPayment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                // open Overpay tab and prefill selected vm
                _selectedVm = vm;
                try { OverpayAmountBox.Text = ""; } catch { }
                try { OverpayResult.Text = string.Empty; } catch { }
                try { FormTabs.SelectedIndex =2; } catch { }
                try { FormBorder.Visibility = Visibility.Visible; } catch { }
            }
        }

        private void OverpaySave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null) return;
            if (!decimal.TryParse((OverpayAmountBox.Text ?? "").Replace(" ", ""), out var amt) || amt <=0)
            {
                OverpayResult.Text = "Podaj poprawną kwotę nadpłaty.";
                OverpayResult.Foreground = System.Windows.Media.Brushes.IndianRed;
                return;
            }

            try
            {
                using var c = DatabaseService.GetConnection();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE Loans SET Principal = Principal - @a WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@a", amt);
                cmd.Parameters.AddWithValue("@id", _selectedVm.Id);
                cmd.ExecuteNonQuery();

                LoadLoans();
                RefreshKpisAndLists();

                OverpayResult.Text = "Nadpłata zapisana.";
                OverpayResult.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                OverpayResult.Text = "Błąd: " + ex.Message;
                OverpayResult.Foreground = System.Windows.Media.Brushes.IndianRed;
            }
        }

        private void CardSchedule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                // show schedule inline
                _selectedVm = vm;
                var schedule = new ObservableCollection<string>();
                if (vm.TermMonths >0)
                {
                    var per = Math.Round(vm.Principal / Math.Max(1, vm.TermMonths),2);
                    for (int i =1; i <= vm.TermMonths; i++)
                    {
                        var d = vm.StartDate.AddMonths(i);
                        schedule.Add($"{d:dd.MM.yyyy} — {per:N2} zł");
                    }
                }
                else
                    schedule.Add("Brak harmonogramu (okres =0)");

                try { ScheduleList.ItemsSource = schedule; } catch { }
                try { FormTabs.SelectedIndex =1; } catch { }
                try { FormBorder.Visibility = Visibility.Visible; } catch { }
            }
        }

        private void SimulateInline_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVm == null)
            {
                SimResult.Text = "Wybierz najpierw kredyt (Kliknij kartę).";
                return;
            }

            if (!decimal.TryParse((SimExtraBox.Text ?? "").Replace(" ", ""), out var extra) || extra <=0)
            {
                SimResult.Text = "Podaj poprawna kwote.";
                return;
            }

            var saved = Math.Round(extra *0.05m,2);
            var months = (int)(extra / Math.Max(1, _selectedVm.Principal));
            SimResult.Text = $"Oszczedzisz ~{saved:N2} zl na odsetkach i skrocisz kredyt o ~{months} miesiecy (szac.).";
        }
    }

    // marker dla kafelka "Dodaj kredyt"
    public sealed class AddLoanTile { }

    public sealed class LoanCardVm
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public decimal Principal { get; set; }
        public decimal InterestRate { get; set; }
        public DateTime StartDate { get; set; }
        public int TermMonths { get; set; }

        public string PrincipalStr => Principal.ToString("N0") + " zł";

        public double PercentPaidClamped
        {
            get
            {
                // If principal is zero or negative, consider fully paid
                if (Principal <=0) return 100.0;
                // No original principal stored here; return0% paid as default
                return 0.0;
            }
        }

        public DateTime NextPaymentDate => StartDate.AddMonths(1);
        public decimal NextPayment => Math.Round(Principal >0 ? Principal / Math.Max(1, TermMonths) :0m,0);
        public string NextPaymentInfo => NextPayment.ToString("N0") + " zł · " + NextPaymentDate.ToString("dd.MM.yyyy");

        public string RemainingTermStr
        {
            get
            {
                if (TermMonths <=0) return "—";
                var monthsLeft = Math.Max(0, TermMonths - ((DateTime.Today.Year - StartDate.Year) *12 + DateTime.Today.Month - StartDate.Month));
                var years = monthsLeft /12;
                var months = monthsLeft %12;
                return $"{years} lat {months} mies.";
            }
        }
    }
}
