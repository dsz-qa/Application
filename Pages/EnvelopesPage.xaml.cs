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

namespace Finly.Pages
{
    // marker do kafelka "Dodaj kopertę"
    public sealed class AddEnvelopeTile { }

    public partial class EnvelopesPage : UserControl
    {
        private int _userId;
        private DataTable? _dt;
        private int? _editingId = null;

        private readonly ObservableCollection<object> _cards = new();
        private decimal _savedTotal = 0m;   // aktualna "Odłożona gotówka" z bazy

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
                // 1) Odłożona gotówka
                _savedTotal = DatabaseService.GetSavedCash(_userId);

                // 2) Koperty
                _dt = DatabaseService.GetEnvelopesTable(_userId);

                if (_dt != null)
                {
                    if (!_dt.Columns.Contains("Remaining"))
                        _dt.Columns.Add("Remaining", typeof(decimal));

                    if (!_dt.Columns.Contains("GoalText"))
                        _dt.Columns.Add("GoalText", typeof(string));

                    foreach (DataRow r in _dt.Rows)
                    {
                        var target = SafeDec(r["Target"]);
                        var alloc = SafeDec(r["Allocated"]);
                        r["Remaining"] = target - alloc;

                        SplitNote(r["Note"]?.ToString(), out var goal, out _);
                        r["GoalText"] = goal;
                    }
                }

                // 3) Karty + kafelek "Dodaj kopertę"
                _cards.Clear();
                if (_dt != null)
                {
                    foreach (DataRowView rowView in _dt.DefaultView)
                        _cards.Add(rowView);
                }
                _cards.Add(new AddEnvelopeTile());

                // 4) Sumy
                var allocated = _dt?.AsEnumerable().Sum(r => SafeDec(r["Allocated"])) ?? 0m;
                var unassigned = _savedTotal - allocated;

                TotalEnvelopesText.Text = allocated.ToString("N2") + " zł";
                SavedCashText.Text = _savedTotal.ToString("N2") + " zł";
                EnvelopesSumText.Text = allocated.ToString("N2") + " zł";

                UnassignedText.Text = unassigned.ToString("N2") + " zł";
                UnassignedText.Foreground = unassigned < 0 ? Brushes.IndianRed : (Brush)FindResource("App.Foreground");
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

        /// <summary>
        /// Note jest zapisywany jako:
        /// "Cel: ....\nOpis: ...."
        /// </summary>
        private static void SplitNote(string? note, out string goal, out string description)
        {
            goal = string.Empty;
            description = string.Empty;

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
                else
                {
                    if (string.IsNullOrEmpty(goal))
                        goal = line;
                    else if (string.IsNullOrEmpty(description))
                        description = line;
                }
            }
        }

        private static string BuildNote(string goal, string description)
        {
            goal = (goal ?? "").Trim();
            description = (description ?? "").Trim();

            if (string.IsNullOrEmpty(goal) && string.IsNullOrEmpty(description))
                return string.Empty;

            if (string.IsNullOrEmpty(description))
                return $"Cel: {goal}";

            if (string.IsNullOrEmpty(goal))
                return $"Opis: {description}";

            return $"Cel: {goal}\nOpis: {description}";
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

            SplitNote(row["Note"]?.ToString(), out var goal, out var description);
            GoalBox.Text = goal;
            DescriptionBox.Text = description;

            FormMessage.Text = string.Empty;
            SaveEnvelopeBtn.Content = "Zapisz zmiany";
            FormBorder.Visibility = Visibility.Visible;
        }

        // ===================== ZDARZENIA UI =====================

        // kafelek "Dodaj kopertę"
        private void AddEnvelopeCard_Click(object sender, MouseButtonEventArgs e)
        {
            SetAddMode();
        }

        // ZERA znikają przy focuse
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
        }

        private void EditEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not DataRowView drv) return;
            var row = drv.Row;
            var id = Convert.ToInt32(row["Id"]);
            SetEditMode(id, row);
        }

        private void DeleteEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not DataRowView drv) return;

            var row = drv.Row;
            var id = Convert.ToInt32(row["Id"]);
            var allocated = SafeDec(row["Allocated"]);

            var result = MessageBox.Show(
                "Usunąć tę kopertę? Operacja nieodwracalna.",
                "Usuń kopertę",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                // zwracamy przydzieloną kwotę do odłożonej gotówki
                var newSaved = _savedTotal + allocated;
                DatabaseService.SetSavedCash(_userId, newSaved);

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
                var row = _dt.AsEnumerable().FirstOrDefault(r => Convert.ToInt32(r["Id"]) == idExisting);
                if (row != null)
                    previousAllocated = SafeDec(row["Allocated"]);
            }

            var delta = allocated - previousAllocated; // ile NOWEJ kasy chcemy dołożyć do kopert
            var newSavedTotal = _savedTotal - delta;   // zdejmujemy z odłożonej gotówki

            if (newSavedTotal < 0)
            {
                FormMessage.Text = "Nie masz tyle odłożonej gotówki, aby przydzielić tę kwotę do kopert.";
                return;
            }

            var note = BuildNote(goal, description);

            try
            {
                // zapis koperty
                if (_editingId is int id)
                {
                    DatabaseService.UpdateEnvelope(id, _userId, name, target, allocated, note);
                    FormMessage.Text = "Zapisano zmiany.";
                }
                else
                {
                    DatabaseService.InsertEnvelope(_userId, name, target, allocated, note);
                    FormMessage.Text = "Dodano kopertę.";
                }

                // aktualizacja odłożonej gotówki w bazie
                DatabaseService.SetSavedCash(_userId, newSavedTotal);

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
