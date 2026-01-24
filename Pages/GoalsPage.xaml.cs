using Finly.Services;
using Finly.Services.Features;
using Finly.Views.Dialogs;
using System;
using System.Collections.Generic;
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
        public int EnvelopeId { get; set; }
        public string EnvelopeName { get; set; } = "";
        public string GoalTitle { get; set; } = "";
        public decimal TargetAmount { get; set; }
        public decimal CurrentAmount { get; set; }
        public DateTime? DueDate { get; set; }
        public string Description { get; set; } = "";



        // BACKWARD COMPAT
        public string Name
        {
            get => EnvelopeName;
            set => EnvelopeName = value ?? "";
        }

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

    public sealed class AddGoalTile { }

    public partial class GoalsPage : UserControl
    {
        private readonly ObservableCollection<GoalVm> _goals = new();
        private readonly ObservableCollection<object> _items = new();


        private Point _dragStartPoint;
        private object? _dragItem;

        private int _uid => UserService.GetCurrentUserId();

        public GoalsPage()
        {
            InitializeComponent();

            GoalsList.ItemsSource = _items;
            Loaded += GoalsPage_Loaded;
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
                    EnvelopeName = g.Name,
                    GoalTitle = goalTitle,
                    TargetAmount = g.Target,
                    CurrentAmount = g.Allocated,
                    DueDate = g.Deadline,
                    Description = string.IsNullOrWhiteSpace(description) ? "Brak" : description
                });
            }

            RebuildItems();
            RefreshKpis();
        }
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T typed) return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private object? GetItemUnderMouse(DependencyObject? source)
        {
            if (source == null) return null;

            var container = FindAncestor<ListBoxItem>(source);
            container ??= GoalsList.ContainerFromElement(source) as ListBoxItem;

            if (container == null) return null;

            var item = GoalsList.ItemContainerGenerator.ItemFromContainer(container);
            if (item == DependencyProperty.UnsetValue) return null;

            return item;
        }


        private void GoalsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragItem = null;

            // Klik na Button (Edytuj/Usuń) nie startuje drag&drop
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
                return;

            _dragStartPoint = e.GetPosition(GoalsList);
            _dragItem = GetItemUnderMouse(e.OriginalSource as DependencyObject);
        }

        private void GoalsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (_dragItem == null) return;

            // nie przeciągamy kafelka “Dodaj cel”
            if (_dragItem is AddGoalTile) return;

            var pos = e.GetPosition(GoalsList);
            var diff = _dragStartPoint - pos;

            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            // USZCZELNIENIE: drag niesie TYLKO GoalVm
            if (_dragItem is not GoalVm vm) return;

            try
            {
                var data = new DataObject(typeof(GoalVm), vm);
                DragDrop.DoDragDrop(GoalsList, data, DragDropEffects.Move);
            }
            finally
            {
                _dragItem = null;
            }
        }

        private void GoalsList_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(GoalVm))) return;

            var dropped = e.Data.GetData(typeof(GoalVm)) as GoalVm;
            if (dropped == null) return;

            var targetObj = GetItemUnderMouse(e.OriginalSource as DependencyObject);
            if (targetObj is not GoalVm target) return;

            if (ReferenceEquals(dropped, target)) return;

            var oldIndex = _goals.IndexOf(dropped);
            var newIndex = _goals.IndexOf(target);

            if (oldIndex < 0 || newIndex < 0) return;

            _goals.Move(oldIndex, newIndex);
            RebuildItems();

            PersistOrder(); // DOCZELOWO: zapis do bazy
        }

        private void PersistOrder()
        {
            try
            {
                var orderedIds = _goals.Select(g => g.EnvelopeId).Where(id => id > 0).ToList();
                DatabaseService.SaveGoalsOrder(_uid, orderedIds);
            }
            catch (Exception ex)
            {
                // Nie blokuj UI – tylko info
                ToastService.Error("Nie udało się zapisać kolejności celów: " + ex.Message);
            }
        }





        private void RebuildItems()
        {
            _items.Clear();

            foreach (var g in _goals)
                _items.Add(g);

            _items.Add(new AddGoalTile());
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

        // NOTE: "Cel: ...\nOpis: ...\nTermin: ..."
        private static (string goalTitle, string description) SplitGoalText(string? raw, string fallbackName)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return (fallbackName, string.Empty);

            string goalTitle = "";
            var descLines = new List<string>();

            var lines = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("Cel:", StringComparison.OrdinalIgnoreCase))
                {
                    goalTitle = line.Substring(4).Trim();
                }
                else if (line.StartsWith("Termin:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                else
                {
                    if (line.StartsWith("Opis:", StringComparison.OrdinalIgnoreCase))
                        line = line.Substring(5).Trim();

                    if (!string.IsNullOrWhiteSpace(line))
                        descLines.Add(line);
                }
            }

            if (string.IsNullOrWhiteSpace(goalTitle))
                goalTitle = fallbackName;

            var description = string.Join(Environment.NewLine, descLines).Trim();
            if (string.IsNullOrWhiteSpace(description))
                description = "Brak";

            return (goalTitle, description);
        }

        private static string BuildNote(string goalTitle, string description, DateTime? deadline)
        {
            goalTitle = (goalTitle ?? "").Trim();
            description = (description ?? "").Trim();

            var parts = new List<string>();

            if (!string.IsNullOrEmpty(goalTitle))
                parts.Add($"Cel: {goalTitle}");

            if (!string.IsNullOrEmpty(description))
            {
                var desc = description.Trim();
                if (!desc.StartsWith("Opis:", StringComparison.OrdinalIgnoreCase))
                    desc = "Opis: " + desc;

                parts.Add(desc);
            }

            if (deadline.HasValue)
                parts.Add("Termin: " + deadline.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            return string.Join("\n", parts);
        }

        // ========= helper do panelu potwierdzenia =========
        private static T? FindTemplateChild<T>(DependencyObject start, string childName) where T : FrameworkElement
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

        // ========= Add/Edit przez dialog =========

        private void AddGoalCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            var dlg = new GoalEditDialog
            {
                Owner = Window.GetWindow(this)
            };

            dlg.SetMode(GoalEditDialog.DialogMode.Add);
            dlg.LoadForAdd();

            if (dlg.ShowDialog() != true)
                return;

            SaveGoalFromDialog(dlg.Result);
        }

        private void EditGoal_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not GoalVm vm)
                return;

            var dlg = new GoalEditDialog
            {
                Owner = Window.GetWindow(this)
            };

            dlg.SetMode(GoalEditDialog.DialogMode.Edit);
            dlg.LoadForEdit(vm.EnvelopeId, vm.GoalTitle, vm.TargetAmount, vm.CurrentAmount, vm.DueDate, vm.Description);

            if (dlg.ShowDialog() != true)
                return;

            SaveGoalFromDialog(dlg.Result);
        }

        private void SaveGoalFromDialog(GoalEditDialog.GoalEditResult r)
        {
            // walidacje takie jak miałaś wcześniej
            if (string.IsNullOrWhiteSpace(r.GoalTitle))
            {
                ToastService.Info("Podaj nazwę celu.");
                return;
            }

            if (r.TargetAmount <= 0m)
            {
                ToastService.Info("Docelowa kwota musi być większa od zera.");
                return;
            }

            if (r.CurrentAmount < 0m)
            {
                ToastService.Info("Odłożona kwota nie może być ujemna.");
                return;
            }

            if (r.CurrentAmount > r.TargetAmount)
            {
                ToastService.Info("Odłożona kwota nie może być większa niż cel.");
                return;
            }

            if (r.DueDate == null)
            {
                ToastService.Info("Wybierz datę zakończenia celu.");
                return;
            }

            if (!ValidateGoalCurrentAgainstSavedCash(_uid, r.EditingEnvelopeId, r.CurrentAmount, out var fundsMsg))
            {
                ToastService.Info(fundsMsg);
                return;
            }

            var note = BuildNote(r.GoalTitle, r.Description, r.DueDate.Value);

            try
            {
                int envelopeId;

                if (r.EditingEnvelopeId.HasValue)
                {
                    envelopeId = r.EditingEnvelopeId.Value;
                }
                else
                {
                    var existingId = DatabaseService.GetEnvelopeIdByName(_uid, r.GoalTitle);
                    if (existingId.HasValue)
                    {
                        envelopeId = existingId.Value;
                    }
                    else
                    {
                        envelopeId = DatabaseService.InsertEnvelope(_uid, r.GoalTitle, r.TargetAmount, r.CurrentAmount, note);
                    }
                }

                DatabaseService.UpdateEnvelopeGoal(_uid, envelopeId, r.TargetAmount, r.CurrentAmount, r.DueDate.Value, note);

                ToastService.Success(r.EditingEnvelopeId.HasValue ? "Cel zaktualizowany." : "Cel dodany.");
                LoadGoals();
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać celu: " + ex.Message);
            }
        }

        // ========= Delete (bez zmian) =========

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

        private void DeleteGoalConfirm_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not GoalVm vm)
                return;

            try
            {
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

                ToastService.Success("Cel usunięty.");
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się usunąć celu.\n" + ex.Message);
            }
        }

        private bool ValidateGoalCurrentAgainstSavedCash(int userId, int? editingEnvelopeId, decimal newCurrent, out string message)
        {
            message = string.Empty;

            if (newCurrent < 0m)
            {
                message = "Odłożona kwota nie może być ujemna.";
                return false;
            }

            var savedTotal = DatabaseService.GetSavedCash(userId);
            var dt = DatabaseService.GetEnvelopesTable(userId);

            decimal totalAllocated = 0m;
            decimal previousAllocated = 0m;

            if (dt != null)
            {
                foreach (System.Data.DataRow r in dt.Rows)
                {
                    var id = Convert.ToInt32(r["Id"]);
                    var alloc = 0m;
                    try { alloc = Convert.ToDecimal(r["Allocated"]); } catch { }

                    totalAllocated += alloc;

                    if (editingEnvelopeId.HasValue && id == editingEnvelopeId.Value)
                        previousAllocated = alloc;
                }
            }

            var allocatedWithoutThis = totalAllocated - previousAllocated;
            var availableForThis = savedTotal - allocatedWithoutThis;

            if (newCurrent > availableForThis)
            {
                message = $"Masz za mało środków w „Odłożonej gotówce”. " +
                          $"Dostępne do przydzielenia: {availableForThis:N2} zł, próbujesz odłożyć: {newCurrent:N2} zł.";
                return false;
            }

            return true;
        }
    }
}
