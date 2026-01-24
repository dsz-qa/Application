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

        // aktualna "Odłożona gotówka" w bazie (SavedCash)
        private decimal _savedTotal = 0m;

        // pilnujemy subskrypcji, żeby nie dublować eventów przy ponownym wejściu na stronę
        private bool _isSubscribed;

        public EnvelopesPage()
        {
            InitializeComponent();

            EnvelopesCards.ItemsSource = _cards;

            Loaded += EnvelopesPage_Loaded;
            Unloaded += EnvelopesPage_Unloaded;
        }

        private Point _dragStartPoint;
        private object? _dragItem;

        private void EnvelopesCards_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);

            // bierzemy element spod kursora
            var lbi = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (lbi?.DataContext is DataRowView)
                _dragItem = lbi.DataContext;     // tylko koperty
            else
                _dragItem = null;                // kafelek "Dodaj" albo coś innego
        }

        private void EnvelopesCards_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (_dragItem is not DataRowView) return;

            var pos = e.GetPosition(null);
            var diff = _dragStartPoint - pos;

            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            DragDrop.DoDragDrop(EnvelopesCards, _dragItem, DragDropEffects.Move);
        }

        private void EnvelopesCards_DragOver(object sender, DragEventArgs e)
        {
            // akceptujemy tylko koperta->koperta
            if (e.Data.GetDataPresent(typeof(DataRowView)))
                e.Effects = DragDropEffects.Move;
            else
                e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private void EnvelopesCards_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(DataRowView))) return;

            var source = e.Data.GetData(typeof(DataRowView)) as DataRowView;
            if (source == null) return;

            // target pod kursorem
            var lbi = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            var target = lbi?.DataContext as DataRowView;

            // nie pozwalamy upuszczać na kafelek "Dodaj" ani poza kartami
            if (target == null) return;
            if (ReferenceEquals(source, target)) return;

            // indeksy liczymy TYLKO wśród kopert (bez AddEnvelopeTile)
            var envelopeItems = _cards.OfType<DataRowView>().ToList();
            int oldIndex = envelopeItems.IndexOf(source);
            int newIndex = envelopeItems.IndexOf(target);

            if (oldIndex < 0 || newIndex < 0) return;

            // faktyczny indeks w _cards (uwzględnia AddEnvelopeTile na końcu)
            int oldIndexInCards = _cards.IndexOf(source);
            int newIndexInCards = _cards.IndexOf(target);

            if (oldIndexInCards < 0 || newIndexInCards < 0) return;

            // przeniesienie w UI
            _cards.Move(oldIndexInCards, newIndexInCards);

            // zapis kolejności do DB
            try
            {
                var orderedIds = _cards
                    .OfType<DataRowView>()
                    .Select(drv => Convert.ToInt32(drv.Row["Id"]))
                    .ToList();

                DatabaseService.SaveEnvelopesOrder(_userId, orderedIds);
                // nie musisz LoadAll() – UI już przestawione,
                // a DB order będzie użyty przy kolejnym wejściu/odświeżeniu.
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się zapisać kolejności kopert: " + ex.Message);
            }
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

            EnsureSubscriptions();
            LoadAll();
        }

        private void EnsureSubscriptions()
        {
            if (_isSubscribed) return;

            DatabaseService.DataChanged += DatabaseService_DataChanged;
            _isSubscribed = true;
        }

        private void RemoveSubscriptions()
        {
            if (!_isSubscribed) return;

            try { DatabaseService.DataChanged -= DatabaseService_DataChanged; }
            catch { /* ignore */ }

            _isSubscribed = false;
        }

        // ===================== LOAD =====================

        private void LoadAll()
        {
            try
            {
                // Snapshoty
                _savedTotal = DatabaseService.GetSavedCash(_userId);
                var cashOnHandTotal = DatabaseService.GetCashOnHand(_userId);

                // Koperty
                _dt = DatabaseService.GetEnvelopesTable(_userId);
                if (_dt != null)
                {
                    EnsureComputedColumns(_dt);

                    foreach (DataRow r in _dt.Rows)
                    {
                        var target = SafeDec(r["Target"]);
                        var alloc = SafeDec(r["Allocated"]);

                        // Remaining
                        r["Remaining"] = target - alloc;

                        // Cel/Opis/Termin z NOTE
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

                // Karty UI
                _cards.Clear();
                if (_dt != null)
                {
                    foreach (DataRowView rowView in _dt.DefaultView)
                        _cards.Add(rowView);
                }
                _cards.Add(new AddEnvelopeTile());

                // ===== AGREGATY =====
                var allocatedSum = _dt?.AsEnumerable().Sum(r => SafeDec(r["Allocated"])) ?? 0m;

                // Wolna gotówka = CashOnHand - SavedCash (SavedCash jest częścią CashOnHand)
                var freeCash = cashOnHandTotal - _savedTotal;
                if (freeCash < 0m) freeCash = 0m;

                // Do rozdysponowania w SavedCash po odjęciu alokacji kopert
                var distributable = _savedTotal - allocatedSum;

                TotalEnvelopesText.Text = allocatedSum.ToString("N2", CultureInfo.CurrentCulture) + " zł";
                SavedCashText.Text = freeCash.ToString("N2", CultureInfo.CurrentCulture) + " zł";

                EnvelopesSumText.Text = distributable.ToString("N2", CultureInfo.CurrentCulture) + " zł";
                EnvelopesSumText.Foreground = distributable < 0m
                    ? Brushes.IndianRed
                    : TryFindResource("App.Foreground") as Brush ?? Foreground;

                // Opcjonalny tekst "Brak przypisanej gotówki"
                if (UnassignedText != null)
                {
                    UnassignedText.Visibility = distributable > 0m ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                ToastService.Error("Błąd odczytu kopert: " + ex.Message);
            }
        }

        private static void EnsureComputedColumns(DataTable dt)
        {
            // Uwaga: GetEnvelopesTable zwraca tylko kolumny z SELECT-a,
            // więc dodajemy "wyliczane" kolumny do bindowania w XAML.
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
            // Walidacja środków w "Odłożonej gotówce"
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

                // Cel tylko gdy warunki spełnione
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
            try { return Convert.ToDecimal(o, CultureInfo.CurrentCulture); }
            catch
            {
                try { return Convert.ToDecimal(o, CultureInfo.InvariantCulture); }
                catch { return 0m; }
            }
        }

        private static int MonthsBetween(DateTime from, DateTime to)
        {
            if (to <= from) return 0;
            int months = (to.Year - from.Year) * 12 + (to.Month - from.Month);

            // jeśli termin ma dzień większy niż "from", doliczamy rozpoczęty miesiąc
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
                    // fallback: jeśli ktoś wpisał "po prostu tekst"
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
                    var alloc = SafeDec(r["Allocated"]);

                    totalAllocated += alloc;

                    if (editingEnvelopeId.HasValue && id == editingEnvelopeId.Value)
                        previousAllocated = alloc;
                }
            }

            var allocatedWithoutThis = totalAllocated - previousAllocated;
            var availableForThis = savedTotal - allocatedWithoutThis;

            if (newAllocated > availableForThis)
            {
                message =
                    $"Masz za mało środków w „Odłożonej gotówce”. " +
                    $"Dostępne do przydzielenia: {availableForThis:N2} zł, próbujesz ustawić: {newAllocated:N2} zł.";
                return false;
            }

            return true;
        }

        // ===================== cleanup =====================

        private void EnvelopesPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Loaded -= EnvelopesPage_Loaded;
            Unloaded -= EnvelopesPage_Unloaded;

            RemoveSubscriptions();
        }

        private void DatabaseService_DataChanged(object? sender, EventArgs e)
        {
            // jeśli kontrolka już “znika”, nie ma sensu odświeżać
            if (!IsLoaded) return;
            LoadAll();
        }
    }
}
