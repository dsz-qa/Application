using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Finly.Views.Dialogs
{
    public partial class EnvelopeEditDialog : Window
    {
        public enum DialogMode { Add, Edit }

        public sealed class EnvelopeEditResult
        {
            public int? EditingId { get; set; }
            public string Name { get; set; } = "";
            public decimal Allocated { get; set; }
            public decimal Target { get; set; }
            public string Goal { get; set; } = "";
            public string Description { get; set; } = "";
            public DateTime? Deadline { get; set; }

            public string Note { get; set; } = "";
            public bool ShouldCreateGoal { get; set; }
        }

        public EnvelopeEditResult Result { get; private set; } = new();
        private DialogMode _mode;

        public EnvelopeEditDialog()
        {
            InitializeComponent();
        }

        public void SetMode(DialogMode mode)
        {
            _mode = mode;

            HeaderTitleText.Text = mode == DialogMode.Add ? "Dodaj kopertę" : "Edytuj kopertę";
            HeaderSubtitleText.Text = mode == DialogMode.Add
                ? "Uzupełnij dane koperty i zapisz."
                : "Zmień dane koperty i zapisz zmiany.";

            SaveBtn.Content = mode == DialogMode.Add ? "Dodaj" : "Zapisz";
        }

        public void LoadForAdd()
        {
            Result = new EnvelopeEditResult
            {
                EditingId = null,
                Name = "",
                Allocated = 0m,
                Target = 0m,
                Goal = "",
                Description = "",
                Deadline = null
            };

            ApplyToUi();
        }

        public void LoadForEdit(int id, string name, decimal target, decimal allocated, string note)
        {
            SplitNote(note, out var goal, out var description, out var deadline);

            Result = new EnvelopeEditResult
            {
                EditingId = id,
                Name = name ?? "",
                Allocated = allocated,
                Target = target,
                Goal = goal,
                Description = description,
                Deadline = deadline
            };

            ApplyToUi();
        }

        private void ApplyToUi()
        {
            NameBox.Text = Result.Name;
            AllocatedBox.Text = Result.Allocated.ToString("N2", CultureInfo.CurrentCulture);
            TargetBox.Text = Result.Target.ToString("N2", CultureInfo.CurrentCulture);
            GoalBox.Text = Result.Goal;
            DescriptionBox.Text = Result.Description;
            DeadlinePicker.SelectedDate = Result.Deadline;

            RecalculateMonthlyRequired();
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

        // ===== Money formatting =====
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
                RecalculateMonthlyRequired();
                return;
            }

            if (decimal.TryParse(txt.Replace(" ", ""), NumberStyles.Number, CultureInfo.CurrentCulture, out var v))
                tb.Text = v.ToString("N2", CultureInfo.CurrentCulture);
            else
                tb.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);

            RecalculateMonthlyRequired();
        }

        private void DeadlinePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            RecalculateMonthlyRequired();
        }

        private void RecalculateMonthlyRequired()
        {
            var target = SafeParse(TargetBox.Text);
            var allocated = SafeParse(AllocatedBox.Text);
            var remaining = target - allocated;

            if (remaining <= 0m)
            {
                MonthlyRequiredText.Text = "Miesięcznie trzeba odkładać: 0,00 zł";
                return;
            }

            var deadline = DeadlinePicker.SelectedDate;
            if (deadline == null)
            {
                MonthlyRequiredText.Text = "Miesięcznie trzeba odkładać: -";
                return;
            }

            int monthsLeft = MonthsBetween(DateTime.Today, deadline.Value.Date);
            if (monthsLeft <= 0) monthsLeft = 1;

            var perMonth = remaining / monthsLeft;
            MonthlyRequiredText.Text = "Miesięcznie trzeba odkładać: " + perMonth.ToString("N2", CultureInfo.CurrentCulture) + " zł";
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

        private static int MonthsBetween(DateTime from, DateTime to)
        {
            if (to <= from) return 0;
            int months = (to.Year - from.Year) * 12 + (to.Month - from.Month);
            if (to.Day > from.Day) months++;
            return months;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var name = (NameBox.Text ?? "").Trim();
            var allocated = SafeParse(AllocatedBox.Text);
            var target = SafeParse(TargetBox.Text);
            var goal = (GoalBox.Text ?? "").Trim();
            var description = (DescriptionBox.Text ?? "").Trim();
            var deadline = DeadlinePicker.SelectedDate;

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Podaj nazwę koperty.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (allocated < 0m || target < 0m)
            {
                MessageBox.Show("Kwoty nie mogą być ujemne.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool shouldCreateGoal =
                !string.IsNullOrWhiteSpace(goal) &&
                deadline.HasValue &&
                target > 0m;

            var effectiveDeadline = shouldCreateGoal ? deadline : null;
            var note = BuildNote(goal, description, effectiveDeadline);

            Result.Name = name;
            Result.Allocated = allocated;
            Result.Target = target;
            Result.Goal = goal;
            Result.Description = description;
            Result.Deadline = deadline;
            Result.ShouldCreateGoal = shouldCreateGoal;
            Result.Note = note;

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Note:
        /// "Cel: ....\nOpis: ....\nTermin: RRRR-MM-DD"
        /// </summary>
        private static void SplitNote(string? note, out string goal, out string description, out DateTime? deadline)
        {
            goal = string.Empty;
            description = string.Empty;
            deadline = null;

            if (string.IsNullOrWhiteSpace(note))
                return;

            var lines = note.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (line.StartsWith("Cel:", StringComparison.OrdinalIgnoreCase))
                {
                    goal = line.Substring(4).Trim();
                }
                else if (line.StartsWith("Opis:", StringComparison.OrdinalIgnoreCase))
                {
                    description = line.Substring(5).Trim();
                }
                else if (line.StartsWith("Termin:", StringComparison.OrdinalIgnoreCase))
                {
                    var dateText = line.Substring(7).Trim();
                    if (DateTime.TryParseExact(dateText, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
                    {
                        deadline = d1.Date;
                    }
                    else if (DateTime.TryParse(dateText, CultureInfo.CurrentCulture,
                             DateTimeStyles.None, out var d2))
                    {
                        deadline = d2.Date;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(goal))
                        goal = line;
                    else if (string.IsNullOrEmpty(description))
                        description = line;
                }
            }
        }

        private static string BuildNote(string goal, string description, DateTime? deadline)
        {
            goal = (goal ?? "").Trim();
            description = (description ?? "").Trim();

            var parts = new List<string>();

            if (!string.IsNullOrEmpty(goal))
                parts.Add($"Cel: {goal}");

            if (!string.IsNullOrEmpty(description))
                parts.Add($"Opis: {description}");

            if (deadline.HasValue)
                parts.Add("Termin: " + deadline.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            return string.Join("\n", parts);
        }
    }
}
