using Finly.Services;
using Finly.Services.Features;
using Finly.Views.Dialogs;
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

        // kolekcja kart (koperty + kafelek Dodaj)
        private readonly ObservableCollection<object> _cards = new();

        // aktualna "Odłożona gotówka" w bazie
        private decimal _savedTotal = 0m;

        public EnvelopesPage()
        {
            InitializeComponent();
            EnvelopesCards.ItemsSource = _cards;

            Loaded += EnvelopesPage_Loaded;
            Unloaded += EnvelopesPage_Unloaded;

            DatabaseService.DataChanged += DatabaseService_DataChanged;
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
        }

        // ===================== LOAD =====================

        private void LoadAll()
        {
            try
            {
                _savedTotal = DatabaseService.GetSavedCash(_userId);
                var cashOnHand = DatabaseService.GetCashOnHand(_userId);

                _dt = DatabaseService.GetEnvelopesTable(_userId);

                if (_dt != null)
                {
                    EnsureComputedColumns(_dt);

                    foreach (DataRow r in _dt.Rows)
                    {
                        var target = SafeDec(r["Target"]);
                        var alloc = SafeDec(r["Allocated"]);

                        r["Remaining"] = target - alloc;

                        SplitNote(r["Note"]?.ToString(), out var goal, out var description, out var deadline);

                        r["GoalText"] = string.IsNullOrWhiteSpace(goal) ? "Brak" : goal;
                        r["Description"] = string.IsNullOrWhiteSpace(description) ? "Brak" : description;

                        if (deadline.HasValue)
                        {
                            r["Deadline"] = deadline.Value.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);

                            var remaining = target - alloc;
                            if (remaining <= 0m)
                            {
                                r["MonthlyRequired"] = 0m;
                            }
                            else
                            {
                                int monthsLeft = MonthsBetween(DateTime.Today, deadline.Value);
                                if (monthsLeft <= 0) monthsLeft = 1;
                                r["MonthlyRequired"] = remaining / monthsLeft;
                            }
                        }
                        else
                        {
                            r["Deadline"] = "Brak";
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

                // ===== AGREGATY =====
                var allocatedSum = _dt?.AsEnumerable().Sum(r => SafeDec(r["Allocated"])) ?? 0m;

                var freeSpending = cashOnHand - _savedTotal;
                if (freeSpending < 0m) freeSpending = 0m;

                TotalEnvelopesText.Text = allocatedSum.ToString("N2", CultureInfo.CurrentCulture) + " zł";
                SavedCashText.Text = freeSpending.ToString("N2", CultureInfo.CurrentCulture) + " zł";

                var distributable = _savedTotal - allocatedSum;
                EnvelopesSumText.Text = distributable.ToString("N2", CultureInfo.CurrentCulture) + " zł";
                EnvelopesSumText.Foreground = distributable < 0
                    ? Brushes.IndianRed
                    : (Brush)FindResource("App.Foreground");
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd odczytu kopert: " + ex.Message);
            }
        }

        private static void EnsureComputedColumns(DataTable dt)
        {
            if (!dt.Columns.Contains("Remaining"))
                dt.Columns.Add("Remaining", typeof(decimal));

            if (!dt.Columns.Contains("GoalText"))
                dt.Columns.Add("GoalText", typeof(string));

            if (!dt.Columns.Contains("Description"))
                dt.Columns.Add("Description", typeof(string));

            if (!dt.Columns.Contains("Deadline"))
                dt.Columns.Add("Deadline", typeof(string));

            if (!dt.Columns.Contains("MonthlyRequired"))
                dt.Columns.Add("MonthlyRequired", typeof(decimal));
        }

        // ===================== UI: Add/Edit dialog =====================

        private void AddEnvelopeCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            OpenEnvelopeDialogAdd();
        }

        private void EditEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not DataRowView drv) return;

            var row = drv.Row;
            var id = Convert.ToInt32(row["Id"]);

            OpenEnvelopeDialogEdit(id, row);
        }

        private void OpenEnvelopeDialogAdd()
        {
            try
            {
                var dlg = new EnvelopeEditDialog
                {
                    Owner = Window.GetWindow(this)
                };

                dlg.SetMode(EnvelopeEditDialog.DialogMode.Add);
                dlg.LoadForAdd();

                if (dlg.ShowDialog() != true)
                    return;

                SaveEnvelopeFromDialog(dlg.Result);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się otworzyć okna koperty: " + ex.Message);
            }
        }

        private void OpenEnvelopeDialogEdit(int id, DataRow row)
        {
            try
            {
                var name = row["Name"]?.ToString() ?? "";
                var target = SafeDec(row["Target"]);
                var allocated = SafeDec(row["Allocated"]);
                var note = row["Note"]?.ToString() ?? "";

                var dlg = new EnvelopeEditDialog
                {
                    Owner = Window.GetWindow(this)
                };

                dlg.SetMode(EnvelopeEditDialog.DialogMode.Edit);
                dlg.LoadForEdit(id, name, target, allocated, note);

                if (dlg.ShowDialog() != true)
                    return;

                SaveEnvelopeFromDialog(dlg.Result);
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się otworzyć edycji koperty: " + ex.Message);
            }
        }

        private void SaveEnvelopeFromDialog(EnvelopeEditDialog.EnvelopeEditResult r)
        {
            // walidacja środków (zostaje jak było)
            if (!ValidateAllocationAgainstSavedCash(_userId, r.EditingId, r.Allocated, out var fundsMsg))
            {
                ToastService.Info(fundsMsg);
                return;
            }

            try
            {
                int envelopeId;

                if (r.EditingId is int editId)
                {
                    DatabaseService.UpdateEnvelope(editId, _userId, r.Name, r.Target, r.Allocated, r.Note);
                    envelopeId = editId;
                    ToastService.Success("Zapisano zmiany koperty.");
                }
                else
                {
                    envelopeId = DatabaseService.InsertEnvelope(_userId, r.Name, r.Target, r.Allocated, r.Note);
                    ToastService.Success("Dodano kopertę.");
                }

                // cel tylko gdy spełnione warunki
                if (r.ShouldCreateGoal && r.Deadline.HasValue)
                {
                    DatabaseService.UpdateEnvelopeGoal(
                        _userId,
                        envelopeId,
                        r.Target,
                        r.Allocated,
                        r.Deadline.Value,
                        r.Note
                    );
                }

                LoadAll();
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd zapisu koperty: " + ex.Message);
            }
        }

        // ===================== UI: Delete inline confirm =====================

        private void DeleteEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not DataRowView) return;

            var btn = sender as Button;
            var cardBorder = FindVisualParent<Border>(btn);
            if (cardBorder == null) return;

            var confirmPanel = FindDescendantByName<FrameworkElement>(cardBorder, "DeleteConfirmPanel");
            if (confirmPanel == null) return;

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

        private void ConfirmDeleteEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not DataRowView drv) return;

            var row = drv.Row;
            var id = Convert.ToInt32(row["Id"]);

            try
            {
                DatabaseService.DeleteEnvelope(id);
                ToastService.Success("Kopertę usunięto.");
                LoadAll();
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd usuwania koperty: " + ex.Message);
            }
            finally
            {
                HideAllDeleteConfirmPanels();
            }
        }

        private void CancelDeleteEnvelope_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var cardBorder = FindVisualParent<Border>(btn);
            if (cardBorder == null) return;

            var confirmPanel = FindDescendantByName<FrameworkElement>(cardBorder, "DeleteConfirmPanel");
            if (confirmPanel != null) confirmPanel.Visibility = Visibility.Collapsed;
        }

        // ===================== HELPERS =====================

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
            if (to.Day > from.Day) months++;
            return months;
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
                var ch = VisualTreeHelper.GetChild(start, i) as FrameworkElement;
                if (ch == null) continue;

                if (ch is T fe && fe.Name == name) return fe;

                var deeper = FindDescendantByName<T>(ch, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private void HideAllDeleteConfirmPanels()
        {
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

        private bool ValidateAllocationAgainstSavedCash(int userId, int? editingEnvelopeId, decimal newAllocated, out string message)
        {
            message = string.Empty;

            if (newAllocated < 0m)
            {
                message = "Kwota nie może być ujemna.";
                return false;
            }

            var savedTotal = DatabaseService.GetSavedCash(userId);

            var dt = DatabaseService.GetEnvelopesTable(userId);
            decimal totalAllocated = 0m;
            decimal previousAllocated = 0m;

            if (dt != null)
            {
                foreach (DataRow r in dt.Rows)
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

            if (newAllocated > availableForThis)
            {
                message = $"Masz za mało środków w „Odłożonej gotówce”. " +
                          $"Dostępne do przydzielenia: {availableForThis:N2} zł, próbujesz ustawić: {newAllocated:N2} zł.";
                return false;
            }

            return true;
        }

        // ===================== cleanup =====================

        private void EnvelopesPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= EnvelopesPage_Unloaded;
            Loaded -= EnvelopesPage_Loaded;

            try { DatabaseService.DataChanged -= DatabaseService_DataChanged; }
            catch { }
        }

        private void DatabaseService_DataChanged(object? sender, EventArgs e)
        {
            LoadAll();
        }
    }
}
