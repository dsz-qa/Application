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
    /// <summary>
    /// ViewModel powłoki (ShellWindow): utrzymuje listę zakładek i bieżący widok.
    /// </summary>
    public class ShellWViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<NavItem> NavItems { get; }

        private UserControl? _currentView;
        public UserControl? CurrentView
        {
            get => _currentView;
            set { if (!ReferenceEquals(_currentView, value)) { _currentView = value; OnPropertyChanged(); } }
        }

        public string DisplayName => UserService.CurrentUserName ?? "Użytkownik";

        // Komendy
        public ICommand NavigateToCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand LogoutCommand { get; }

        public ShellWViewModel()
        {
            // Zdefiniuj nawigację (klucz musi odpowiadać temu, czego użyjesz w XAML)
            NavItems = new ObservableCollection<NavItem>
            {
                new("add",          "Dodaj wydatek",     () => new AddExpensePage()),
                new("transactions", "Transakcje",        () => new TransactionsPage()),
                new("charts",       "Wykresy",           () => new ChartsPage()),
                new("budgets",      "Budżety",           () => new BudgetsPage()),
                new("subscriptions","Subskrypcje",       () => new SubscriptionsPage()),
                new("goals",        "Cele",              () => new GoalsPage()),
                new("categories",   "Kategorie",         () => new CategoriesPage()),
                new("reports",      "Raporty",           () => new ReportsPage()),
                // jeśli masz ImportPage.xaml (albo ImportSyncPage.xaml) – dopasuj tutaj:
                new("import",       "Import / Sync",     () => new ImportPage()),
                new("settings",     "Ustawienia",        () => new SettingsPage())
            };

            NavigateToCommand = new RelayCommand(p => NavigateTo(p?.ToString()));
            OpenSettingsCommand = new RelayCommand(() => NavigateTo("settings"));
            LogoutCommand = new RelayCommand(() => OnLogoutRequested());

            // startowy ekran
            NavigateTo("transactions");
        }

        /// <summary>Zmienia aktywną zakładkę po kluczu (np. "charts").</summary>
        public void NavigateTo(string? key)
        {
            var item = NavItems.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase))
                       ?? NavItems.First();

            foreach (var ni in NavItems) ni.IsSelected = (ni == item);
            CurrentView = item.Factory();
        }

        // ===== sygnał do okna (np. zamknij sesję i wróć do AuthWindow) =====
        public event EventHandler? LogoutRequested;
        private void OnLogoutRequested() => LogoutRequested?.Invoke(this, EventArgs.Empty);

        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
