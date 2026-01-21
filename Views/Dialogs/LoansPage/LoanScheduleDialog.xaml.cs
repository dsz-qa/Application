using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Finly.Models;

namespace Finly.Views.Dialogs
{
    public partial class LoanScheduleDialog : Window
    {
        public LoanScheduleDialog(string loanName, IReadOnlyList<LoanInstallmentRow> rows)
        {
            InitializeComponent();

            TitleText.Text = "Harmonogram spłat";
            HeaderText.Text = $"Harmonogram – {loanName}";
            rows ??= Array.Empty<LoanInstallmentRow>();

            RowsList.ItemsSource = rows.Select(r => r.ToString()).ToList();

            var today = DateTime.Today;
            var next = rows.Where(r => r.Date >= today).OrderBy(r => r.Date).FirstOrDefault();
            if (next != null)
            {
                NextPaymentText.Text = $"{next.Total:N2} zł · {next.Date:dd.MM.yyyy}";
                SubText.Text = $"Liczba rat w harmonogramie: {rows.Count}.";
            }
            else
            {
                NextPaymentText.Text = "Brak przyszłych rat w pliku.";
                SubText.Text = $"Liczba rat w harmonogramie: {rows.Count}.";
            }
        }

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
    }
}

