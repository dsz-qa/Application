using Finly.Models;
using Finly.Services;
using Finly.ViewModels;
using Finly.Views;              // AuthWindow
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class AccountPage : UserControl
    {
        private readonly int _userId;
        private readonly AccountViewModel _vm;

        private PersonalDetails _personalDetails = new();
        private bool _isEditingPersonal = false;

        private UserProfile _companyProfile = new();
        private bool _isEditingCompany = false;

        public AccountPage(int userId)
        {
            InitializeComponent();
            _userId = userId;
            _vm = new AccountViewModel(userId);
            DataContext = _vm;

            Loaded += AccountPage_Loaded;
        }

        private void AccountPage_Loaded(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsBusiness)
                LoadCompanyDetails();
            else
                LoadPersonalDetails();
        }

        // ===== Pomocnicze dla adresu firmy =====

        private static (string? City, string? Postal, string? Street, string? HouseNo)
            SplitCompanyAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return (null, null, null, null);

            string? city = null, postal = null, street = null, house = null;

            // Szukamy kodu pocztowego 00-000
            var m = Regex.Match(address, @"\b\d{2}-\d{3}\b");
            if (m.Success)
            {
                postal = m.Value;
                var before = address[..m.Index].Trim(',', ' ', '\t');
                var after = address[(m.Index + m.Length)..].Trim(',', ' ', '\t');

                if (!string.IsNullOrEmpty(before))
                    city = before;

                if (!string.IsNullOrEmpty(after))
                {
                    var lastSpace = after.LastIndexOf(' ');
                    if (lastSpace > 0 && lastSpace < after.Length - 1)
                    {
                        street = after[..lastSpace].Trim();
                        house = after[(lastSpace + 1)..].Trim();
                    }
                    else
                    {
                        street = after.Trim();
                    }
                }
            }
            else
            {
                // fallback: "Miasto, Ulica 10"
                var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                    city = parts[0].Trim();
                if (parts.Length > 1)
                {
                    var rest = parts[1].Trim();
                    var lastSpace = rest.LastIndexOf(' ');
                    if (lastSpace > 0 && lastSpace < rest.Length - 1)
                    {
                        street = rest[..lastSpace].Trim();
                        house = rest[(lastSpace + 1)..].Trim();
                    }
                    else
                    {
                        street = rest;
                    }
                }
            }

            return (city, postal, street, house);
        }

        private static string? BuildCompanyAddress(
            string? city, string? postal, string? street, string? houseNo)
        {
            if (string.IsNullOrWhiteSpace(city) &&
                string.IsNullOrWhiteSpace(postal) &&
                string.IsNullOrWhiteSpace(street) &&
                string.IsNullOrWhiteSpace(houseNo))
                return null;

            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(city))
                sb.Append(city.Trim());

            if (!string.IsNullOrWhiteSpace(postal))
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(postal.Trim());
            }

            if (!string.IsNullOrWhiteSpace(street))
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(street.Trim());

                if (!string.IsNullOrWhiteSpace(houseNo))
                {
                    sb.Append(' ');
                    sb.Append(houseNo.Trim());
                    houseNo = null; // żeby niżej nie dodać drugi raz
                }
            }

            if (!string.IsNullOrWhiteSpace(houseNo))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(houseNo.Trim());
            }

            return sb.ToString();
        }

        // ====== DANE OSOBOWE ======

        private static void SetLabel(TextBlock label, string? value)
        {
            label.Text = string.IsNullOrWhiteSpace(value) ? "nie podano" : value;
        }

        private void LoadPersonalDetails()
        {
            var uid = UserService.GetCurrentUserId();
            if (uid <= 0) return;

            _personalDetails = UserService.GetPersonalDetails(uid) ?? new PersonalDetails();

            SetLabel(LblEmail, _personalDetails.Email);
            SetLabel(LblFirstName, _personalDetails.FirstName);
            SetLabel(LblLastName, _personalDetails.LastName);
            SetLabel(LblPhone, _personalDetails.Phone);
            SetLabel(LblCity, _personalDetails.City);
            SetLabel(LblPostalCode, _personalDetails.PostalCode);
            SetLabel(LblStreet, _personalDetails.Street);
            SetLabel(LblHouseNo, _personalDetails.HouseNo);

            LblBirthDate.Text = _personalDetails.BirthDate.HasValue
                ? _personalDetails.BirthDate.Value.ToString("dd-MM-yyyy", CultureInfo.GetCultureInfo("pl-PL"))
                : "nie podano";

            TxtEmail.Text = _personalDetails.Email ?? "";
            TxtFirstName.Text = _personalDetails.FirstName ?? "";
            TxtLastName.Text = _personalDetails.LastName ?? "";
            TxtPhone.Text = _personalDetails.Phone ?? "";
            TxtCity.Text = _personalDetails.City ?? "";
            TxtPostalCode.Text = _personalDetails.PostalCode ?? "";
            TxtStreet.Text = _personalDetails.Street ?? "";
            TxtHouseNo.Text = _personalDetails.HouseNo ?? "";
            TxtBirthDate.Text = _personalDetails.BirthDate.HasValue
                ? _personalDetails.BirthDate.Value.ToString("dd-MM-yyyy", CultureInfo.GetCultureInfo("pl-PL"))
                : "";
        }

        private void TogglePersonalEditMode(bool editing)
        {
            _isEditingPersonal = editing;

            LblEmail.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblFirstName.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblLastName.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblBirthDate.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblPhone.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblCity.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblPostalCode.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblStreet.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblHouseNo.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;

            TxtEmail.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtFirstName.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtLastName.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtBirthDate.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtPhone.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtCity.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtPostalCode.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtStreet.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtHouseNo.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

            BtnEditPersonal.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            BtnSavePersonal.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            BtnCancelPersonal.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnEditPersonal_Click(object sender, RoutedEventArgs e)
        {
            TogglePersonalEditMode(true);
        }

        private void BtnCancelPersonal_Click(object sender, RoutedEventArgs e)
        {
            LoadPersonalDetails();
            TogglePersonalEditMode(false);
        }

        private static string? NullIfEmpty(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private void BtnSavePersonal_Click(object sender, RoutedEventArgs e)
        {
            var uid = UserService.GetCurrentUserId();
            if (uid <= 0)
            {
                ToastService.Error("Brak zalogowanego użytkownika.");
                return;
            }

            var d = new PersonalDetails
            {
                Email = NullIfEmpty(TxtEmail.Text),
                FirstName = NullIfEmpty(TxtFirstName.Text),
                LastName = NullIfEmpty(TxtLastName.Text),
                Phone = NullIfEmpty(TxtPhone.Text),
                City = NullIfEmpty(TxtCity.Text),
                PostalCode = NullIfEmpty(TxtPostalCode.Text),
                Street = NullIfEmpty(TxtStreet.Text),
                HouseNo = NullIfEmpty(TxtHouseNo.Text)
            };

            if (!string.IsNullOrWhiteSpace(d.Email) && !d.Email!.Contains("@"))
            {
                ToastService.Info("Podaj poprawny adres e-mail (lub zostaw pole puste).");
                return;
            }

            var birthRaw = (TxtBirthDate.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(birthRaw))
            {
                if (!DateTime.TryParseExact(
                        birthRaw,
                        "dd-MM-yyyy",
                        CultureInfo.GetCultureInfo("pl-PL"),
                        DateTimeStyles.None,
                        out var bd))
                {
                    ToastService.Info("Data urodzenia musi mieć format DD-MM-RRRR.");
                    return;
                }
                d.BirthDate = bd;
            }
            else
            {
                d.BirthDate = null;
            }

            try
            {
                UserService.UpdatePersonalDetails(uid, d);
                ToastService.Success("Zapisano dane osobowe.");

                LoadPersonalDetails();
                TogglePersonalEditMode(false);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać danych: " + ex.Message);
            }
        }

        // ====== DANE FIRMY ======

        private void LoadCompanyDetails()
        {
            var uid = UserService.GetCurrentUserId();
            if (uid <= 0) return;

            _companyProfile = UserService.GetProfile(uid) ?? new UserProfile();

            SetLabel(LblCompanyName, _companyProfile.CompanyName);
            SetLabel(LblCompanyNip, _companyProfile.CompanyNip);
            SetLabel(LblCompanyRegon, _companyProfile.CompanyRegon);
            SetLabel(LblCompanyKrs, _companyProfile.CompanyKrs);

            var (city, postal, street, house) =
                SplitCompanyAddress(_companyProfile.CompanyAddress);

            SetLabel(LblCompanyCity, city);
            SetLabel(LblCompanyPostalCode, postal);
            SetLabel(LblCompanyStreet, street);
            SetLabel(LblCompanyHouseNo, house);

            TxtCompanyName.Text = _companyProfile.CompanyName ?? "";
            TxtCompanyNip.Text = _companyProfile.CompanyNip ?? "";
            TxtCompanyRegon.Text = _companyProfile.CompanyRegon ?? "";
            TxtCompanyKrs.Text = _companyProfile.CompanyKrs ?? "";

            TxtCompanyCity.Text = city ?? "";
            TxtCompanyPostalCode.Text = postal ?? "";
            TxtCompanyStreet.Text = street ?? "";
            TxtCompanyHouseNo.Text = house ?? "";
        }

        private void ToggleCompanyEditMode(bool editing)
        {
            _isEditingCompany = editing;

            LblCompanyName.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblCompanyNip.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblCompanyRegon.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblCompanyKrs.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblCompanyCity.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblCompanyPostalCode.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblCompanyStreet.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblCompanyHouseNo.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;

            TxtCompanyName.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtCompanyNip.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtCompanyRegon.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtCompanyKrs.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtCompanyCity.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtCompanyPostalCode.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtCompanyStreet.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            TxtCompanyHouseNo.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

            BtnEditCompany.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            BtnSaveCompany.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            BtnCancelCompany.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnEditCompany_Click(object sender, RoutedEventArgs e)
        {
            ToggleCompanyEditMode(true);
        }

        private void BtnCancelCompany_Click(object sender, RoutedEventArgs e)
        {
            LoadCompanyDetails();
            ToggleCompanyEditMode(false);
        }

        private void BtnSaveCompany_Click(object sender, RoutedEventArgs e)
        {
            var uid = UserService.GetCurrentUserId();
            if (uid <= 0)
            {
                ToastService.Error("Brak zalogowanego użytkownika.");
                return;
            }

            var p = UserService.GetProfile(uid) ?? new UserProfile();
            p.CompanyName = NullIfEmpty(TxtCompanyName.Text);
            p.CompanyNip = NullIfEmpty(TxtCompanyNip.Text);
            p.CompanyRegon = NullIfEmpty(TxtCompanyRegon.Text);
            p.CompanyKrs = NullIfEmpty(TxtCompanyKrs.Text);

            var city = NullIfEmpty(TxtCompanyCity.Text);
            var postal = NullIfEmpty(TxtCompanyPostalCode.Text);
            var street = NullIfEmpty(TxtCompanyStreet.Text);
            var house = NullIfEmpty(TxtCompanyHouseNo.Text);

            p.CompanyAddress = BuildCompanyAddress(city, postal, street, house);

            if (string.IsNullOrWhiteSpace(p.CompanyName))
            {
                ToastService.Info("Podaj nazwę firmy.");
                return;
            }
            if (string.IsNullOrWhiteSpace(p.CompanyNip))
            {
                ToastService.Info("Podaj NIP firmy.");
                return;
            }

            try
            {
                UserService.UpdateProfile(uid, p);
                ToastService.Success("Zapisano dane firmy.");

                LoadCompanyDetails();
                ToggleCompanyEditMode(false);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać danych firmy: " + ex.Message);
            }
        }

        // ===== HASŁO =====

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AccountViewModel vm) return;

            var oldPwd = PwdOld.Password;
            var newPwd = PwdNew.Password;
            var newPwd2 = PwdNew2.Password;

            vm.ChangePassword(oldPwd, newPwd, newPwd2);
        }

        // ===== USUWANIE KONTA =====

        private void ShowDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmPanel.Visibility = Visibility.Visible;
        }

        private void CancelDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmPanel.Visibility = Visibility.Collapsed;
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            var uid = UserService.GetCurrentUserId();
            if (uid <= 0)
            {
                ToastService.Error("Brak zalogowanego użytkownika.");
                return;
            }

            try
            {
                if (!UserService.DeleteAccount(uid))
                {
                    ToastService.Error("Nie udało się usunąć konta.");
                    return;
                }

                UserService.ClearCurrentUser();
                ToastService.Success("Twoje konto zostało usunięte.");

                var auth = new AuthWindow();
                Application.Current.MainWindow = auth;
                auth.Show();

                Window.GetWindow(this)?.Close();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się usunąć konta: " + ex.Message);
            }
        }
    }
}
