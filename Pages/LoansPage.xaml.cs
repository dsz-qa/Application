using Finly.Models;
using Finly.Services;
using System;
using System.Collections.ObjectModel;
using System.Data;
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

        public LoansPage() : this(UserService.GetCurrentUserId()) { }

        public LoansPage(int userId)
        {
            InitializeComponent();
            _userId = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            LoansGrid.ItemsSource = _loans;
            UpcomingPaymentsList.ItemsSource = new ObservableCollection<object>();
            InsightsList.ItemsSource = new ObservableCollection<string>();
            Loaded += LoansPage_Loaded;

            // start with form hidden
            FormBorder.Visibility = Visibility.Collapsed;
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
            TotalDebtText.Text = totalDebt.ToString("N0") + " zł";

            // Simple monthly payment estimate: sum of principal / remaining months (na szybko)
            decimal monthly = 0m;
            foreach (var l in _loans.OfType<LoanCardVm>())
            {
                var monthsElapsed = (DateTime.Today.Year - l.StartDate.Year) * 12 + DateTime.Today.Month - l.StartDate.Month;
                var monthsLeft = Math.Max(1, l.TermMonths - monthsElapsed);
                monthly += monthsLeft > 0 ? Math.Round(l.Principal / monthsLeft, 2) : l.Principal;
            }
            MonthlyPaymentText.Text = monthly.ToString("N0") + " zł";

            // Average monthly over 12 months (simple)
            var avg12 = _loans.OfType<LoanCardVm>().Sum(x => x.Principal) / 12m;
            AvgMonthlyText.Text = avg12.ToString("N0") + " zł";

            // Percent of loans that are fully paid (Principal == 0 for simplicity)
            var loanCount = _loans.OfType<LoanCardVm>().Count();
            var paidPct = loanCount == 0 ? 0 : (double)_loans.OfType<LoanCardVm>().Count(x => x.Principal <= 0) / loanCount * 100.0;
            PercentPaidText.Text = ((int)paidPct).ToString() + "%";


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
                insights.Add($"W grudniu sumaryczne raty = {monthly:N0} zł, to ~{(monthly / snapshot * 100m):N0}% Twoich przychodów.");
                insights.Add("Najwyższe oprocentowanie: sprawdź kredyt z największą stopą.");
            }
            InsightsList.ItemsSource = insights;
        }

        // Header button - show add form
        private void ShowAddLoan_Click(object sender, RoutedEventArgs e)
        {
            ShowAddForm();
        }

        private void ShowAddForm()
        {
            FormHeader.Text = "Dodaj kredyt";
            LoanFormMessage.Text = string.Empty;
            LoanNameBox.Text = "";
            LoanPrincipalBox.Text = "0";
            LoanInterestBox.Text = "0";
            LoanTermBox.Text = "0";
            LoanStartDatePicker.SelectedDate = DateTime.Today;
            FormBorder.Visibility = Visibility.Visible;
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

            if (!decimal.TryParse((LoanPrincipalBox.Text ?? "").Trim(), out var principal)) principal = 0m;
            if (!decimal.TryParse((LoanInterestBox.Text ?? "").Trim(), out var interest)) interest = 0m;
            if (!int.TryParse((LoanTermBox.Text ?? "").Trim(), out var term)) term = 0;
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
                MessageBox.Show("Kredyt dodany.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd dodawania kredytu: " + ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                FormBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void CancelLoan_Click(object sender, RoutedEventArgs e)
        {
            FormBorder.Visibility = Visibility.Collapsed;
        }

        private void CardDetails_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                var details = new LoanDetailsWindow(new LoanDetailsWindow.DetailsVm {
                    Name = vm.Name,
                    Principal = vm.Principal,
                    InterestRate = vm.InterestRate,
                    StartDate = vm.StartDate,
                    TermMonths = vm.TermMonths
                });
                details.Owner = Window.GetWindow(this);
                details.ShowDialog();
                LoadLoans();
                RefreshKpisAndLists();
            }
        }

        private void CardAddPayment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                var amtStr = Microsoft.VisualBasic.Interaction.InputBox("Kwota nadpłaty:", "Nadpłata", "0");
                if (!decimal.TryParse(amtStr, out var amt)) return;
                try
                {
                    // Proste: zaktualizuj principal w DB redukując o amt
                    using var c = DatabaseService.GetConnection();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "UPDATE Loans SET Principal = Principal - @a WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@a", amt);
                    cmd.Parameters.AddWithValue("@id", vm.Id);
                    cmd.ExecuteNonQuery();

                    LoadLoans();
                    RefreshKpisAndLists();
                    MessageBox.Show("Nadpłata zapisana.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Błąd: " + ex.Message);
                }
            }
        }

        private void CardSchedule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanCardVm vm)
            {
                MessageBox.Show($"Harmonogram dla {vm.Name} (w budowie)");
            }
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
                if (Principal <= 0) return 100.0;
                // No original principal stored here; show 0% paid as safe default
                return 0.0;
            }
        }

        public DateTime NextPaymentDate => StartDate.AddMonths(1);
        public decimal NextPayment => Math.Round(Principal > 0 ? Principal / Math.Max(1, TermMonths) : 0m, 0);
        public string NextPaymentInfo => NextPayment.ToString("N0") + " zł · " + NextPaymentDate.ToString("dd.MM.yyyy");
        public string RemainingTermStr
        {
            get
            {
                if (TermMonths <= 0) return "—";
                var monthsLeft = Math.Max(0, TermMonths - ((DateTime.Today.Year - StartDate.Year) * 12 + DateTime.Today.Month - StartDate.Month));
                var years = monthsLeft / 12;
                var months = monthsLeft % 12;
                return $"{years} lat {months} mies.";
            }
        }
    }
}
