using Finly.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Finly.Pages
{
    // marker do kafelka "Dodaj kopertę"
    public sealed class AddEnvelopeTile { }

    // EnvelopeVm defined in Pages/EnvelopeVm.cs

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
            // Always ensure cards collection exists and contains the Add tile.
            _cards.Clear();
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

                    if (!_dt.Columns.Contains("Deadline"))
                        _dt.Columns.Add("Deadline", typeof(string));

                    if (!_dt.Columns.Contains("MonthlyRequired"))
                        _dt.Columns.Add("MonthlyRequired", typeof(decimal));

                    var envList = new List<EnvelopeVm>();

                    foreach (DataRow r in _dt.Rows)
                    {
                        var target = SafeDec(r["Target"]);
                        var alloc = SafeDec(r["Allocated"]);
                        r["Remaining"] = target - alloc;

                        SplitNote(r["Note"]?.ToString(), out var goal, out var description, out var deadline);
                        r["GoalText"] = goal;
                        r["Description"] = description; // expose description for tile

                        string dl = "";
                        if (deadline.HasValue)
                        {
                            dl = deadline.Value.ToString("d", CultureInfo.CurrentCulture);

                            var remaining = target - alloc;
                            if (remaining <=0m)
                            {
                                r["MonthlyRequired"] =0m;
                            }
                            else
                            {
                                int monthsLeft = MonthsBetween(DateTime.Today, deadline.Value);
                                if (monthsLeft <=0)
                                    monthsLeft =1;

                                r["MonthlyRequired"] = remaining / monthsLeft;
                            }
                        }
                        else
                        {
                            r["Deadline"] = "";
                            r["MonthlyRequired"] =0m;
                        }

                        // create VM for UI
                        var vm = new EnvelopeVm
                        {
                            Id = Convert.ToInt32(r["Id"]),
                            Name = r["Name"]?.ToString() ?? "(bez nazwy)",
                            Target = target,
                            Allocated = alloc,
                            GoalText = goal,
                            Description = description,
                            Deadline = dl,
                            Note = r["Note"]?.ToString() ?? ""
                        };

                        envList.Add(vm);
                    }

                    foreach (var ev in envList)
                        _cards.Add(ev);
                }

                var allocated = _dt?.AsEnumerable().Sum(r => SafeDec(r["Allocated"])) ??0m;
                var unassigned = _savedTotal - allocated;
                // treat tiny rounding differences as zero
                if (Math.Abs(decimal.Round(unassigned,2)) ==0m) unassigned =0m;

                TotalEnvelopesText.Text = allocated.ToString("N2") + " zł";
                SavedCashText.Text = _savedTotal.ToString("N2") + " zł";
                EnvelopesSumText.Text = allocated.ToString("N2") + " zł";

                UnassignedText.Text = unassigned.ToString("N2") + " zł";
                UnassignedText.Foreground = unassigned <0
                    ? Brushes.IndianRed
                    : (Brush)FindResource("App.Foreground");
            }
            catch (Exception ex)
            {
                FormMessage.Text = "Błąd odczytu: " + ex.Message;
                // set defaults so UI isn't empty
                TotalEnvelopesText.Text = "0,00 zł";
                SavedCashText.Text = "0,00 zł";
                EnvelopesSumText.Text = "0,00 zł";
                UnassignedText.Text = "0,00 zł";
            }
            finally
            {
                // Ensure Add tile is always present so user can add new envelope even if DB failed
                _cards.Add(new AddEnvelopeTile());
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

        private void SetEditMode(int id, EnvelopeVm vm)
        {
            _editingId = id;
            FormHeader.Text = "Edytuj kopertę";

            NameBox.Text = vm.Name;
            TargetBox.Text = vm.Target.ToString("N2");
            AllocatedBox.Text = vm.Allocated.ToString("N2");

            // parse note into fields
            SplitNote(vm.Note, out var goal, out var description, out var deadline);
            GoalBox.Text = goal;
            DescriptionBox.Text = description;
            DeadlinePicker.SelectedDate = deadline;

            RecalculateMonthlyRequired();

            FormMessage.Text = string.Empty;
            SaveEnvelopeBtn.Content = "Zapisz zmiany";
            FormBorder.Visibility = Visibility.Visible;
        }

        // helper do szukania panelu potwierdzenia w szablonie
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
            if ((sender as Button)?.Tag is EnvelopeVm vm)
            {
                SetEditMode(vm.Id, vm);
            }
        }

        /// <summary>
        /// Kliknięcie "Usuń" – tylko pokazuje panel potwierdzenia.
        /// </summary>
        private void DeleteEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var panel = FindTemplateChild<StackPanel>(fe, "EnvelopeDeleteConfirmPanel");
                if (panel != null)
                    panel.Visibility = Visibility.Visible;
            }
        }

        private void DeleteEnvelopeCancel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var panel = FindTemplateChild<StackPanel>(fe, "EnvelopeDeleteConfirmPanel");
                if (panel != null)
                    panel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Faktyczne usunięcie koperty po kliknięciu "Tak".
        /// </summary>
        private void DeleteEnvelopeConfirm_Click(object sender, RoutedEventArgs e)
        {
            EnvelopeVm? vm = null;
            if ((sender as FrameworkElement)?.DataContext is EnvelopeVm dctx)
                vm = dctx;

            if (vm == null && (sender as FrameworkElement)?.Tag is EnvelopeVm tagVm)
                vm = tagVm;

            if (vm == null) return;

            var id = vm.Id;
            var allocated = vm.Allocated;

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

            if (allocated <0 || target <0)
            {
                FormMessage.Text = "Kwoty nie mogą być ujemne.";
                return;
            }

            // previous allocated from existing VM if editing
            decimal previousAllocated =0m;
            if (_editingId is int idExisting)
            {
                var existingVm = _cards.OfType<EnvelopeVm>().FirstOrDefault(x => x.Id == idExisting);
                if (existingVm != null)
                    previousAllocated = existingVm.Allocated;
            }

            var delta = allocated - previousAllocated; // ile nowej kasy dokładamy
            var newSavedTotal = _savedTotal - delta; // zdejmujemy z odłożonej gotówki

            if (newSavedTotal <0)
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

                // zapisujemy też cel/termin pod stronę "Cele"
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
    }
}
