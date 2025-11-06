using System;
using System.ComponentModel;
using System.Linq; // <-- potrzebne dla .All()
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Finly.Models;
using Finly.Services;

namespace Finly.ViewModels
{
    public class AuthViewModel : INotifyPropertyChanged
    {
        // ===== TYP KONTA =====
        private AccountType _accountType = AccountType.Personal;
        public AccountType AccountType
        {
            get => _accountType;
            set
            {
                if (Set(ref _accountType, value))
                    OnPropertyChanged(nameof(IsBusinessAccount));
            }
        }
        public bool IsBusinessAccount => AccountType == AccountType.Business;

        // ===== DANE FIRMOWE =====
        private string _companyName = string.Empty;
        public string CompanyName { get => _companyName; set => Set(ref _companyName, value ?? string.Empty); }

        private string _nip = string.Empty;
        public string NIP { get => _nip; set => Set(ref _nip, value ?? string.Empty); }

        private string _regon = string.Empty;
        public string REGON { get => _regon; set => Set(ref _regon, value ?? string.Empty); }

        private string _krs = string.Empty;
        public string KRS { get => _krs; set => Set(ref _krs, value ?? string.Empty); }

        private string _companyAddress = string.Empty;
        public string CompanyAddress { get => _companyAddress; set => Set(ref _companyAddress, value ?? string.Empty); }

        // ===== Walidacja NIP/REGON (PL) =====
        private static bool IsValidNip(string nip)
        {
            var digits = new string((nip ?? "").Replace("-", "").Trim().ToCharArray());
            if (digits.Length != 10 || !digits.All(char.IsDigit)) return false;
            int[] w = { 6, 5, 7, 2, 3, 4, 5, 6, 7 };
            int sum = 0;
            for (int i = 0; i < 9; i++) sum += (digits[i] - '0') * w[i];
            int c = sum % 11;
            return c == (digits[9] - '0');
        }

        private static bool IsValidRegon(string regon)
        {
            var d = new string((regon ?? "").Replace("-", "").Trim().ToCharArray());
            if (!(d.Length == 9 || d.Length == 14) || !d.All(char.IsDigit)) return false;
            int[] w9 = { 8, 9, 2, 3, 4, 5, 6, 7 };
            int[] w14 = { 2, 4, 8, 5, 0, 9, 7, 3, 6, 1, 2, 4, 8 };
            if (d.Length == 9)
            {
                int s = 0; for (int i = 0; i < 8; i++) s += (d[i] - '0') * w9[i];
                return (s % 11 % 10) == (d[8] - '0');
            }
            else
            {
                int s = 0; for (int i = 0; i < 13; i++) s += (d[i] - '0') * w14[i];
                return (s % 11 % 10) == (d[13] - '0');
            }
        }

        // ===== Pola stanu logowania/rejestracji =====
        private string _username = string.Empty;
        private bool _isLoginMode = true;

        private string _registerMessage = string.Empty;
        private bool _registerIsError;

        private string _loginBanner = string.Empty;
        private Brush _loginBannerBrush = Brushes.Transparent;

        private string _loginMessage = string.Empty;
        private bool _loginIsError;

        // Widoczność oraz treść haseł (rejestracja)
        private bool _isPasswordVisible = false;
        private string _password = string.Empty;
        private string _repeatPassword = string.Empty;

        // E-mail regex (culture-invariant)
        private static readonly Regex EmailRegex =
            new(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // ===== Właściwości publiczne =====
        public string Username
        {
            get => _username;
            set
            {
                if (Set(ref _username, (value ?? string.Empty).Trim()))
                    UpdateEmailValidity();
            }
        }

        public bool IsLoginMode
        {
            get => _isLoginMode;
            private set
            {
                if (Set(ref _isLoginMode, value))
                    OnPropertyChanged(nameof(IsRegisterMode));
            }
        }
        public bool IsRegisterMode => !IsLoginMode;

        // Rejestracja – widoczność haseł i treść
        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set => Set(ref _isPasswordVisible, value);
        }

        public string Password
        {
            get => _password;
            set
            {
                if (Set(ref _password, value ?? string.Empty))
                    UpdatePasswordHints(_password, _repeatPassword);
            }
        }

        public string RepeatPassword
        {
            get => _repeatPassword;
            set
            {
                if (Set(ref _repeatPassword, value ?? string.Empty))
                    UpdatePasswordHints(_password, _repeatPassword);
            }
        }

        // Rejestracja – komunikat + kolor
        public string RegisterMessage
        {
            get => _registerMessage;
            set => Set(ref _registerMessage, value ?? string.Empty);
        }
        public bool RegisterIsError
        {
            get => _registerIsError;
            set
            {
                if (Set(ref _registerIsError, value))
                    OnPropertyChanged(nameof(RegisterMessageBrush));
            }
        }
        public Brush RegisterMessageBrush => RegisterIsError ? Brushes.IndianRed : Brushes.SeaGreen;

        // Logowanie – baner u góry
        public string LoginBanner
        {
            get => _loginBanner;
            set => Set(ref _loginBanner, value ?? string.Empty);
        }
        public Brush LoginBannerBrush
        {
            get => _loginBannerBrush;
            set => Set(ref _loginBannerBrush, value ?? Brushes.Transparent);
        }

        // Logowanie – komunikat pod hasłem
        public string LoginMessage
        {
            get => _loginMessage;
            set => Set(ref _loginMessage, value ?? string.Empty);
        }
        public bool LoginIsError
        {
            get => _loginIsError;
            set
            {
                if (Set(ref _loginIsError, value))
                    OnPropertyChanged(nameof(LoginMessageBrush));
            }
        }
        public Brush LoginMessageBrush => LoginIsError ? Brushes.IndianRed : Brushes.SeaGreen;

        public bool IsEmailValid { get; private set; }

        // ===== Wymagania hasła (live) =====
        private bool _pwMinLen, _pwLower, _pwUpper, _pwDigit, _pwSpecial, _pwNoSpace, _pwMatch;

        public bool PwMinLen { get => _pwMinLen; set { if (Set(ref _pwMinLen, value)) BumpPwAggregates(); } }
        public bool PwLower { get => _pwLower; set { if (Set(ref _pwLower, value)) BumpPwAggregates(); } }
        public bool PwUpper { get => _pwUpper; set { if (Set(ref _pwUpper, value)) BumpPwAggregates(); } }
        public bool PwDigit { get => _pwDigit; set { if (Set(ref _pwDigit, value)) BumpPwAggregates(); } }
        public bool PwSpecial { get => _pwSpecial; set { if (Set(ref _pwSpecial, value)) BumpPwAggregates(); } }
        public bool PwNoSpace { get => _pwNoSpace; set { if (Set(ref _pwNoSpace, value)) BumpPwAggregates(); } }
        public bool PwMatch { get => _pwMatch; set { if (Set(ref _pwMatch, value)) BumpPwAggregates(); } }

        public bool IsPasswordValid =>
            PwMinLen && PwLower && PwUpper && PwDigit && PwSpecial && PwNoSpace && PwMatch;

        public bool CanRegister => IsEmailValid && IsPasswordValid;

        private void BumpPwAggregates()
        {
            OnPropertyChanged(nameof(IsPasswordValid));
            OnPropertyChanged(nameof(CanRegister));
        }

        // Ustalany po poprawnym logowaniu
        public int LoggedInUserId { get; private set; } = -1;

        // ===== Przełączanie paneli =====
        public void SwitchToRegister()
        {
            IsLoginMode = false;
            ClearMessages();
            LoginBanner = string.Empty;
            LoginBannerBrush = Brushes.Transparent;
            IsPasswordVisible = false;
        }

        public void SwitchToLogin()
        {
            IsLoginMode = true;
            ClearMessages();
            LoginBanner = string.Empty;
            LoginBannerBrush = Brushes.Transparent;
            IsPasswordVisible = false;
        }

        public void ShowLogoutInfo()
        {
            IsLoginMode = true;
            Username = string.Empty;
            LoginBanner = "Zostałeś(-aś) wylogowany(-a).";
            LoginBannerBrush = Brushes.SteelBlue;
            ClearMessages();
        }

        public void ShowAccountDeletedInfo()
        {
            IsLoginMode = true;
            Username = string.Empty;
            LoginBanner = "Konto zostało usunięte.";
            LoginBannerBrush = Brushes.IndianRed;
            ClearMessages();
        }

        private void ClearMessages()
        {
            RegisterMessage = string.Empty;
            RegisterIsError = false;

            LoginMessage = string.Empty;
            LoginIsError = false;
        }

        private void UpdateEmailValidity()
        {
            IsEmailValid = EmailRegex.IsMatch(_username);
            OnPropertyChanged(nameof(IsEmailValid));
            OnPropertyChanged(nameof(CanRegister));
        }

        // ===== Logowanie =====
        public bool Login(string password)
        {
            LoginMessage = string.Empty;
            LoginIsError = false;

            var u = (Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(u))
            {
                LoginIsError = true;
                LoginMessage = "Podaj login lub e-mail.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                LoginIsError = true;
                LoginMessage = "Podaj hasło.";
                return false;
            }

            Username = u; // backend sprawdza login LUB e-mail

            if (!UserService.Login(Username, password))
            {
                LoginIsError = true;
                LoginMessage = "Błędny login lub hasło.";
                return false;
            }

            LoggedInUserId = UserService.GetUserIdByLogin(Username);
            return LoggedInUserId > 0;
        }


        // ===== Rejestracja =====
        public bool Register(string password, string confirm)
        {
            ClearMessages();
            UpdateEmailValidity();
            UpdatePasswordHints(password, confirm);

            if (!IsEmailValid)
                return Error("Podaj poprawny adres e-mail.");

            if (!IsPasswordValid)
                return Error("Hasło nie spełnia wymagań – popraw czerwone pozycje poniżej.");

            Username = (Username ?? string.Empty).ToLowerInvariant();

            if (!UserService.IsUsernameAvailable(Username))
                return Error("Nie udało się utworzyć konta. Login jest zajęty.");

            // Walidacja firmowa (gdy Business)
            if (IsBusinessAccount)
            {
                if (string.IsNullOrWhiteSpace(CompanyName))
                    return Error("Podaj nazwę firmy.");
                if (!IsValidNip(NIP))
                    return Error("Podaj poprawny NIP (10 cyfr, poprawna suma kontrolna).");
                if (!string.IsNullOrWhiteSpace(REGON) && !IsValidRegon(REGON))
                    return Error("REGON ma niepoprawny format.");
                if (string.IsNullOrWhiteSpace(CompanyAddress))
                    return Error("Podaj adres siedziby firmy.");
            }

            // Rejestracja z zapisem typu konta i ewentualnych danych firmy
            bool ok = UserService.Register(
                username: Username,
                password: password,
                accountType: AccountType,
                companyName: CompanyName,
                nip: NIP,
                regon: REGON,
                krs: KRS,
                companyAddress: CompanyAddress
            );

            if (!ok)
                return Error("Nie udało się utworzyć konta. Spróbuj ponownie.");

            IsLoginMode = true;
            LoginBanner = "Konto utworzone. Zaloguj się.";
            LoginBannerBrush = Brushes.SeaGreen;
            return true;
        }

        // ===== Live-checklista haseł =====
        public void UpdatePasswordHints(string password, string confirm)
        {
            var p = password ?? string.Empty;
            PwMinLen = p.Length >= 8;
            PwLower = Regex.IsMatch(p, "[a-z]");
            PwUpper = Regex.IsMatch(p, "[A-Z]");
            PwDigit = Regex.IsMatch(p, "\\d");
            PwSpecial = Regex.IsMatch(p, "[^\\w\\s]"); // znak spec. (nie litera/cyfra/whitespace)
            PwNoSpace = !Regex.IsMatch(p, "\\s");
            PwMatch = p == (confirm ?? string.Empty);
        }

        // ===== Helpery =====
        private bool Error(string msg)
        {
            RegisterIsError = true;
            RegisterMessage = msg ?? string.Empty;
            return false;
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

