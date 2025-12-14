// using ... (bez zmian)
using Finly.Helpers;
using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace Finly.ViewModels
{
    public class AccountViewModel : INotifyPropertyChanged
    {
        private readonly int _userId;

        // ——— Dane główne ———
        public string Username { get; }
        public DateTime CreatedAt { get; }

        // E-mail pokazywany w sekcji „Dane użytkownika”
        private string? _emailDisplay;
        public string? EmailDisplay { get => _emailDisplay; private set { _emailDisplay = value; OnPropertyChanged(); } }

        // Typ konta
        public AccountType AccountType { get; }
        public bool IsBusiness => AccountType == AccountType.Business;
        public bool IsPersonal => AccountType == AccountType.Personal;

        // ——— Osobiste (do wyświetlenia) ———
        private string? _firstName, _lastName;
        private DateTime? _birthDate;
        private string? _city, _postalCode, _houseNo;

        public string? FirstName { get => _firstName; private set { _firstName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FirstNameDisplay)); OnPropertyChanged(nameof(ShowPersonalSection)); } }
        public string? LastName { get => _lastName; private set { _lastName = value; OnPropertyChanged(); OnPropertyChanged(nameof(LastNameDisplay)); OnPropertyChanged(nameof(ShowPersonalSection)); } }

        // Teksty „brak danych” dla UI
        public string FirstNameDisplay => string.IsNullOrWhiteSpace(FirstName) ? "brak danych" : FirstName!;
        public string LastNameDisplay => string.IsNullOrWhiteSpace(LastName) ? "brak danych" : LastName!;

        public string BirthDateDisplay =>
            _birthDate is null ? "brak danych" : _birthDate.Value.ToString("yyyy-MM-dd");

        // Złożony adres do pokazania w profilu
        public string AddressDisplay
        {
            get
            {
                var parts = new[]
                {
                    string.IsNullOrWhiteSpace(_city) ? null : _city!.Trim(),
                    string.IsNullOrWhiteSpace(_postalCode) ? null : _postalCode!.Trim(),
                    string.IsNullOrWhiteSpace(_houseNo) ? null : $"nr {_houseNo!.Trim()}",
                };
                var filtered = parts.Where(p => !string.IsNullOrWhiteSpace(p));
                var joined = string.Join(", ", filtered);
                return string.IsNullOrWhiteSpace(joined) ? "brak danych" : joined;
            }
        }

        public bool ShowPersonalSection =>
            IsPersonal && (!string.IsNullOrWhiteSpace(FirstName)
                        || !string.IsNullOrWhiteSpace(LastName)
                        || !string.IsNullOrWhiteSpace(_city)
                        || !string.IsNullOrWhiteSpace(_postalCode)
                        || !string.IsNullOrWhiteSpace(_houseNo));

        // ——— Firmowe ———
        private string? _companyName, _companyNip, _companyAddress;
        public string? CompanyName { get => _companyName; private set { _companyName = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowCompanySection)); } }
        public string? CompanyNip { get => _companyNip; private set { _companyNip = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowCompanySection)); } }
        public string? CompanyAddress { get => _companyAddress; private set { _companyAddress = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowCompanySection)); } }

        public bool ShowCompanySection =>
            IsBusiness && (!string.IsNullOrWhiteSpace(CompanyName)
                        || !string.IsNullOrWhiteSpace(CompanyNip)
                        || !string.IsNullOrWhiteSpace(CompanyAddress));

        // Banki/rachunki (bez zmian)
        public ObservableCollection<BankConnectionModel> BankConnections { get; } = new();
        public ObservableCollection<BankAccountModel> BankAccounts { get; } = new();
        public ICommand ConnectBankCommand { get; }
        public ICommand DisconnectBankCommand { get; }
        public ICommand SyncNowCommand { get; }

        // Zmiana hasła (bez zmian)
        private string? _pwMsg; private Brush? _pwBrush;
        public string? PasswordChangeMessage { get => _pwMsg; set { _pwMsg = value; OnPropertyChanged(); } }
        public Brush? PasswordChangeBrush { get => _pwBrush; set { _pwBrush = value; OnPropertyChanged(); } }

        public AccountViewModel(int userId)
        {
            _userId = userId;

            Username = UserService.GetUsername(userId);
            CreatedAt = UserService.GetCreatedAt(userId);
            AccountType = UserService.GetAccountType(userId);

            // Załaduj wszystko w jednym miejscu
            Refresh();

            ConnectBankCommand = new RelayCommand(_ => ConnectBank());
            DisconnectBankCommand = new RelayCommand(c =>
            {
                if (c is BankConnectionModel m)
                {
                    OpenBankingService.Disconnect(m.Id);
                    LoadBanks();
                }
            });
            SyncNowCommand = new RelayCommand(_ => { OpenBankingService.SyncNow(_userId); LoadBanks(); });
        }

        /// <summary>Świeże dane profilu + e-mail + banki</summary>
        public void Refresh()
        {
            // e-mail z Users
            EmailDisplay = UserService.GetEmail(_userId);

            // dane osobiste/firmowe
            var p = UserService.GetProfile(_userId);
            FirstName = p.FirstName;
            LastName = p.LastName;

            // Data urodzenia – preferujemy pełną datę z Users.BirthDate
            var d = UserService.GetPersonalDetails(_userId);   // zawiera Email, City, PostalCode, HouseNo, BirthDate
            _birthDate = d.BirthDate;

            _city = d.City;
            _postalCode = d.PostalCode;
            _houseNo = d.HouseNo;

            CompanyName = p.CompanyName;
            CompanyNip = p.CompanyNip;
            CompanyAddress = p.CompanyAddress;

            // poinformuj widok, że AddressDisplay się zmieniło
            OnPropertyChanged(nameof(AddressDisplay));

            LoadBanks();
        }

        private void LoadBanks()
        {
            BankConnections.Clear();
            foreach (var c in OpenBankingService.GetConnections(_userId))
                BankConnections.Add(c);

            BankAccounts.Clear();
            foreach (var a in OpenBankingService.GetAccounts(_userId))
                BankAccounts.Add(a);
        }

        private void ConnectBank()
        {
            if (OpenBankingService.ConnectDemo(_userId))
                LoadBanks();
        }

        public void ChangePassword(string oldPwd, string newPwd, string newPwdRepeat)
        {
            if (string.IsNullOrWhiteSpace(newPwd) || newPwd != newPwdRepeat)
            {
                PasswordChangeMessage = "Hasła nie są takie same.";
                PasswordChangeBrush = Brushes.IndianRed;
                return;
            }

            var ok = UserService.ChangePassword(_userId, oldPwd, newPwd);
            PasswordChangeMessage = ok ? "Zmieniono hasło." : "Błędne obecne hasło.";
            PasswordChangeBrush = ok ? Brushes.LimeGreen : Brushes.IndianRed;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}





