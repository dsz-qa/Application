using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;
using Finly.Helpers;
using Finly.Pages;
using Finly.Services;

namespace Finly.ViewModels
{
    /// <summary>ViewModel powłoki (ShellWindow): nawigacja i bieżący widok.</summary>
    public class ShellViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<NavItem> NavItems { get; }

        private UserControl? _currentView;
        public UserControl? CurrentView
        {
            get => _currentView;
            set { if (!ReferenceEquals(_currentView, value)) { _currentView = value; OnPropertyChanged(); } }
        }

        public string DisplayName => UserService.CurrentUserName ?? "Użytkownik";

        public ICommand NavigateToCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand LogoutCommand { get; }

        public ShellViewModel()
        {
            int uid = UserService.GetCurrentUserId(); // bezpiecznie, gdyby CurrentUserId nie był ustawiony

            NavItems = new ObservableCollection<NavItem>
            {
                new("add",          "Dodaj",            () => new AddExpensePage(uid)),
                new("transactions", "Transakcje",       () => new TransactionsPage()),
                new("charts",       "Wykresy",          () => new ChartsPage()),
                new("budgets",      "Budżety",          () => new BudgetsPage()),
                new("subscriptions","Subskrypcje",      () => new InvestmentsPage()),
                new("goals",        "Cele",             () => new GoalsPage()),
                new("categories",   "Kategorie",        () => new CategoriesPage()),
                new("reports",      "Raporty",          () => new ReportsPage()),
                new("settings",     "Ustawienia",       () => new SettingsPage())
            };

            NavigateToCommand = new RelayCommand(p => NavigateTo(p?.ToString()));
            OpenSettingsCommand = new RelayCommand(() => NavigateTo("settings"));
            LogoutCommand = new RelayCommand(() => OnLogoutRequested());

            // Ekran startowy – wybierz, co wolisz:
            NavigateTo("add");          // od razu formularz Dodaj
            // NavigateTo("transactions");
        }

        public void NavigateTo(string? key)
        {
            var item = NavItems.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase))
                       ?? NavItems.First();

            foreach (var ni in NavItems) ni.IsSelected = (ni == item);
            CurrentView = item.Factory();
        }

        public event EventHandler? LogoutRequested;
        private void OnLogoutRequested() => LogoutRequested?.Invoke(this, EventArgs.Empty);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

