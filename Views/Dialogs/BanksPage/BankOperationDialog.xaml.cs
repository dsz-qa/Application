using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Views.Dialogs
{
    public partial class BankOperationDialog : Window
    {
        public enum OperationKind { Withdraw, Deposit }

        public OperationKind Kind { get; private set; }
        public int? SelectedEnvelopeId { get; private set; }
        public string SourceKind { get; private set; } = "free";
        public decimal Amount { get; private set; }

        public BankOperationDialog()
        {
            InitializeComponent();

            FreeRadio.Checked += (_, __) => OnSourceChanged("free");
            SavedRadio.Checked += (_, __) => OnSourceChanged("saved");
            EnvelopeRadio.Checked += (_, __) => OnSourceChanged("envelope");
        }

        public void Configure(OperationKind kind, string accountName)
        {
            Kind = kind;

            if (kind == OperationKind.Withdraw)
            {
                HeaderTitleText.Text = $"Wypłata z konta „{accountName}”";
                HeaderSubtitleText.Text = "Wprowadź kwotę i wybierz, dokąd wypłacić środki.";
            }
            else
            {
                HeaderTitleText.Text = $"Wpłata na konto „{accountName}”";
                HeaderSubtitleText.Text = "Wprowadź kwotę i wybierz, skąd pobrać środki.";
            }

            AmountBox.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "";
            OnSourceChanged("free");
        }

        public void SetEnvelopes(IEnumerable<object> items)
        {
            EnvelopeCombo.ItemsSource = items;
            EnvelopeCombo.SelectedIndex = 0;
        }

        // ===== TitleBar =====
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
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

        private void OnSourceChanged(string kind)
        {
            SourceKind = kind;
            EnvelopeCombo.Visibility = kind == "envelope" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== Amount formatting =====
        private void AmountBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            var txt = (tb.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt) || txt == 0m.ToString("N2", CultureInfo.CurrentCulture))
                tb.Clear();

            tb.SelectAll();
        }

        private void AmountBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            var txt = (tb.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt))
            {
                tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
                return;
            }

            if (decimal.TryParse(txt, NumberStyles.Number, CultureInfo.CurrentCulture, out var v))
                tb.Text = v.ToString("N2", CultureInfo.CurrentCulture);
            else
                tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "";

            var raw = (AmountBox.Text ?? "").Replace(" ", "");
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
            {
                ErrorText.Text = "Podaj poprawną dodatnią kwotę.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            if (SourceKind == "envelope" && EnvelopeCombo.SelectedItem == null)
            {
                ErrorText.Text = "Wybierz kopertę.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            Amount = amount;

            // SelectedEnvelopeId: zależy od typu obiektu w ItemsSource,
            // w BanksPage ustawimy ItemsSource na listę EnvelopeItem (z Id)
            if (SourceKind == "envelope")
            {
                var pi = EnvelopeCombo.SelectedItem.GetType().GetProperty("Id");
                if (pi != null)
                {
                    SelectedEnvelopeId = Convert.ToInt32(pi.GetValue(EnvelopeCombo.SelectedItem));
                }
            }

            DialogResult = true;
            Close();
        }
    }
}
