using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Finly.Services;
using Finly.Services.Features;
using Finly.Services.SpecificPages;

namespace Finly.Pages
{
    public class InvestmentVm
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal TargetAmount { get; set; }
        public decimal CurrentAmount { get; set; }
        public DateTime? TargetDate { get; set; }
        public string Description { get; set; } = "";

        public decimal Remaining => Math.Max(0, TargetAmount - CurrentAmount);

        public int MonthsLeft
        {
            get
            {
                if (TargetDate == null) return 0;
                var today = DateTime.Today;
                var d = TargetDate.Value.Date;
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

    public sealed class AddInvestmentTile { }

    public partial class InvestmentsPage : UserControl
    {
        private readonly ObservableCollection<InvestmentVm> _investments = new();
        private readonly ObservableCollection<object> _items = new();
        private int _nextId = 1;
        private int? _editingId = null;
        private bool _initializedOk = false;

        public InvestmentsPage()
        {
            try
            {
                InitializeComponent();
                _initializedOk = true;

                if (InvestmentsRepeater != null)
                {
                    try { InvestmentsRepeater.ItemsSource = _items; } catch { }
                }

                Loaded += InvestmentsPage_Loaded;

                // Refresh when DB changes elsewhere in app
                try { DatabaseService.DataChanged += DatabaseService_DataChanged; } catch { }

                // Unsubscribe to avoid leaks when control unloaded
                Unloaded += (_, _) => { try { DatabaseService.DataChanged -= DatabaseService_DataChanged; } catch { } };

                try { if (FormBorder != null) FormBorder.Visibility = Visibility.Collapsed; } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("InvestmentsPage init error: " + ex);
                var tb = new TextBlock
                {
                    Text = "Nie można załadować strony Inwestycje:\n" + ex.Message,
                    Foreground = Brushes.OrangeRed,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16)
                };
                this.Content = tb;
            }
        }

        private void InvestmentsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadInvestments();
        }

        private void LoadInvestments()
        {
            try
            {
                _investments.Clear();

                try
                {
                    var uid = UserService.GetCurrentUserId();
                    if (uid > 0)
                    {
                        var rows = DatabaseService.GetInvestments(uid);
                        foreach (var r in rows)
                        {
                            var vm = new InvestmentVm
                            {
                                Id = r.Id,
                                Name = r.Name,
                                TargetAmount = r.TargetAmount,
                                CurrentAmount = r.CurrentAmount,
                                TargetDate = string.IsNullOrWhiteSpace(r.TargetDate) ? null : DateTime.TryParse(r.TargetDate, out var d) ? d : (DateTime?)null,
                                Description = r.Description ?? ""
                            };
                            _investments.Add(vm);
                            if (r.Id >= _nextId) _nextId = r.Id + 1;
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("LoadInvestments DB error: " + ex); try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Finly", "Logs", "investments_errors.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] LoadInvestments DB error: {ex}\n"); } catch { } }

                RebuildItems();
                RefreshKpis();

                // hide any previous inline error
                try { InvestmentsErrorText.Visibility = Visibility.Collapsed; } catch { }
            }
            catch (Exception ex)
            {
                // Show a non-modal inline error on the page instead of forcing a MessageBox
                try
                {
                    InvestmentsErrorText.Text = "Błąd podczas ładowania inwestycji: " + ex.Message;
                    InvestmentsErrorText.Visibility = Visibility.Visible;
                }
                catch { }
                System.Diagnostics.Debug.WriteLine("LoadInvestments error: " + ex);
            }
        }

        private void RebuildItems()
        {
            _items.Clear();
            foreach (var i in _investments) _items.Add(i);
            _items.Add(new AddInvestmentTile());
        }

        private void RefreshKpis()
        {
            var totalTarget = _investments.Sum(i => i.TargetAmount);
            var totalCurrent = _investments.Sum(i => i.CurrentAmount);
            var totalMonthly = _investments.Sum(i => i.MonthlyNeeded);

            TotalTargetText.Text = totalTarget.ToString("N2") + " zł";
            TotalCurrentText.Text = totalCurrent.ToString("N2") + " zł";
            TotalMonthlyText.Text = totalMonthly.ToString("N2") + " zł";
        }

        // helpers wyszukiwania elementów w template
        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static T? FindDescendantByName<T>(DependencyObject start, string name) where T : FrameworkElement
        {
            if (start == null) return null;
            int childCount = VisualTreeHelper.GetChildrenCount(start);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(start, i);
                if (child is T fe && fe.Name == name) return fe;
                var found = FindDescendantByName<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // Dodaj -> otwórz formularz
        private void AddInvestmentCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_initializedOk)
            {
                ToastService.Error("Strona Inwestycje nie została poprawnie zainicjalizowana.");
                return;
            }

            try
            {
                _editingId = null;
                if (FormHeader != null) FormHeader.Text = "Dodaj inwestycję";
                if (InvestmentFormMessage != null) InvestmentFormMessage.Text = string.Empty;
                if (FormBorder != null) FormBorder.Visibility = Visibility.Visible;
                ClearInvestmentForm();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AddInvestmentCard_Click error: " + ex);
                ToastService.Error("Nie można otworzyć formularza inwestycji: " + ex.Message);
            }
        }

        // Edycja
        private void EditInvestment_Click(object sender, RoutedEventArgs e)
        {
            if (!_initializedOk)
            {
                ToastService.Error("Strona Inwestycje nie została poprawnie zainicjalizowana.");
                return;
            }

            try
            {
                if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;
                _editingId = vm.Id;
                if (FormHeader != null) FormHeader.Text = "Edytuj inwestycję";
                if (InvestmentFormMessage != null) InvestmentFormMessage.Text = string.Empty;
                if (FormBorder != null) FormBorder.Visibility = Visibility.Visible;

                if (InvestmentNameBox != null) InvestmentNameBox.Text = vm.Name;
                if (InvestmentTargetBox != null) InvestmentTargetBox.Text = vm.TargetAmount.ToString("N2");
                if (InvestmentCurrentBox != null) InvestmentCurrentBox.Text = vm.CurrentAmount.ToString("N2");
                if (InvestmentTargetDatePicker != null) InvestmentTargetDatePicker.SelectedDate = vm.TargetDate;
                if (InvestmentDescriptionBox != null) InvestmentDescriptionBox.Text = vm.Description ?? "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EditInvestment_Click error: " + ex);
                ToastService.Error("Nie można otworzyć formularza edycji: " + ex.Message);
            }
        }

        // Pokaż panel potwierdzenia usunięcia
        private void DeleteInvestment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var root = FindAncestor<Border>(fe);
                if (root != null)
                {
                    var panel = FindDescendantByName<StackPanel>(root, "InvestmentDeleteConfirmPanel");
                    if (panel != null) panel.Visibility = Visibility.Visible;
                }
            }
        }

        private void DeleteInvestmentCancel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var root = FindAncestor<Border>(fe);
                if (root != null)
                {
                    var panel = FindDescendantByName<StackPanel>(root, "InvestmentDeleteConfirmPanel");
                    if (panel != null) panel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void DeleteInvestmentConfirm_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm)
                return;

            // TODO: jeśli chcesz usuwać w DB -> wywołaj tutaj DatabaseService.DeleteInvestment(vm.Id)
            _investments.Remove(vm);
            RebuildItems();
            RefreshKpis();

            if (sender is FrameworkElement fe)
            {
                var root = FindAncestor<Border>(fe);
                if (root != null)
                {
                    var panel = FindDescendantByName<StackPanel>(root, "InvestmentDeleteConfirmPanel");
                    if (panel != null) panel.Visibility = Visibility.Collapsed;
                }
            }
        }

        // Zapisz z formularza
        private void AddInvestment_Click(object sender, RoutedEventArgs e)
        {
            if (!_initializedOk)
            {
                ToastService.Error("Strona Inwestycje nie została poprawnie zainicjalizowana.");
                return;
            }

            try
            {
                var name = (InvestmentNameBox?.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    if (InvestmentFormMessage != null) InvestmentFormMessage.Text = "Podaj nazwę inwestycji.";
                    return;
                }

                if (!decimal.TryParse(InvestmentTargetBox?.Text?.Replace(" ", ""), NumberStyles.Number, CultureInfo.CurrentCulture, out var target))
                    target = 0m;
                if (!decimal.TryParse(InvestmentCurrentBox?.Text?.Replace(" ", ""), NumberStyles.Number, CultureInfo.CurrentCulture, out var current))
                    current = 0m;

                var date = InvestmentTargetDatePicker?.SelectedDate;
                var desc = InvestmentDescriptionBox?.Text ?? "";

                if (target <= 0)
                {
                    if (InvestmentFormMessage != null) InvestmentFormMessage.Text = "Wartość docelowa musi być większa niż0.";
                    return;
                }

                if (current < 0)
                {
                    if (InvestmentFormMessage != null) InvestmentFormMessage.Text = "Aktualna wartość nie może być ujemna.";
                    return;
                }

                if (current > target)
                {
                    if (InvestmentFormMessage != null) InvestmentFormMessage.Text = "Aktualna wartość nie może przekraczać wartości docelowej.";
                    return;
                }

                if (!date.HasValue)
                {
                    if (InvestmentFormMessage != null) InvestmentFormMessage.Text = "Wybierz termin docelowy.";
                    return;
                }

                if (_editingId.HasValue)
                {
                    var existing = _investments.FirstOrDefault(x => x.Id == _editingId.Value);
                    if (existing != null)
                    {
                        existing.Name = name;
                        existing.TargetAmount = target;
                        existing.CurrentAmount = current;
                        existing.TargetDate = date;
                        existing.Description = desc;
                        try
                        {
                            var m = new Models.InvestmentModel
                            {
                                Id = existing.Id,
                                UserId = UserService.GetCurrentUserId(),
                                Name = existing.Name,
                                TargetAmount = existing.TargetAmount,
                                CurrentAmount = existing.CurrentAmount,
                                TargetDate = existing.TargetDate?.ToString("yyyy-MM-dd"),
                                Description = existing.Description
                            };
                            DatabaseService.UpdateInvestment(m);
                            ToastService.Success("Zaktualizowano inwestycję.");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("UpdateInvestment DB error: " + ex);
                            try { if (InvestmentFormMessage != null) InvestmentFormMessage.Text = "Błąd zapisu do bazy: " + ex.Message; } catch { }
                        }
                    }
                }
                else
                {
                    var vm = new InvestmentVm
                    {
                        Id = _nextId++,
                        Name = name,
                        TargetAmount = target,
                        CurrentAmount = current,
                        TargetDate = date,
                        Description = desc
                    };
                    try
                    {
                        var m = new Models.InvestmentModel
                        {
                            UserId = UserService.GetCurrentUserId(),
                            Name = vm.Name,
                            TargetAmount = vm.TargetAmount,
                            CurrentAmount = vm.CurrentAmount,
                            TargetDate = vm.TargetDate?.ToString("yyyy-MM-dd"),
                            Description = vm.Description
                        };
                        var newId = DatabaseService.InsertInvestment(m);
                        vm.Id = newId;
                        _investments.Add(vm);
                        ToastService.Success("Dodano inwestycję.");
                        if (newId >= _nextId) _nextId = newId + 1;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("InsertInvestment DB error: " + ex);
                        try { if (InvestmentFormMessage != null) InvestmentFormMessage.Text = "Błąd zapisu do bazy: " + ex.Message; } catch { }
                        // Fallback: keep in-memory item so user doesn't lose data
                        _investments.Add(vm);
                    }
                }

                // After DB operation reload investments from DB to ensure UI shows persisted data immediately
                try
                {
                    LoadInvestments();

                    // Force ItemsControl to refresh binding to avoid stale visuals
                    try
                    {
                        if (InvestmentsRepeater != null)
                        {
                            InvestmentsRepeater.ItemsSource = null;
                            InvestmentsRepeater.ItemsSource = _items;
                        }
                    }
                    catch { }

                    RefreshKpis();
                    ClearInvestmentForm();
                    if (FormBorder != null) FormBorder.Visibility = Visibility.Collapsed;
                    _editingId = null;
                    if (InvestmentFormMessage != null) InvestmentFormMessage.Text = string.Empty;

                    // Scroll the newly added/updated investment into view after layout
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // find investment by name+target+date heuristic (new id may have changed)
                            var last = _investments.OrderByDescending(i => i.Id).FirstOrDefault();
                            if (last != null && InvestmentsRepeater != null)
                            {
                                var container = InvestmentsRepeater.ItemContainerGenerator.ContainerFromItem(last) as FrameworkElement;
                                if (container != null)
                                {
                                    container.BringIntoView();
                                }
                                else
                                {
                                    try { InvestmentsRepeater.BringIntoView(); } catch { }
                                }
                            }
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("AddInvestment post-update error: " + ex);
                    try { if (InvestmentFormMessage != null) InvestmentFormMessage.Text = "Nie udało się odświeżyć listy: " + ex.Message; } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AddInvestment_Click error: " + ex);
                // Log detailed info to file for diagnostics
                try
                {
                    var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Finly", "Logs");
                    Directory.CreateDirectory(logDir);
                    var file = Path.Combine(logDir, "investments_errors.log");
                    var info = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AddInvestment_Click error:\n{ex}\nForm values:\nName={InvestmentNameBox?.Text}\nTarget={InvestmentTargetBox?.Text}\nCurrent={InvestmentCurrentBox?.Text}\nDate={InvestmentTargetDatePicker?.SelectedDate}\nDescription={InvestmentDescriptionBox?.Text}\n\n";
                    File.AppendAllText(file, info);
                }
                catch { }

                var msg = "Błąd zapisu inwestycji: " + ex.Message;
                try { if (InvestmentFormMessage != null) InvestmentFormMessage.Text = msg; else ToastService.Error(msg); } catch { }
            }
        }

        private void ClearInvestmentForm_Click(object sender, RoutedEventArgs e)
        {
            ClearInvestmentForm();
            InvestmentFormMessage.Text = string.Empty;
            FormBorder.Visibility = Visibility.Collapsed;
            _editingId = null;
        }

        private void ClearInvestmentForm()
        {
            InvestmentNameBox.Text = "";
            InvestmentTargetBox.Text = "0,00";
            InvestmentCurrentBox.Text = "0,00";
            InvestmentTargetDatePicker.SelectedDate = null;
            InvestmentDescriptionBox.Text = "";
        }

        private void AmountBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (tb.Text == "0,00" || tb.Text == "0.00") tb.Clear();
            }
        }

        private void AmountBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (decimal.TryParse(tb.Text?.Replace(" ", ""), NumberStyles.Number, CultureInfo.CurrentCulture, out var d))
                    tb.Text = d.ToString("N2");
                else
                    tb.Text = "0,00";
            }
        }

        private void DatabaseService_DataChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() => LoadInvestments()), DispatcherPriority.Background);
            }
            catch { }
        }
    }
}