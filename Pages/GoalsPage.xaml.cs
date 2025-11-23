using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public class GoalVm
    {
        public string Name { get; set; } = "";
        public decimal TargetAmount { get; set; }
        public decimal CurrentAmount { get; set; }
        public DateTime? DueDate { get; set; }
        public string Description { get; set; } = "";

        public decimal Remaining => Math.Max(0, TargetAmount - CurrentAmount);

        public int MonthsLeft
        {
            get
            {
                if (DueDate == null) return 0;

                var today = DateTime.Today;
                var d = DueDate.Value.Date;

                if (d <= today) return 0;

                int months = (d.Year - today.Year) * 12 + d.Month - today.Month;
                if (d.Day > today.Day) months++;
                if (months <= 0) months = 1;

                return months;
            }
        }

        public decimal MonthlyNeeded
        {
            get
            {
                if (Remaining <= 0) return 0m;
                var m = MonthsLeft;
                if (m <= 0) return Remaining;
                return Math.Round(Remaining / m, 2);
            }
        }
    }

    public partial class GoalsPage : UserControl
    {
        private readonly ObservableCollection<GoalVm> _goals = new();

        public GoalsPage()
        {
            InitializeComponent();
            GoalsRepeater.ItemsSource = _goals;
            LoadGoals();
        }

        // Na razie cele są tylko w pamięci.
        // Później możesz je podpiąć pod bazę / koperty.
        private void LoadGoals()
        {
            // startowo pusto
            _goals.Clear();

            RefreshKpis();
        }

        private static decimal ParseDecimal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0m;
            var raw = text.Replace(" ", "");

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var d))
                return d;
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                return d;

            return 0m;
        }

        private void RefreshKpis()
        {
            var totalTarget = _goals.Sum(g => g.TargetAmount);
            var totalSaved = _goals.Sum(g => g.CurrentAmount);
            var avgMonthly = _goals.Count == 0 ? 0m : _goals.Average(g => g.MonthlyNeeded);

            TotalGoalsAmountText.Text = totalTarget.ToString("N2") + " zł";
            TotalGoalsSavedText.Text = totalSaved.ToString("N2") + " zł";
            AverageMonthlyNeededText.Text = avgMonthly.ToString("N2") + " zł";
        }

        private void AddGoal_Click(object sender, RoutedEventArgs e)
        {
            var name = (GoalNameBox.Text ?? "").Trim();
            var target = ParseDecimal(GoalTargetBox.Text);
            var current = ParseDecimal(GoalCurrentBox.Text);
            var dueDate = GoalDueDatePicker.SelectedDate;
            var desc = GoalDescriptionBox.Text ?? "";

            if (string.IsNullOrWhiteSpace(name))
            {
                GoalFormMessage.Text = "Podaj nazwę celu.";
                return;
            }

            if (target <= 0)
            {
                GoalFormMessage.Text = "Docelowa kwota musi być większa od zera.";
                return;
            }

            if (current < 0)
            {
                GoalFormMessage.Text = "Odłożona kwota nie może być ujemna.";
                return;
            }

            if (current > target)
            {
                GoalFormMessage.Text = "Odłożona kwota nie może być większa niż cel.";
                return;
            }

            if (dueDate == null)
            {
                GoalFormMessage.Text = "Wybierz datę zakończenia celu.";
                return;
            }

            var goal = new GoalVm
            {
                Name = name,
                TargetAmount = target,
                CurrentAmount = current,
                DueDate = dueDate,
                Description = desc
            };

            _goals.Add(goal);
            RefreshKpis();
            GoalFormMessage.Text = "Cel dodany.";

            // opcjonalnie wyczyść
            ClearGoalForm();
        }

        private void ClearGoalForm_Click(object sender, RoutedEventArgs e)
        {
            ClearGoalForm();
            GoalFormMessage.Text = string.Empty;
        }

        private void ClearGoalForm()
        {
            GoalNameBox.Text = "";
            GoalTargetBox.Text = "";
            GoalCurrentBox.Text = "";
            GoalDueDatePicker.SelectedDate = null;
            GoalDescriptionBox.Text = "";
        }
    }
}
