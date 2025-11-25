using Finly.Models;
using Finly.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class LoansPage : UserControl
    {
        private readonly ObservableCollection<LoanVm> _loans = new();
        private readonly int _userId;

        public LoansPage() : this(UserService.GetCurrentUserId()) { }

        public LoansPage(int userId)
        {
            InitializeComponent();
            _userId = userId <= 0 ? UserService.GetCurrentUserId() : userId;

            LoansList.ItemsSource = _loans;
            Loaded += LoansPage_Loaded;
        }

        private void LoansPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLoans();
        }

        private void LoadLoans()
        {
            _loans.Clear();
            var list = DatabaseService.GetLoans(_userId) ?? new System.Collections.Generic.List<LoanModel>();
            foreach (var l in list)
            {
                _loans.Add(new LoanVm
                {
                    Id = l.Id,
                    Name = l.Name,
                    Principal = l.Principal,
                    InterestRate = l.InterestRate,
                    StartDate = l.StartDate,
                    TermMonths = l.TermMonths
                });
            }
        }

        private void AddLoan_Click(object sender, RoutedEventArgs e)
        {
            var name = Microsoft.VisualBasic.Interaction.InputBox("Nazwa kredytu:", "Dodaj kredyt", "Nowy kredyt");
            if (string.IsNullOrWhiteSpace(name)) return;

            var principalStr = Microsoft.VisualBasic.Interaction.InputBox("Kwota główna (np.10000):", "Dodaj kredyt", "0");
            if (!decimal.TryParse(principalStr, out var principal)) principal = 0m;

            try
            {
                var loan = new LoanModel
                {
                    UserId = _userId,
                    Name = name.Trim(),
                    Principal = principal,
                    InterestRate = 0m,
                    StartDate = DateTime.Today,
                    TermMonths = 0
                };

                var id = DatabaseService.InsertLoan(loan);
                loan.Id = id;
                LoadLoans();
                MessageBox.Show("Kredyt dodany.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd dodawania kredytu: " + ex.Message, "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteLoan_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LoanVm vm)
            {
                var yes = MessageBox.Show($"Czy na pewno usunąć kredyt '{vm.Name}'?", "Usuń kredyt", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (yes != MessageBoxResult.Yes) return;

                try
                {
                    DatabaseService.DeleteLoan(vm.Id);
                    LoadLoans();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Błąd usuwania: " + ex.Message);
                }
            }
        }

        private class LoanVm
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Principal { get; set; }
            public decimal InterestRate { get; set; }
            public DateTime StartDate { get; set; }
            public int TermMonths { get; set; }

            public string PrincipalStr => Principal.ToString("N2") + " zł";
        }
    }
}
