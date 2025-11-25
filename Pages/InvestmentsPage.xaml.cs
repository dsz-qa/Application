using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

        public InvestmentsPage()
        {
            InitializeComponent();
            InvestmentsRepeater.ItemsSource = _items;
            Loaded += InvestmentsPage_Loaded;
            FormBorder.Visibility = Visibility.Collapsed;
        }

        private void InvestmentsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadInvestments();
        }

        private void LoadInvestments()
        {
            // TODO: tutaj podłącz odczyt z DB (jeśli masz DatabaseService.GetInvestments)
            _investments.Clear();

            // sample / placeholder — usuń lub zastąp danymi z DB
            _investments.Add(new InvestmentVm { Id = _nextId++, Name = "Fundusz emerytalny", TargetAmount = 50000m, CurrentAmount = 12000m, TargetDate = DateTime.Today.AddYears(5), Description = "Długoterminowa inwestycja" });

            RebuildItems();
            RefreshKpis();
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
            _editingId = null;
            FormHeader.Text = "Dodaj inwestycję";
            InvestmentFormMessage.Text = string.Empty;
            FormBorder.Visibility = Visibility.Visible;
            ClearInvestmentForm();
        }

        // Edycja
        private void EditInvestment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;
            _editingId = vm.Id;
            FormHeader.Text = "Edytuj inwestycję";
            InvestmentFormMessage.Text = string.Empty;
            FormBorder.Visibility = Visibility.Visible;

            InvestmentNameBox.Text = vm.Name;
            InvestmentTargetBox.Text = vm.TargetAmount.ToString("N2");
            InvestmentCurrentBox.Text = vm.CurrentAmount.ToString("N2");
            InvestmentTargetDatePicker.SelectedDate = vm.TargetDate;
            InvestmentDescriptionBox.Text = vm.Description ?? "";
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
            var name = (InvestmentNameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                InvestmentFormMessage.Text = "Podaj nazwę inwestycji.";
                return;
            }

            if (!decimal.TryParse(InvestmentTargetBox.Text?.Replace(" ", ""), NumberStyles.Number, CultureInfo.CurrentCulture, out var target))
                target = 0m;
            if (!decimal.TryParse(InvestmentCurrentBox.Text?.Replace(" ", ""), NumberStyles.Number, CultureInfo.CurrentCulture, out var current))
                current = 0m;

            var date = InvestmentTargetDatePicker.SelectedDate;
            var desc = InvestmentDescriptionBox.Text ?? "";

            if (target <= 0)
            {
                InvestmentFormMessage.Text = "Wartość docelowa musi być większa niż 0.";
                return;
            }

            if (current < 0)
            {
                InvestmentFormMessage.Text = "Aktualna wartość nie może być ujemna.";
                return;
            }

            if (current > target)
            {
                InvestmentFormMessage.Text = "Aktualna wartość nie może przekraczać wartości docelowej.";
                return;
            }

            if (!date.HasValue)
            {
                InvestmentFormMessage.Text = "Wybierz termin docelowy.";
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
                    // TODO: zapisz do DB jeśli potrzebujesz
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
                _investments.Add(vm);
                // TODO: zapisz do DB jeśli potrzebujesz
            }

            RebuildItems();
            RefreshKpis();
            ClearInvestmentForm();
            FormBorder.Visibility = Visibility.Collapsed;
            _editingId = null;
            InvestmentFormMessage.Text = string.Empty;
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
    }
}