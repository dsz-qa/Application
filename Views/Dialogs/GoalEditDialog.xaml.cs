using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Views.Dialogs
{
    public partial class GoalEditDialog : Window
    {
        public enum DialogMode { Add, Edit }

        public sealed class GoalEditResult
        {
            public int? EditingEnvelopeId { get; set; }
            public string GoalTitle { get; set; } = "";
            public decimal TargetAmount { get; set; }
            public decimal CurrentAmount { get; set; }
            public DateTime? DueDate { get; set; }
            public string Description { get; set; } = "";
        }

        public GoalEditResult Result { get; private set; } = new();
        private DialogMode _mode;

        public GoalEditDialog()
        {
            InitializeComponent();
        }

        public void SetMode(DialogMode mode)
        {
            _mode = mode;

            HeaderTitleText.Text = mode == DialogMode.Add ? "Dodaj cel" : "Edytuj cel";
            HeaderSubtitleText.Text = mode == DialogMode.Add
                ? "Uzupełnij dane celu i zapisz."
                : "Zmień dane celu i zapisz zmiany.";

            SaveBtn.Content = mode == DialogMode.Add ? "Dodaj" : "Zapisz";
        }

        public void LoadForAdd()
        {
            Result = new GoalEditResult
            {
                EditingEnvelopeId = null,
                GoalTitle = "",
                TargetAmount = 0m,
                CurrentAmount = 0m,
                DueDate = null,
                Description = ""
            };

            ApplyToUi();
        }

        public void LoadForEdit(int envelopeId, string goalTitle, decimal target, decimal current, DateTime? dueDate, string description)
        {
            // w UI nie chcemy "Opis:" prefiksu – user edytuje czysty tekst
            var d = description ?? "";
            if (d.TrimStart().StartsWith("Opis:", StringComparison.OrdinalIgnoreCase))
                d = d.Trim().Substring(5).Trim();

            Result = new GoalEditResult
            {
                EditingEnvelopeId = envelopeId,
                GoalTitle = goalTitle ?? "",
                TargetAmount = target,
                CurrentAmount = current,
                DueDate = dueDate,
                Description = d
            };

            ApplyToUi();
        }

        private void ApplyToUi()
        {
            GoalNameBox.Text = Result.GoalTitle;
            TargetBox.Text = Result.TargetAmount.ToString("N2", CultureInfo.CurrentCulture);
            CurrentBox.Text = Result.CurrentAmount.ToString("N2", CultureInfo.CurrentCulture);
            DueDatePicker.SelectedDate = Result.DueDate;
            DescriptionBox.Text = Result.Description;
        }

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

        private void MoneyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            var txt = (tb.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt) || txt == 0m.ToString("N2", CultureInfo.CurrentCulture))
                tb.Clear();

            tb.SelectAll();
        }

        private void MoneyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            var txt = (tb.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt))
            {
                tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
                return;
            }

            if (decimal.TryParse(txt.Replace(" ", ""), NumberStyles.Number, CultureInfo.CurrentCulture, out var v))
                tb.Text = v.ToString("N2", CultureInfo.CurrentCulture);
            else
                tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
        }

        private static decimal SafeParse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            var raw = s.Replace(" ", "");

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var d))
                return d;

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                return d;

            return 0m;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var title = (GoalNameBox.Text ?? "").Trim();
            var target = SafeParse(TargetBox.Text);
            var current = SafeParse(CurrentBox.Text);
            var due = DueDatePicker.SelectedDate;
            var desc = (DescriptionBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("Podaj nazwę celu.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result.GoalTitle = title;
            Result.TargetAmount = target;
            Result.CurrentAmount = current;
            Result.DueDate = due;
            Result.Description = desc;

            DialogResult = true;
            Close();
        }
    }
}
