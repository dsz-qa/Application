using Finly.Helpers;
using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
        public string? EmailDisplay
        {
            get => _emailDisplay;
            private set { _emailDisplay = value; OnPropertyChanged(); }
        }

        // Typ konta
        public AccountType AccountType { get; }
        public bool IsBusiness => AccountType == AccountType.Business;
        public bool IsPersonal => AccountType == AccountType.Personal;

        // ——— Osobiste (do wyświetlenia) ———
        private string? _firstName, _lastName;
        private DateTime? _birthDate;
        private string? _city, _postalCode, _houseNo;

        public string? FirstName
        {
            get => _firstName;
            private set
            {
                _firstName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FirstNameDisplay));
                OnPropertyChanged(nameof(ShowPersonalSection));
            }
        }

        public string? LastName
        {
            get => _lastName;
            private set
            {
                _lastName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastNameDisplay));
                OnPropertyChanged(nameof(ShowPersonalSection));
            }
        }

        public string FirstNameDisplay => string.IsNullOrWhiteSpace(FirstName) ? "brak danych" : FirstName!;
        public string LastNameDisplay => string.IsNullOrWhiteSpace(LastName) ? "brak danych" : LastName!;

        public string BirthDateDisplay =>
            _birthDate is null ? "brak danych" : _birthDate.Value.ToString("yyyy-MM-dd");

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

                var joined = string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
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

        public string? CompanyName
        {
            get => _companyName;
            private set
            {
                _companyName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCompanySection));
            }
        }

        public string? CompanyNip
        {
            get => _companyNip;
            private set
            {
                _companyNip = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCompanySection));
            }
        }

        public string? CompanyAddress
        {
            get => _companyAddress;
            private set
            {
                _companyAddress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCompanySection));
            }
        }

        public bool ShowCompanySection =>
            IsBusiness && (!string.IsNullOrWhiteSpace(CompanyName)
                        || !string.IsNullOrWhiteSpace(CompanyNip)
                        || !string.IsNullOrWhiteSpace(CompanyAddress));

        // ——— Banki / rachunki ———
        public ObservableCollection<BankConnectionModel> BankConnections { get; } = new();
        public ObservableCollection<BankAccountModel> BankAccounts { get; } = new();

        public ICommand ConnectBankCommand { get; }
        public ICommand DisconnectBankCommand { get; }
        public ICommand SyncNowCommand { get; }

        // ——— Zmiana hasła ———
        private string? _pwMsg;
        private Brush? _pwBrush;

        public string? PasswordChangeMessage
        {
            get => _pwMsg;
            set { _pwMsg = value; OnPropertyChanged(); }
        }

        public Brush? PasswordChangeBrush
        {
            get => _pwBrush;
            set { _pwBrush = value; OnPropertyChanged(); }
        }

        public AccountViewModel(int userId)
        {
            _userId = userId;

            // Jeśli userId jest 0, to i tak nie wywalaj wyjątku: UI ma się otworzyć i pokazać komunikat
            if (_userId <= 0)
            {
                Username = "brak danych";
                CreatedAt = DateTime.MinValue;
                AccountType = AccountType.Personal;

                ConnectBankCommand = new RelayCommand(_ => { });
                DisconnectBankCommand = new RelayCommand(_ => { });
                SyncNowCommand = new RelayCommand(_ => { });

                PasswordChangeMessage = "Brak zalogowanego użytkownika.";
                PasswordChangeBrush = Brushes.IndianRed;
                return;
            }

            // Te metody mogą rzucać wyjątki, więc osłaniamy konstruktor
            try
            {
                Username = UserService.GetUsername(_userId);
                CreatedAt = UserService.GetCreatedAt(_userId);
                AccountType = UserService.GetAccountType(_userId);
            }
            catch
            {
                Username = "brak danych";
                CreatedAt = DateTime.MinValue;
                AccountType = AccountType.Personal;
            }

            // Komendy
            ConnectBankCommand = new RelayCommand(_ => ConnectBank());
            DisconnectBankCommand = new RelayCommand(c =>
            {
                if (c is BankConnectionModel m)
                {
                    try
                    {
                        OpenBankingService.Disconnect(m.Id);
                    }
                    catch
                    {
                        // celowo pomijamy – ewentualnie toast w UI
                    }
                    LoadBanksSafe();
                }
            });
            SyncNowCommand = new RelayCommand(_ =>
            {
                try
                {
                    OpenBankingService.SyncNow(_userId);
                }
                catch
                {
                    // celowo pomijamy
                }
                LoadBanksSafe();
            });

            // Załaduj dane
            Refresh();
        }

        public void Refresh()
        {
            if (_userId <= 0) return;

            try
            {
                EmailDisplay = UserService.GetEmail(_userId);

                // PROFIL może nie istnieć
                var p = UserService.GetProfile(_userId) ?? new UserProfile();
                FirstName = p.FirstName;
                LastName = p.LastName;

                // DANE OSOBOWE mogą nie istnieć
                var d = UserService.GetPersonalDetails(_userId) ?? new PersonalDetails();
                _birthDate = d.BirthDate;
                _city = d.City;
                _postalCode = d.PostalCode;
                _houseNo = d.HouseNo;

                CompanyName = p.CompanyName;
                CompanyNip = p.CompanyNip;
                CompanyAddress = p.CompanyAddress;

                OnPropertyChanged(nameof(AddressDisplay));

                LoadBanksSafe();
            }
            catch
            {
                // W krytycznym przypadku nie wywalaj strony
                PasswordChangeMessage = "Nie udało się wczytać danych konta.";
                PasswordChangeBrush = Brushes.IndianRed;
            }
        }

        private void LoadBanksSafe()
        {
            BankConnections.Clear();
            BankAccounts.Clear();

            try
            {
                foreach (var c in OpenBankingService.GetConnections(_userId))
                    BankConnections.Add(c);
            }
            catch
            {
                // brak integracji / błąd w serwisie – UI ma działać dalej
            }

            try
            {
                foreach (var a in OpenBankingService.GetAccounts(_userId))
                    BankAccounts.Add(a);
            }
            catch
            {
                // jw.
            }
        }

        private void ConnectBank()
        {
            try
            {
                if (OpenBankingService.ConnectDemo(_userId))
                    LoadBanksSafe();
            }
            catch
            {
                // jw.
            }
        }

        public void ChangePassword(string oldPwd, string newPwd, string newPwdRepeat)
        {
            if (_userId <= 0)
            {
                PasswordChangeMessage = "Brak zalogowanego użytkownika.";
                PasswordChangeBrush = Brushes.IndianRed;
                return;
            }

            if (string.IsNullOrWhiteSpace(newPwd) || newPwd != newPwdRepeat)
            {
                PasswordChangeMessage = "Hasła nie są takie same.";
                PasswordChangeBrush = Brushes.IndianRed;
                return;
            }

            bool ok;
            try
            {
                ok = UserService.ChangePassword(_userId, oldPwd, newPwd);
            }
            catch
            {
                ok = false;
            }

            PasswordChangeMessage = ok ? "Zmieniono hasło." : "Błędne obecne hasło.";
            PasswordChangeBrush = ok ? Brushes.LimeGreen : Brushes.IndianRed;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
