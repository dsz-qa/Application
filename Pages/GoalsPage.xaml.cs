using Finly.Services;
using Finly.Services.Features;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Finly.Pages
{
    public class GoalVm
    {
        // powiązanie 1:1 z kopertą
        public int EnvelopeId { get; set; }

        // Tekst nagłówka "Cel: ..."
        public string GoalTitle { get; set; } = "";

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

                int months = (d.Year - today.Year) * 12 + (d.Month - today.Month);
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

        public decimal CompletionPercent
        {
            get
            {
                if (TargetAmount <= 0) return 0m;
                var pct = (CurrentAmount / TargetAmount) * 100m;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                return Math.Round(pct, 1);
            }
        }
    }

    /// <summary>
    /// Kafelek „Dodaj cel”.
    /// </summary>
    public sealed class AddGoalTile { }

    public partial class GoalsPage : UserControl
    {
        private readonly ObservableCollection<GoalVm> _goals = new();
        private readonly ObservableCollection<object> _items = new();

        private int _uid => UserService.GetCurrentUserId();
        private int? _editingEnvelopeId = null;

        public GoalsPage()
        {
            InitializeComponent();

            GoalsRepeater.ItemsSource = _items;
            Loaded += GoalsPage_Loaded;

            FormBorder.Visibility = Visibility.Collapsed;
        }

        private void GoalsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadGoals();
        }

        private void LoadGoals()
        {
            _goals.Clear();

            var list = DatabaseService.GetEnvelopeGoals(_uid);
            foreach (var g in list)
            {
                var (goalTitle, description) = SplitGoalText(g.GoalText, g.Name);

                _goals.Add(new GoalVm
                {
                    EnvelopeId = g.EnvelopeId,
                    Name = g.Name,
                    GoalTitle = goalTitle,
                    TargetAmount = g.Target,
                    CurrentAmount = g.Allocated,
                    DueDate = g.Deadline,
                    Description = description
                });
            }

            RebuildItems();
            RefreshKpis();
        }


        private void RebuildItems()
        {
            _items.Clear();

            foreach (var g in _goals)
                _items.Add(g);

            // kafelek „Dodaj cel”
            _items.Add(new AddGoalTile());
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
            var totalMonthly = _goals.Sum(g => g.MonthlyNeeded);

            TotalGoalsAmountText.Text = totalTarget.ToString("N2") + " zł";
            TotalGoalsSavedText.Text = totalSaved.ToString("N2") + " zł";
            TotalMonthlyNeededText.Text = totalMonthly.ToString("N2") + " zł";
        }

        private static (string goalTitle, string description) SplitGoalText(string? raw, string fallbackName)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return (fallbackName, string.Empty);

            string goalTitle = "";
            var descLines = new System.Collections.Generic.List<string>();

            var lines = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("Cel:", StringComparison.OrdinalIgnoreCase))
                {
                    // "Cel: Spłata pożyczki u Pani Beaty" -> "Spłata pożyczki u Pani Beaty"
                    goalTitle = line.Substring(4).Trim();
                }
                else if (line.StartsWith("Termin:", StringComparison.OrdinalIgnoreCase))
                {
                    // tę linię pomijamy – termin już pokazujemy wyżej w osobnym polu
                    continue;
                }
                else
                {
                    // reszta (np. "Opis: ...") leci do opisu
                    descLines.Add(line);
                }
            }

            if (string.IsNullOrWhiteSpace(goalTitle))
                goalTitle = fallbackName;  // awaryjnie weź nazwę koperty

            var description = string.Join(Environment.NewLine, descLines);
            return (goalTitle, description);
        }


        // ========= helper do szukania panelu potwierdzenia w szablonie =========

        private static T? FindTemplateChild<T>(DependencyObject start, string childName)
            where T : FrameworkElement
        {
            var current = start;
            while (current != null)
            {
                if (current is FrameworkElement fe)
                {
                    var candidate = fe.FindName(childName) as T;
                    if (candidate != null)
                        return candidate;
                }

                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // ========= kafel „Dodaj cel” =========

        private void AddGoalCard_Click(object sender, MouseButtonEventArgs e)
        {
            _editingEnvelopeId = null;

            FormHeader.Text = "Dodaj cel";
            GoalFormMessage.Text = string.Empty;
            FormBorder.Visibility = Visibility.Visible;

            ClearGoalForm();
        }

        // ========= Edycja / usuwanie istniejących celów =========

        private void EditGoal_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not GoalVm vm)
                return;

            _editingEnvelopeId = vm.EnvelopeId;

            FormHeader.Text = "Edytuj cel";
            GoalFormMessage.Text = string.Empty;
            FormBorder.Visibility = Visibility.Visible;

            GoalNameBox.Text = vm.Name;
            GoalTargetBox.Text = vm.TargetAmount.ToString("N2");
            GoalCurrentBox.Text = vm.CurrentAmount.ToString("N2");
            GoalDueDatePicker.SelectedDate = vm.DueDate;
            GoalDescriptionBox.Text = vm.Description ?? "";
        }

        /// <summary>
        /// Kliknięcie "Usuń" – tylko pokazuje panel potwierdzenia.
        /// </summary>
        private void DeleteGoal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var panel = FindTemplateChild<StackPanel>(fe, "GoalDeleteConfirmPanel");
                if (panel != null)
                    panel.Visibility = Visibility.Visible;
            }
        }

        private void DeleteGoalCancel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var panel = FindTemplateChild<StackPanel>(fe, "GoalDeleteConfirmPanel");
                if (panel != null)
                    panel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Faktyczne usunięcie celu po kliknięciu "Tak".
        /// </summary>
        private void DeleteGoalConfirm_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not GoalVm vm)
                return;

            try
            {
                // Czyścimy DANE CELU w kopercie, ale NIE usuwamy samej koperty.
                DatabaseService.ClearEnvelopeGoal(_uid, vm.EnvelopeId);

                _goals.Remove(vm);
                RebuildItems();
                RefreshKpis();

                if (sender is FrameworkElement fe)
                {
                    var panel = FindTemplateChild<StackPanel>(fe, "GoalDeleteConfirmPanel");
                    if (panel != null)
                        panel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się usunąć celu.\n" + ex.Message);
            }
        }

        // ========= Formularz zapisujący cel do kopert =========

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

            try
            {
                int envelopeId;

                if (_editingEnvelopeId.HasValue)
                {
                    // edycja istniejącej koperty
                    envelopeId = _editingEnvelopeId.Value;
                }
                else
                {
                    // szukamy koperty o tej nazwie; jeśli brak – tworzymy nową kopertę z tym celem
                    var existingId = DatabaseService.GetEnvelopeIdByName(_uid, name);
                    if (existingId.HasValue)
                    {
                        envelopeId = existingId.Value;
                    }
                    else
                    {
                        envelopeId = DatabaseService.InsertEnvelope(_uid, name, target, current, desc);
                    }
                }

                // ustaw / zaktualizuj dane celu w tej kopercie
                DatabaseService.UpdateEnvelopeGoal(_uid, envelopeId, target, current, dueDate.Value, desc);

                // przeładuj cele z bazy (procent, miesięcznie, KPI)
                LoadGoals();

                GoalFormMessage.Text = _editingEnvelopeId.HasValue
                    ? "Cel zaktualizowany."
                    : "Cel dodany.";

                ClearGoalForm();
                FormBorder.Visibility = Visibility.Collapsed;
                _editingEnvelopeId = null;
            }
            catch (Exception ex)
            {
                GoalFormMessage.Text = "Nie udało się zapisać celu: " + ex.Message;
            }
        }

        private void ClearGoalForm_Click(object sender, RoutedEventArgs e)
        {
            ClearGoalForm();
            GoalFormMessage.Text = string.Empty;
            FormBorder.Visibility = Visibility.Collapsed;
            _editingEnvelopeId = null;
        }

        private void ClearGoalForm()
        {
            GoalNameBox.Text = "";
            GoalTargetBox.Text = "0,00";
            GoalCurrentBox.Text = "0,00";
            GoalDueDatePicker.SelectedDate = null;
            GoalDescriptionBox.Text = "";
        }

        // ========= 0,00 -> czyszczenie po kliknięciu =========

        private void AmountBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (tb.Text == "0,00" || tb.Text == "0.00")
                    tb.Clear();
            }
        }

        private void AmountBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var val = ParseDecimal(tb.Text);
                tb.Text = val.ToString("N2");
            }
        }
    }
}