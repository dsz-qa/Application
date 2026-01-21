using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Finly.Models;

namespace Finly.Views.Dialogs
{
    public partial class EditLoanDialog : Window
    {
        public enum Mode { Add, Edit }

        public Mode DialogMode { get; private set; } = Mode.Add;

        public LoanModel ResultLoan { get; private set; } = new();
        public int? SelectedAccountId { get; private set; }
        public string? AttachedSchedulePath { get; private set; }

        private readonly List<BankAccountModel> _accounts;

        // Konstruktor bezparametrowy (bezpieczny fallback / designer)
        public EditLoanDialog() : this(new List<BankAccountModel>())
        {
        }

        public EditLoanDialog(List<BankAccountModel> accounts)
        {
            InitializeComponent();

            _accounts = accounts ?? new List<BankAccountModel>();
            AccountCombo.ItemsSource = _accounts;

            BuildPaymentDays();
            SetMode(Mode.Add);

            Loaded += (_, __) =>
            {
                StartDatePicker.SelectedDate ??= DateTime.Today;
                NameTextBox.Focus();
            };
        }

        public void SetMode(Mode mode)
        {
            DialogMode = mode;

            HeaderTitleText.Text = mode == Mode.Add ? "Dodawanie kredytu" : "Edycja kredytu";
            HeaderSubtitleText.Text = mode == Mode.Add
                ? "Uzupełnij dane kredytu, a następnie zapisz."
                : "Zmień dane kredytu, a następnie zapisz zmiany.";

            TitleText.Text = "Kredyt";
        }

        public void LoadLoan(LoanModel loan, int? accountId, string? schedulePath)
        {
            if (loan == null) throw new ArgumentNullException(nameof(loan));

            SetMode(Mode.Edit);

            ResultLoan = new LoanModel
            {
                Id = loan.Id,
                UserId = loan.UserId,
                Name = loan.Name,
                Principal = loan.Principal,
                InterestRate = loan.InterestRate,
                StartDate = loan.StartDate,
                TermMonths = loan.TermMonths,
                PaymentDay = loan.PaymentDay
            };

            NameTextBox.Text = ResultLoan.Name;
            PrincipalTextBox.Text = ResultLoan.Principal.ToString(CultureInfo.CurrentCulture);
            InterestTextBox.Text = ResultLoan.InterestRate.ToString(CultureInfo.CurrentCulture);
            TermTextBox.Text = ResultLoan.TermMonths.ToString(CultureInfo.CurrentCulture);
            StartDatePicker.SelectedDate = ResultLoan.StartDate;

            if (accountId.HasValue)
            {
                var acc = _accounts.FirstOrDefault(a => a.Id == accountId.Value);
                AccountCombo.SelectedItem = acc;
                SelectedAccountId = acc?.Id;
            }
            else
            {
                AccountCombo.SelectedIndex = -1;
                SelectedAccountId = null;
            }

            SelectPaymentDay(ResultLoan.PaymentDay);

            AttachedSchedulePath = schedulePath;
            ScheduleFileText.Text = string.IsNullOrWhiteSpace(schedulePath) ? "Brak pliku" : Path.GetFileName(schedulePath);
        }

        private void BuildPaymentDays()
        {
            PaymentDayCombo.Items.Clear();
            PaymentDayCombo.Items.Add(new ComboBoxItem { Content = "Nie ustawiono", Tag = 0 });

            for (int i = 1; i <= 31; i++)
                PaymentDayCombo.Items.Add(new ComboBoxItem { Content = i.ToString(CultureInfo.InvariantCulture), Tag = i });

            PaymentDayCombo.SelectedIndex = 0;
        }

        private void SelectPaymentDay(int paymentDay)
        {
            for (int i = 0; i < PaymentDayCombo.Items.Count; i++)
            {
                if (PaymentDayCombo.Items[i] is ComboBoxItem ci &&
                    ci.Tag != null &&
                    int.TryParse(ci.Tag.ToString(), out var tagVal) &&
                    tagVal == paymentDay)
                {
                    PaymentDayCombo.SelectedIndex = i;
                    return;
                }
            }
            PaymentDayCombo.SelectedIndex = 0;
        }

        // ======= TitleBar =======
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ======= UI helpers =======
        private void AnyFieldChanged(object? sender, EventArgs e)
        {
            HideInlineError();
        }

        private void ShowInlineError(string message, Control? focus = null)
        {
            InlineErrorText.Text = message;
            InlineErrorText.Visibility = Visibility.Visible;
            focus?.Focus();
        }

        private void HideInlineError()
        {
            InlineErrorText.Text = string.Empty;
            InlineErrorText.Visibility = Visibility.Collapsed;
        }

        private void AttachSchedule_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Pliki CSV|*.csv|Pliki PDF|*.pdf|Wszystkie pliki|*.*"
            };

            var ok = dlg.ShowDialog();
            if (ok != true) return;

            AttachedSchedulePath = dlg.FileName;
            ScheduleFileText.Text = Path.GetFileName(dlg.FileName);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            HideInlineError();

            var name = (NameTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowInlineError("Podaj nazwę kredytu.", NameTextBox);
                return;
            }

            if (!TryParseDecimal(PrincipalTextBox.Text, out var principal) || principal < 0m)
            {
                ShowInlineError("Podaj poprawną kwotę kapitału.", PrincipalTextBox);
                return;
            }

            if (!TryParseDecimal(InterestTextBox.Text, out var interest) || interest < 0m)
            {
                ShowInlineError("Podaj poprawne oprocentowanie.", InterestTextBox);
                return;
            }

            if (!int.TryParse((TermTextBox.Text ?? "").Trim(), out var termMonths) || termMonths < 0)
            {
                ShowInlineError("Podaj poprawny okres w miesiącach.", TermTextBox);
                return;
            }

            var start = StartDatePicker.SelectedDate ?? DateTime.Today;

            int paymentDay = 0;
            if (PaymentDayCombo.SelectedItem is ComboBoxItem ci && ci.Tag != null)
                int.TryParse(ci.Tag.ToString(), out paymentDay);

            int? selectedAccId = null;
            if (AccountCombo.SelectedItem is BankAccountModel acc)
                selectedAccId = acc.Id;

            ResultLoan.Name = name;
            ResultLoan.Principal = principal;
            ResultLoan.InterestRate = interest;
            ResultLoan.TermMonths = termMonths;
            ResultLoan.StartDate = start;
            ResultLoan.PaymentDay = paymentDay;

            SelectedAccountId = selectedAccId;

            DialogResult = true;
            Close();
        }

        private static bool TryParseDecimal(string? raw, out decimal value)
        {
            value = 0m;
            raw = (raw ?? "").Trim();

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Replace("\u00A0", " ").Replace("\u202F", " ").Replace(" ", "");

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
                return true;

            return decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }
    }
}
