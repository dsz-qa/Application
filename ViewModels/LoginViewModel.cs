using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Finly.Helpers; // RelayCommand
using Finly.Services.Features;
using Finly.Views; // DashboardView, LoginView


namespace Finly.ViewModels
{
    public class LoginViewModel : INotifyPropertyChanged
    {
        private string _username = string.Empty;
        private string _password = string.Empty;

        public string Username
        {
            get => _username;
            set
            {
                if (_username == (value ?? string.Empty)) return;
                _username = value ?? string.Empty;
                OnPropertyChanged();
                RaiseCanExecuteChanged();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (_password == (value ?? string.Empty)) return;
                _password = value ?? string.Empty;
                OnPropertyChanged();
                RaiseCanExecuteChanged();
            }
        }

        public ICommand LoginCommand { get; }

        public LoginViewModel()
        {
            // CanExecute blokuje przycisk gdy pola s¹ puste
            LoginCommand = new RelayCommand(_ => Login(), _ => CanLogin()); // poprawka: lambdy z parametrem
        }

        private bool CanLogin()
            => !string.IsNullOrWhiteSpace(Username)
            && !string.IsNullOrWhiteSpace(Password);

        private void Login()
        {
            try
            {
                var user = (Username ?? string.Empty).Trim().ToLowerInvariant();
                var pass = Password ?? string.Empty;

                if (!CanLogin())
                {
                    MessageBox.Show("Podaj login i has³o.", "Logowanie", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!UserService.Login(user, pass))
                {
                    MessageBox.Show("B³êdny login lub has³o.", "Logowanie", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Sukces: pobierz Id i otwórz Dashboard
                int userId = UserService.GetUserIdByUsername(user);
                if (userId <= 0)
                {
                    MessageBox.Show("Nie uda³o siê pobraæ identyfikatora u¿ytkownika.", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                UserService.CurrentUserId = userId;
                var shell = new ShellWindow();
                Application.Current.MainWindow = shell;
                shell.Show();

                // zamykamy okno logowania:
                Application.Current.Windows
                    .OfType<Finly.Views.AuthWindow>()
                    .FirstOrDefault()
                    ?.Close();


            }
            catch (Exception ex)
            {
                MessageBox.Show($"Wyst¹pi³ b³¹d podczas logowania:\n{ex.Message}", "B³¹d", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RaiseCanExecuteChanged()
        {
            if (LoginCommand is RelayCommand rc)
                rc.RaiseCanExecuteChanged();
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
