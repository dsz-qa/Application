using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.ViewModels;
using Finly.Views;
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
        private bool _isEditingPersonal;

        private UserProfile _companyProfile = new();
        private bool _isEditingCompany;

        public AccountPage(int userId)
        {
            InitializeComponent();

            _userId = userId;
            _vm = new AccountViewModel(userId);
            DataContext = _vm;

            Loaded += AccountPage_Loaded;
        }

        private int ResolveUserId()
            => _userId > 0 ? _userId : UserService.GetCurrentUserId();

        private void AccountPage_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                var uid = ResolveUserId();
                if (uid <= 0)
                {
                    ToastService.Error("Brak zalogowanego użytkownika.");
                    return;
                }

                if (_vm.IsBusiness)
                    LoadCompanyDetails();
                else
                    LoadPersonalDetails();

                TogglePersonalEditMode(false);
                ToggleCompanyEditMode(false);
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd podczas ładowania strony Konto: " + ex.Message);
            }
        }

        // ===== Pomocnicze dla adresu firmy =====

        private static (string? City, string? Postal, string? Street, string? HouseNo)
            SplitCompanyAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return (null, null, null, null);

            string? city = null, postal = null, street = null, house = null;

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
                var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) city = parts[0].Trim();

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

        private static string? BuildCompanyAddress(string? city, string? postal, string? street, string? houseNo)
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
                    houseNo = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(houseNo))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(houseNo.Trim());
            }

            return sb.ToString();
        }

        // ===== Helpers null-safe =====

        private static void SetLabel(TextBlock? label, string? value)
        {
            if (label == null) return;
            label.Text = string.IsNullOrWhiteSpace(value) ? "nie podano" : value;
        }

        private static void SetText(TextBox? tb, string? value)
        {
            if (tb == null) return;
            tb.Text = value ?? "";
        }

        private static string? NullIfEmpty(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // ====== DANE OSOBOWE ======

        private void LoadPersonalDetails()
        {
            var uid = ResolveUserId();
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

            if (LblBirthDate != null)
            {
                LblBirthDate.Text = _personalDetails.BirthDate.HasValue
                    ? _personalDetails.BirthDate.Value.ToString("dd-MM-yyyy", CultureInfo.GetCultureInfo("pl-PL"))
                    : "nie podano";
            }

            SetText(TxtEmail, _personalDetails.Email);
            SetText(TxtFirstName, _personalDetails.FirstName);
            SetText(TxtLastName, _personalDetails.LastName);
            SetText(TxtPhone, _personalDetails.Phone);
            SetText(TxtCity, _personalDetails.City);
            SetText(TxtPostalCode, _personalDetails.PostalCode);
            SetText(TxtStreet, _personalDetails.Street);
            SetText(TxtHouseNo, _personalDetails.HouseNo);

            SetText(TxtBirthDate, _personalDetails.BirthDate.HasValue
                ? _personalDetails.BirthDate.Value.ToString("dd-MM-yyyy", CultureInfo.GetCultureInfo("pl-PL"))
                : "");
        }

        private void TogglePersonalEditMode(bool editing)
        {
            _isEditingPersonal = editing;

            if (LblEmail != null) LblEmail.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblFirstName != null) LblFirstName.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblLastName != null) LblLastName.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblBirthDate != null) LblBirthDate.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblPhone != null) LblPhone.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblCity != null) LblCity.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblPostalCode != null) LblPostalCode.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblStreet != null) LblStreet.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblHouseNo != null) LblHouseNo.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;

            if (TxtEmail != null) TxtEmail.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtFirstName != null) TxtFirstName.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtLastName != null) TxtLastName.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtBirthDate != null) TxtBirthDate.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtPhone != null) TxtPhone.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCity != null) TxtCity.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtPostalCode != null) TxtPostalCode.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtStreet != null) TxtStreet.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtHouseNo != null) TxtHouseNo.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

            if (BtnEditPersonal != null) BtnEditPersonal.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (BtnSavePersonal != null) BtnSavePersonal.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (BtnCancelPersonal != null) BtnCancelPersonal.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnEditPersonal_Click(object sender, RoutedEventArgs e) => TogglePersonalEditMode(true);

        private void BtnCancelPersonal_Click(object sender, RoutedEventArgs e)
        {
            LoadPersonalDetails();
            TogglePersonalEditMode(false);
        }

        private void BtnSavePersonal_Click(object sender, RoutedEventArgs e)
        {
            var uid = ResolveUserId();
            if (uid <= 0)
            {
                ToastService.Error("Brak zalogowanego użytkownika.");
                return;
            }

            var d = new PersonalDetails
            {
                Email = NullIfEmpty(TxtEmail?.Text),
                FirstName = NullIfEmpty(TxtFirstName?.Text),
                LastName = NullIfEmpty(TxtLastName?.Text),
                Phone = NullIfEmpty(TxtPhone?.Text),
                City = NullIfEmpty(TxtCity?.Text),
                PostalCode = NullIfEmpty(TxtPostalCode?.Text),
                Street = NullIfEmpty(TxtStreet?.Text),
                HouseNo = NullIfEmpty(TxtHouseNo?.Text)
            };

            if (!string.IsNullOrWhiteSpace(d.Email) && !d.Email!.Contains("@"))
            {
                ToastService.Info("Podaj poprawny adres e-mail (lub zostaw pole puste).");
                return;
            }

            var birthRaw = (TxtBirthDate?.Text ?? "").Trim();
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

                _vm.Refresh(); // spójność VM
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać danych: " + ex.Message);
            }
        }

        // ====== DANE FIRMY ======

        private void LoadCompanyDetails()
        {
            var uid = ResolveUserId();
            if (uid <= 0) return;

            _companyProfile = UserService.GetProfile(uid) ?? new UserProfile();

            SetLabel(LblCompanyName, _companyProfile.CompanyName);
            SetLabel(LblCompanyNip, _companyProfile.CompanyNip);
            SetLabel(LblCompanyRegon, _companyProfile.CompanyRegon);
            SetLabel(LblCompanyKrs, _companyProfile.CompanyKrs);

            var (city, postal, street, house) = SplitCompanyAddress(_companyProfile.CompanyAddress);

            SetLabel(LblCompanyCity, city);
            SetLabel(LblCompanyPostalCode, postal);
            SetLabel(LblCompanyStreet, street);
            SetLabel(LblCompanyHouseNo, house);

            SetText(TxtCompanyName, _companyProfile.CompanyName);
            SetText(TxtCompanyNip, _companyProfile.CompanyNip);
            SetText(TxtCompanyRegon, _companyProfile.CompanyRegon);
            SetText(TxtCompanyKrs, _companyProfile.CompanyKrs);

            SetText(TxtCompanyCity, city);
            SetText(TxtCompanyPostalCode, postal);
            SetText(TxtCompanyStreet, street);
            SetText(TxtCompanyHouseNo, house);
        }

        private void ToggleCompanyEditMode(bool editing)
        {
            _isEditingCompany = editing;

            if (LblCompanyName != null) LblCompanyName.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblCompanyNip != null) LblCompanyNip.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblCompanyRegon != null) LblCompanyRegon.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblCompanyKrs != null) LblCompanyKrs.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblCompanyCity != null) LblCompanyCity.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblCompanyPostalCode != null) LblCompanyPostalCode.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblCompanyStreet != null) LblCompanyStreet.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (LblCompanyHouseNo != null) LblCompanyHouseNo.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;

            if (TxtCompanyName != null) TxtCompanyName.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCompanyNip != null) TxtCompanyNip.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCompanyRegon != null) TxtCompanyRegon.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCompanyKrs != null) TxtCompanyKrs.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCompanyCity != null) TxtCompanyCity.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCompanyPostalCode != null) TxtCompanyPostalCode.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCompanyStreet != null) TxtCompanyStreet.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (TxtCompanyHouseNo != null) TxtCompanyHouseNo.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

            if (BtnEditCompany != null) BtnEditCompany.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            if (BtnSaveCompany != null) BtnSaveCompany.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (BtnCancelCompany != null) BtnCancelCompany.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnEditCompany_Click(object sender, RoutedEventArgs e) => ToggleCompanyEditMode(true);

        private void BtnCancelCompany_Click(object sender, RoutedEventArgs e)
        {
            LoadCompanyDetails();
            ToggleCompanyEditMode(false);
        }

        private void BtnSaveCompany_Click(object sender, RoutedEventArgs e)
        {
            var uid = ResolveUserId();
            if (uid <= 0)
            {
                ToastService.Error("Brak zalogowanego użytkownika.");
                return;
            }

            var p = UserService.GetProfile(uid) ?? new UserProfile();

            p.CompanyName = NullIfEmpty(TxtCompanyName?.Text);
            p.CompanyNip = NullIfEmpty(TxtCompanyNip?.Text);
            p.CompanyRegon = NullIfEmpty(TxtCompanyRegon?.Text);
            p.CompanyKrs = NullIfEmpty(TxtCompanyKrs?.Text);

            var city = NullIfEmpty(TxtCompanyCity?.Text);
            var postal = NullIfEmpty(TxtCompanyPostalCode?.Text);
            var street = NullIfEmpty(TxtCompanyStreet?.Text);
            var house = NullIfEmpty(TxtCompanyHouseNo?.Text);

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

                _vm.Refresh(); // spójność VM
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

            var oldPwd = PwdOld?.Password ?? "";
            var newPwd = PwdNew?.Password ?? "";
            var newPwd2 = PwdNew2?.Password ?? "";

            vm.ChangePassword(oldPwd, newPwd, newPwd2);
        }

        // ===== USUWANIE KONTA =====

        private void ShowDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (DeleteConfirmPanel != null)
                DeleteConfirmPanel.Visibility = Visibility.Visible;
        }

        private void CancelDelete_Click(object sender, RoutedEventArgs e)
        {
            if (DeleteConfirmPanel != null)
                DeleteConfirmPanel.Visibility = Visibility.Collapsed;
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            var uid = ResolveUserId();
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
