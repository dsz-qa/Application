using System.Linq;
using System.Windows;
using System.Windows.Input;
using Finly.Helpers;
using Finly.Services;

namespace Finly.ViewModels
{
    public class RegisterViewModel
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        // >>> DODANE: potwierdzenie has³a (ustawiane przez RepeatPasswordBox_PasswordChanged)
        public string ConfirmPassword { get; set; } = string.Empty;

        public ICommand RegisterCommand { get; }

        public RegisterViewModel()
        {
            RegisterCommand = new RelayCommand(Register);
        }

        private void Register()
        {
            // Walidacja u¿ytkownika
            var normalized = (Username ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                MessageBox.Show("Login nie mo¿e byæ pusty.", "Rejestracja", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Walidacja has³a
            if (!ValidatePassword(Password, ConfirmPassword, out string error))
            {
                MessageBox.Show(error, "Rejestracja", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Rejestracja
            bool success = UserService.Register(normalized, Password);
            if (success)
            {
                MessageBox.Show("Rejestracja udana!", "Rejestracja", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Rejestracja nieudana. Taki u¿ytkownik mo¿e ju¿ istnieæ.", "Rejestracja",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Prosta walidacja zgodna z checklist¹:
        // - >= 8 znaków, - ma³a litera, - DU¯A litera, - cyfra, - znak specjalny, - bez spacji, - zgodnoœæ z powtórzeniem
        private static bool ValidatePassword(string password, string confirm, out string error)
        {
            error = string.Empty;
            password ??= string.Empty;
            confirm ??= string.Empty;

            if (password.Length < 8)
            {
                error = "Has³o musi mieæ co najmniej 8 znaków.";
                return false;
            }
            if (!password.Any(char.IsLower))
            {
                error = "Has³o musi zawieraæ ma³¹ literê.";
                return false;
            }
            if (!password.Any(char.IsUpper))
            {
                error = "Has³o musi zawieraæ wielk¹ literê.";
                return false;
            }
            if (!password.Any(char.IsDigit))
            {
                error = "Has³o musi zawieraæ cyfrê.";
                return false;
            }
            if (!password.Any(ch => char.IsPunctuation(ch) || char.IsSymbol(ch)))
            {
                error = "Has³o musi zawieraæ znak specjalny (np. !@#$%).";
                return false;
            }
            if (password.Any(char.IsWhiteSpace))
            {
                error = "Has³o nie mo¿e zawieraæ spacji.";
                return false;
            }
            if (password != confirm)
            {
                error = "Powtórzone has³o musi byæ identyczne.";
                return false;
            }
            return true;
        }
    }
}
