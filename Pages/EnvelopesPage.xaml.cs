using Finly.Services;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Finly.Pages
{
    // marker do kafelka "Dodaj kopertę"
    public sealed class AddEnvelopeTile { }

    public partial class EnvelopesPage : UserControl
    {
        private int _userId;
        private DataTable? _dt;
        private int? _editingId = null;

        // kolekcja kart (koperty + kafelek Dodaj)
        private readonly ObservableCollection<object> _cards = new();

        // aktualna "Odłożona gotówka" w bazie
        private decimal _savedTotal = 0m;

        public EnvelopesPage()
        {
            InitializeComponent();
            EnvelopesCards.ItemsSource = _cards;
            Loaded += EnvelopesPage_Loaded;
        }

        public EnvelopesPage(int userId) : this()
        {
            _userId = userId;
        }

        private void EnvelopesPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_userId <= 0)
                _userId = UserService.GetCurrentUserId();

            if (_userId <= 0)
                return;

            LoadAll();
            FormBorder.Visibility = Visibility.Collapsed;
        }

        // ===================== LOAD =====================

        private void LoadAll()
        {
            try
            {
                _savedTotal = DatabaseService.GetSavedCash(_userId);

                _dt = DatabaseService.GetEnvelopesTable(_userId);

                if (_dt != null)
                {
                    if (!_dt.Columns.Contains("Remaining"))
                        _dt.Columns.Add("Remaining", typeof(decimal));

                    if (!_dt.Columns.Contains("GoalText"))
                        _dt.Columns.Add("GoalText", typeof(string));

                    if (!_dt.Columns.Contains("Description"))
                        _dt.Columns.Add("Description", typeof(string));

                    if (!_dt.Columns.Contains("Deadline"))
                        _dt.Columns.Add("Deadline", typeof(string));

                    if (!_dt.Columns.Contains("MonthlyRequired"))
                        _dt.Columns.Add("MonthlyRequired", typeof(decimal));

                    foreach (DataRow r in _dt.Rows)
                    {
                        var target = SafeDec(r["Target"]);
                        var alloc = SafeDec(r["Allocated"]);
                        r["Remaining"] = target - alloc;

                        SplitNote(r["Note"]?.ToString(), out var goal, out var description, out var deadline);
                        r["GoalText"] = goal;
                        r["Description"] = description; // <- now description is available to XAML

                        if (deadline.HasValue)
                        {
                            r["Deadline"] = deadline.Value.ToString("d", CultureInfo.CurrentCulture);

                            var remaining = target - alloc;
                            if (remaining <= 0m)
                            {
                                r["MonthlyRequired"] = 0m;
                            }
                            else
                            {
                                int monthsLeft = MonthsBetween(DateTime.Today, deadline.Value);
                                if (monthsLeft <= 0)
                                    monthsLeft = 1;

                                r["MonthlyRequired"] = remaining / monthsLeft;
                            }
                        }
                        else
                        {
                            r["Deadline"] = "";
                            r["MonthlyRequired"] = 0m;
                        }
                    }
                }

                _cards.Clear();
                if (_dt != null)
                {
                    foreach (DataRowView rowView in _dt.DefaultView)
                        _cards.Add(rowView);
                }
                _cards.Add(new AddEnvelopeTile());

                var allocated = _dt?.AsEnumerable().Sum(r => SafeDec(r["Allocated"])) ?? 0m;
                var unassigned = _savedTotal - allocated;

                TotalEnvelopesText.Text = allocated.ToString("N2") + " zł";
                SavedCashText.Text = _savedTotal.ToString("N2") + " zł";
                EnvelopesSumText.Text = allocated.ToString("N2") + " zł";

                UnassignedText.Text = unassigned.ToString("N2") + " zł";
                UnassignedText.Foreground = unassigned < 0
                    ? Brushes.IndianRed
                    : (Brush)FindResource("App.Foreground");
            }
            catch (Exception ex)
            {
                FormMessage.Text = "Błąd odczytu: " + ex.Message;
            }
        }

        // ===================== HELPERS =====================

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

        private static decimal SafeDec(object? o)
        {
            if (o == null || o == DBNull.Value) return 0m;
            try { return Convert.ToDecimal(o); }
            catch { return 0m; }
        }

        private static int MonthsBetween(DateTime from, DateTime to)
        {
            if (to <= from) return 0;
            int months = (to.Year - from.Year) * 12 + (to.Month - from.Month);
            if (to.Day > from.Day)
                months++;
            return months;
        }

        /// <summary>
        /// Note jest zapisywany jako:
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

            var parts = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrEmpty(goal))
                parts.Add($"Cel: {goal}");

            if (!string.IsNullOrEmpty(description))
                parts.Add($"Opis: {description}");

            if (deadline.HasValue)
                parts.Add("Termin: " + deadline.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            return string.Join("\n", parts);
        }

        private void RecalculateMonthlyRequired()
        {
            var target = SafeParse(TargetBox.Text);
            var allocated = SafeParse(AllocatedBox.Text);
            var remaining = target - allocated;

            if (remaining <= 0m)
            {
                MonthlyRequiredText.Text = "0,00 zł";
                return;
            }

            var deadline = DeadlinePicker.SelectedDate;
            if (deadline == null)
            {
                MonthlyRequiredText.Text = "-";
                return;
            }

            int monthsLeft = MonthsBetween(DateTime.Today, deadline.Value.Date);
            if (monthsLeft <= 0)
                monthsLeft = 1;

            var perMonth = remaining / monthsLeft;
            MonthlyRequiredText.Text = perMonth.ToString("N2") + " zł";
        }

        private void SetAddMode()
        {
            _editingId = null;
            FormHeader.Text = "Dodaj kopertę";

            NameBox.Text = string.Empty;
            TargetBox.Text = "0,00";
            AllocatedBox.Text = "0,00";
            GoalBox.Text = string.Empty;
            DescriptionBox.Text = string.Empty;
            DeadlinePicker.SelectedDate = null;
            MonthlyRequiredText.Text = "-";

            FormMessage.Text = string.Empty;
            SaveEnvelopeBtn.Content = "Dodaj";

            FormBorder.Visibility = Visibility.Visible;
        }

        private void SetEditMode(int id, DataRow row)
        {
            _editingId = id;
            FormHeader.Text = "Edytuj kopertę";

            NameBox.Text = row["Name"]?.ToString() ?? "";
            TargetBox.Text = SafeDec(row["Target"]).ToString("N2");
            AllocatedBox.Text = SafeDec(row["Allocated"]).ToString("N2");

            SplitNote(row["Note"]?.ToString(), out var goal, out var description, out var deadline);
            GoalBox.Text = goal;
            DescriptionBox.Text = description;
            DeadlinePicker.SelectedDate = deadline;

            RecalculateMonthlyRequired();

            FormMessage.Text = string.Empty;
            SaveEnvelopeBtn.Content = "Zapisz zmiany";
            FormBorder.Visibility = Visibility.Visible;
        }

        // ===================== ZDARZENIA UI =====================

        private void AddEnvelopeCard_Click(object sender, MouseButtonEventArgs e)
        {
            SetAddMode();
        }

        private void AmountBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var t = tb.Text.Trim();
                if (t == "0" || t == "0,00" || t == "0.00")
                    tb.Text = "";
            }
        }

        private void AmountBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var val = SafeParse(tb.Text);
                tb.Text = val.ToString("N2");
            }

            RecalculateMonthlyRequired();
        }

        private void DeadlinePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            RecalculateMonthlyRequired();
        }

        private void EditEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not DataRowView drv) return;
            var row = drv.Row;
            var id = Convert.ToInt32(row["Id"]);
            SetEditMode(id, row);
        }

        // show / hide inline confirmation (nie MsgBox)
        private void DeleteEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not DataRowView drv) return;
            var btn = sender as Button;

            // znajdź kontener karty (Border) i panel potwierdzenia wewnątrz niej
            var cardBorder = FindVisualParent<Border>(btn);
            if (cardBorder == null) return;

            var confirmPanel = FindDescendantByName<FrameworkElement>(cardBorder, "DeleteConfirmPanel");
            if (confirmPanel == null) return;

            // przełącz widoczność; przed pokazaniem ukryj inne panele
            if (confirmPanel.Visibility == Visibility.Visible)
            {
                confirmPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                HideAllDeleteConfirmPanels();
                confirmPanel.Visibility = Visibility.Visible;
            }
        }

        // potwierdź usunięcie (Tak)
        private void ConfirmDeleteEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not DataRowView drv) return;

            var row = drv.Row;
            var id = Convert.ToInt32(row["Id"]);
            var allocated = SafeDec(row["Allocated"]);

            try
            {
                var newSaved = _savedTotal + allocated;
                DatabaseService.SetSavedCash(_userId, newSaved);
                _savedTotal = newSaved;

                DatabaseService.DeleteEnvelope(id);

                LoadAll();
                FormBorder.Visibility = Visibility.Collapsed;
                FormMessage.Text = "Kopertę usunięto.";
            }
            catch (Exception ex)
            {
                FormMessage.Text = "Błąd usuwania: " + ex.Message;
            }
        }

        // anuluj potwierdzenie (Nie)
        private void CancelDeleteEnvelope_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var cardBorder = FindVisualParent<Border>(btn);
            if (cardBorder == null) return;
            var confirmPanel = FindDescendantByName<FrameworkElement>(cardBorder, "DeleteConfirmPanel");
            if (confirmPanel != null) confirmPanel.Visibility = Visibility.Collapsed;
        }

        private void SaveEnvelope_Click(object sender, RoutedEventArgs e)
        {
            var name = (NameBox.Text ?? "").Trim();
            var target = SafeParse(TargetBox.Text);
            var allocated = SafeParse(AllocatedBox.Text);
            var goal = GoalBox.Text ?? string.Empty;
            var description = DescriptionBox.Text ?? string.Empty;
            var deadline = DeadlinePicker.SelectedDate;

            if (string.IsNullOrWhiteSpace(name))
            {
                FormMessage.Text = "Podaj nazwę koperty.";
                return;
            }

            if (allocated < 0 || target < 0)
            {
                FormMessage.Text = "Kwoty nie mogą być ujemne.";
                return;
            }

            // różnica przydzielonej kwoty względem poprzedniego stanu
            decimal previousAllocated = 0m;
            if (_editingId is int idExisting && _dt != null)
            {
                var row = _dt.AsEnumerable()
                             .FirstOrDefault(r => Convert.ToInt32(r["Id"]) == idExisting);
                if (row != null)
                    previousAllocated = SafeDec(row["Allocated"]);
            }

            var delta = allocated - previousAllocated;   // ile nowej kasy dokładamy
            var newSavedTotal = _savedTotal - delta;     // zdejmujemy z odłożonej gotówki

            if (newSavedTotal < 0)
            {
                FormMessage.Text = "Nie masz tyle odłożonej gotówki, aby przydzielić tę kwotę do kopert.";
                return;
            }

            var note = BuildNote(goal, description, deadline);

            try
            {
                int envelopeId;

                if (_editingId is int id)
                {
                    DatabaseService.UpdateEnvelope(id, _userId, name, target, allocated, note);
                    envelopeId = id;
                    FormMessage.Text = "Zapisano zmiany.";
                }
                else
                {
                    envelopeId = DatabaseService.InsertEnvelope(_userId, name, target, allocated, note);
                    FormMessage.Text = "Dodano kopertę.";
                }

                // zapisujemy nową wartość odłożonej gotówki
                DatabaseService.SetSavedCash(_userId, newSavedTotal);
                _savedTotal = newSavedTotal;

                // *** NOWE: zapisujemy cel/termin także w kolumnach używanych przez stronę „Cele” ***
                if (deadline.HasValue)
                {
                    DatabaseService.UpdateEnvelopeGoal(
                        _userId,
                        envelopeId,
                        target,
                        allocated,
                        deadline.Value,
                        note
                    );
                }

                _editingId = null;
                LoadAll();
                FormBorder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                FormMessage.Text = "Błąd zapisu: " + ex.Message;
            }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            _editingId = null;
            FormBorder.Visibility = Visibility.Collapsed;
            FormMessage.Text = string.Empty;
        }

        // ===================== VisualTree helpers =====================

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            if (child == null) return null;
            DependencyObject? parent = VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not T)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        private static T? FindDescendantByName<T>(DependencyObject? start, string name) where T : FrameworkElement
        {
            if (start == null) return null;
            int cnt = VisualTreeHelper.GetChildrenCount(start);
            for (int i = 0; i < cnt; i++)
            {
                var ch = VisualTreeHelper.GetChild(start, i);
                if (ch is T fe && fe.Name == name) return fe;
                var deeper = FindDescendantByName<T>(ch, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private void HideAllDeleteConfirmPanels()
        {
            // przeszukaj visual tree od kontrolki EnvelopesCards i ukryj wszystkie panele DeleteConfirmPanel
            CollapseDeleteConfirm(EnvelopesCards);
        }

        private void CollapseDeleteConfirm(DependencyObject? parent)
        {
            if (parent == null) return;
            int cnt = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < cnt; i++)
            {
                var ch = VisualTreeHelper.GetChild(parent, i) as FrameworkElement;
                if (ch == null) continue;
                if (ch.Name == "DeleteConfirmPanel")
                    ch.Visibility = Visibility.Collapsed;
                CollapseDeleteConfirm(ch);
            }
        }
    }
}