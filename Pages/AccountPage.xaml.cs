using Finly.Services;
using Finly.ViewModels;
using Finly.Views;              // ShellWindow, AuthWindow
using Finly.Views.Dialogs;      // ConfirmDialog
using Finly.Models;
using System;
using System.Globalization;
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

        public AccountPage(int userId)
        {
            InitializeComponent();
            _userId = userId;
            _vm = new AccountViewModel(userId);
            DataContext = _vm;

            Loaded += (_, __) =>
            {
                LoadPersonalDetails();
            };
        }

        // ====== DANE OSOBOWE – ODCZYT I TRYB EDYCJI ======

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

            // jednocześnie uzupełniamy textboxy (na wypadek wejścia w edycję)
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

            // TextBlocki
            LblEmail.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblFirstName.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblLastName.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblBirthDate.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblPhone.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblCity.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblPostalCode.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblStreet.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            LblHouseNo.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;

            // TextBoxy
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

            // Walidacja e-maila (prosta – czy coś jest + @)
            if (!string.IsNullOrWhiteSpace(d.Email) && !d.Email!.Contains("@"))
            {
                ToastService.Info("Podaj poprawny adres e-mail (lub zostaw pole puste).");
                return;
            }

            // Data urodzenia
            var birthRaw = (TxtBirthDate.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(birthRaw))
            {
                if (!DateTime.TryParseExact(
                        birthRaw,
                        "dd-MM-yyyy",
                        CultureInfo.GetCultureInfo("pl-PL"),
                        System.Globalization.DateTimeStyles.None,
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

        // ===== HASŁO =====

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AccountViewModel vm) return;

            var oldPwd = PwdOld.Password;
            var newPwd = PwdNew.Password;
            var newPwd2 = PwdNew2.Password;

            vm.ChangePassword(oldPwd, newPwd, newPwd2);
        }

        // ===== USUWANIE KONTA UŻYTKOWNIKA =====
        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            var uid = UserService.GetCurrentUserId();
            if (uid <= 0)
            {
                ToastService.Error("Brak zalogowanego użytkownika.");
                return;
            }

            var dlg = new ConfirmDialog(
                "Usunąć konto użytkownika?\n\n" +
                "Tej operacji nie można cofnąć. Zostaną usunięte wszystkie Twoje dane: " +
                "rachunki, transakcje, budżety, kategorie itp.")
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() == true && dlg.Result)
            {
                try
                {
                    DatabaseService.DeleteUserCascade(uid);
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

        private void ShowDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmPanel.Visibility = Visibility.Visible;
        }

        private void CancelDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmPanel.Visibility = Visibility.Collapsed;
        }

        private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            var uid = UserService.GetCurrentUserId();
            if (uid <= 0)
            {
                ToastService.Error("Brak zalogowanego użytkownika.");
                return;
            }

            try
            {
                DatabaseService.DeleteUserCascade(uid);
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










