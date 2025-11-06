using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Finly.Models;
using Finly.Services;

namespace Finly.ViewModels
{
    public class EditPersonalViewModel : INotifyPropertyChanged
    {
        private readonly int _userId;

        public string? Email { get => _email; set { _email = value; OnPropertyChanged(); } }
        public string? FirstName { get => _firstName; set { _firstName = value; OnPropertyChanged(); } }
        public string? LastName { get => _lastName; set { _lastName = value; OnPropertyChanged(); } }
        // w UI mamy pole „Rok urodzenia” – trzymamy je jako string i mapujemy na BirthDate (YYYY-01-01)
        public string? BirthYear { get => _birthYear; set { _birthYear = value; OnPropertyChanged(); } }
        public string? City { get => _city; set { _city = value; OnPropertyChanged(); } }
        public string? PostalCode { get => _postalCode; set { _postalCode = value; OnPropertyChanged(); } }
        public string? HouseNo { get => _houseNo; set { _houseNo = value; OnPropertyChanged(); } }

        private string? _email, _firstName, _lastName, _birthYear, _city, _postalCode, _houseNo;

        public EditPersonalViewModel(int userId)
        {
            _userId = userId;

            var d = UserService.GetPersonalDetails(userId);
            Email = d.Email;
            FirstName = d.FirstName;
            LastName = d.LastName;
            BirthYear = d.BirthDate?.Year.ToString(); // z pełnej daty do pola „rok”
            City = d.City;
            PostalCode = d.PostalCode;
            HouseNo = d.HouseNo;
        }

        public bool Save()
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                ToastService.Error("Podaj adres e-mail.");
                return false;
            }

            int? by = null;
            if (!string.IsNullOrWhiteSpace(BirthYear) && int.TryParse(BirthYear, out var tmp))
                by = tmp;

            var details = new PersonalDetails
            {
                Email = Email,
                FirstName = FirstName,
                LastName = LastName,
                BirthDate = by.HasValue ? new DateTime(by.Value, 1, 1) : (DateTime?)null,
                City = City,
                PostalCode = PostalCode,
                HouseNo = HouseNo
            };

            UserService.UpdatePersonalDetails(_userId, details);

            // odśwież cache e-maila bieżącego użytkownika
            if (UserService.CurrentUserId == _userId)
                UserService.CurrentUserEmail = Email;

            ToastService.Success("Zapisano dane osobowe.");
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}


