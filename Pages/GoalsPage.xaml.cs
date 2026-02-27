using Finly.Services;
using Finly.Services.Features;
using Finly.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Finly.Pages
{
    public class GoalVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private int _envelopeId;
        public int EnvelopeId
        {
            get => _envelopeId;
            set
            {
                if (_envelopeId == value) return;
                _envelopeId = value;
                OnPropertyChanged(nameof(EnvelopeId));
            }
        }

        private string _envelopeName = "";
        public string EnvelopeName
        {
            get => _envelopeName;
            set
            {
                value ??= "";
                if (_envelopeName == value) return;
                _envelopeName = value;
                OnPropertyChanged(nameof(EnvelopeName));
                OnPropertyChanged(nameof(Name));
            }
        }

        private string _goalTitle = "";
        public string GoalTitle
        {
            get => _goalTitle;
            set
            {
                value ??= "";
                if (_goalTitle == value) return;
                _goalTitle = value;
                OnPropertyChanged(nameof(GoalTitle));
            }
        }

        private decimal _targetAmount;
        public decimal TargetAmount
        {
            get => _targetAmount;
            set
            {
                if (_targetAmount == value) return;
                _targetAmount = value;
                OnPropertyChanged(nameof(TargetAmount));
                OnPropertyChanged(nameof(Remaining));
                OnPropertyChanged(nameof(CompletionPercent));
                OnPropertyChanged(nameof(MonthlyNeeded));
            }
        }

        private decimal _currentAmount;
        public decimal CurrentAmount
        {
            get => _currentAmount;
            set
            {
                if (_currentAmount == value) return;
                _currentAmount = value;
                OnPropertyChanged(nameof(CurrentAmount));
                OnPropertyChanged(nameof(Remaining));
                OnPropertyChanged(nameof(CompletionPercent));
                OnPropertyChanged(nameof(MonthlyNeeded));
            }
        }

        private DateTime? _dueDate;
        public DateTime? DueDate
        {
            get => _dueDate;
            set
            {
                if (_dueDate == value) return;
                _dueDate = value;
                OnPropertyChanged(nameof(DueDate));
                OnPropertyChanged(nameof(MonthsLeft));
                OnPropertyChanged(nameof(MonthlyNeeded));
            }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set
            {
                value ??= "";
                if (_description == value) return;
                _description = value;
                OnPropertyChanged(nameof(Description));
            }
        }

        // ✅ zaznaczenie celu (domyślnie: true)
        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

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
        private bool _isDragging;

        private int _uid => UserService.GetCurrentUserId();

        public GoalsPage()
        {
            InitializeComponent();

            GoalsList.ItemsSource = _items;
            Loaded += GoalsPage_Loaded;
            Unloaded += GoalsPage_Unloaded;
        }

        private void GoalsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadGoals();
        }

        private void GoalsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // odpinamy eventy, żeby nie trzymać VM-ów przy życiu
            foreach (var g in _goals)
                g.PropertyChanged -= GoalVm_PropertyChanged;
        }

        private void GoalVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GoalVm.IsSelected) ||
                e.PropertyName == nameof(GoalVm.TargetAmount) ||
                e.PropertyName == nameof(GoalVm.CurrentAmount) ||
                e.PropertyName == nameof(GoalVm.DueDate))
            {
                RefreshKpis();
            }
        }

        private void LoadGoals()
        {
            // odepnij stare VM-y (bezpiecznie)
            foreach (var g in _goals)
                g.PropertyChanged -= GoalVm_PropertyChanged;

            _goals.Clear();

            var list = DatabaseService.GetEnvelopeGoals(_uid);
            foreach (var g in list)
            {
                var (goalTitle, description) = SplitGoalText(g.GoalText, g.Name);

                var vm = new GoalVm
                {
                    EnvelopeId = g.EnvelopeId,
                    EnvelopeName = g.Name,
                    GoalTitle = goalTitle,
                    TargetAmount = g.Target,
                    CurrentAmount = g.Allocated,
                    DueDate = g.Deadline,
                    Description = string.IsNullOrWhiteSpace(description) ? "Brak" : description,

                    // ✅ domyślnie zaznaczone
                    IsSelected = true
                };

                vm.PropertyChanged += GoalVm_PropertyChanged;

                _goals.Add(vm);
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
                _isDragging = true;

                var data = new DataObject(typeof(GoalVm), vm);
                DragDrop.DoDragDrop(GoalsList, data, DragDropEffects.Move);
            }
            finally
            {
                _isDragging = false;
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

            PersistOrder();
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
            // ✅ KPI liczone tylko z zaznaczonych
            var selected = _goals.Where(g => g.IsSelected).ToList();

            var totalTarget = selected.Sum(g => g.TargetAmount);
            var totalSaved = selected.Sum(g => g.CurrentAmount);
            var totalMonthly = selected.Sum(g => g.MonthlyNeeded);

            TotalGoalsAmountText.Text = totalTarget.ToString("N2") + " zł";
            TotalGoalsSavedText.Text = totalSaved.ToString("N2") + " zł";
            TotalMonthlyNeededText.Text = totalMonthly.ToString("N2") + " zł";
        }

        // ✅ klik w kartę celu (nie w buttony) przełącza zaznaczenie
        private void GoalCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging) return;
            if (e.ChangedButton != MouseButton.Left) return;

            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
                return;

            if (sender is Border b && b.DataContext is GoalVm vm)
            {
                vm.IsSelected = !vm.IsSelected;
                // RefreshKpis() też poleci z PropertyChanged, ale tu może zostać
                RefreshKpis();
            }
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

            // jeśli pusty opis, nie dokładaj śmieciowego "Opis:"
            if (!string.IsNullOrWhiteSpace(description) && !string.Equals(description.Trim(), "Brak", StringComparison.OrdinalIgnoreCase))
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

            var goalTitleTrim = r.GoalTitle.Trim();
            var note = BuildNote(goalTitleTrim, r.Description, r.DueDate.Value);

            try
            {
                int envelopeId;

                if (r.EditingEnvelopeId.HasValue)
                {
                    // EDIT: pracujemy na istniejącej kopercie
                    envelopeId = r.EditingEnvelopeId.Value;
                }
                else
                {
                    // ADD: jeśli istnieje koperta o tej nazwie -> użyj jej, inaczej utwórz
                    var existingId = DatabaseService.GetEnvelopeIdByName(_uid, goalTitleTrim);
                    if (existingId.HasValue)
                        envelopeId = existingId.Value;
                    else
                        envelopeId = DatabaseService.InsertEnvelope(_uid, goalTitleTrim, r.TargetAmount, r.CurrentAmount, note);
                }

                // ✅ KLUCZOWA NAPRAWA BUGA:
                // NIE zmieniamy nazwy koperty przy zapisie celu.
                DatabaseService.UpdateEnvelopeGoal(
                    _uid,
                    envelopeId,
                    r.TargetAmount,
                    r.CurrentAmount,
                    r.DueDate.Value,
                    note
                );

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

                vm.PropertyChanged -= GoalVm_PropertyChanged;

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