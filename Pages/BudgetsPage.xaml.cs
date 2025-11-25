using Finly.Models;
using Finly.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Finly.Pages
{
    public partial class BudgetsPage : UserControl
    {
        private int _userId;
        private List<BudgetModel> _budgets = new List<BudgetModel>();
        private readonly Brush _ok = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private readonly Brush _warn = new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00));
        private readonly Brush _danger = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));

        public BudgetsPage()
        {
            InitializeComponent();
            Loaded += BudgetsPage_Loaded;
        }

        public BudgetsPage(int userId) : this()
        {
            _userId = userId;
        }

        private void BudgetsPage_Loaded(object? sender, RoutedEventArgs e)
        {
            _userId = _userId > 0 ? _userId : UserService.GetCurrentUserId();
            LoadCategories();
            LoadBudgets();
            RefreshAll();
        }

        private void LoadCategories()
        {
            try
            {
                var dt = DatabaseService.GetCategories(_userId);
                var list = new List<string> { "" };
                if (dt != null)
                {
                    foreach (DataRow r in dt.Rows)
                        list.Add(r["Name"]?.ToString() ?? "");
                }
                CategoryBox.ItemsSource = list;
            }
            catch
            {
                CategoryBox.ItemsSource = Array.Empty<string>();
            }
        }

        private void LoadBudgets()
        {
            _budgets = BudgetService.LoadBudgets(_userId) ?? new List<BudgetModel>();
        }

        private void SaveBudgets() => BudgetService.SaveBudgets(_userId, _budgets);

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_userId <= 0) return;

            var name = (NameBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                ToastService.Info("Podaj nazwę budżetu.");
                return;
            }

            if (!decimal.TryParse((AmountBox.Text ?? "").Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
            {
                ToastService.Info("Podaj poprawną kwotę.");
                return;
            }

            var typeTag = (TypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Monthly";
            var type = typeTag switch
            {
                "Weekly" => BudgetType.Weekly,
                "Rollover" => BudgetType.Rollover,
                "OneTime" => BudgetType.OneTime,
                _ => BudgetType.Monthly
            };

            int? catId = null;
            string? catName = null;
            var catStr = (CategoryBox.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(catStr))
            {
                try
                {
                    var cats = DatabaseService.GetCategories(_userId);
                    if (cats != null)
                    {
                        foreach (DataRow r in cats.Rows)
                        {
                            var n = r["Name"]?.ToString() ?? "";
                            if (string.Equals(n, catStr, StringComparison.OrdinalIgnoreCase))
                            {
                                try { catId = Convert.ToInt32(r["Id"]); } catch { catId = null; }
                                catName = n;
                                break;
                            }
                        }
                    }
                }
                catch { }

                if (catId == null) catName = catStr;
            }

            var b = new BudgetModel
            {
                Name = name,
                Type = type,
                CategoryId = catId,
                CategoryName = catName,
                Amount = amount
            };

            _budgets.Add(b);
            SaveBudgets();
            ToastService.Success("Dodano budżet.");

            // Dodaj panel dla nowego budżetu bez otwierania nowego okna — jako widget
            var panel = BuildBudgetPanel(b, expandDetails: true);
            BudgetsStack.Children.Add(panel);

            // opcjonalnie przewiń do dołu - jeśli masz ScrollViewer otaczający BudgetsStack
            // (tu nie dodajemy konkretnej implementacji scrollowania żeby nie zakładać struktury XAML)
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshAll();

        private void RefreshAll()
        {
            LoadBudgets();
            BudgetsStack.Children.Clear();

            decimal totalIncome = 0m;
            try
            {
                var snap = DatabaseService.GetMoneySnapshot(_userId);
                if (snap != null) totalIncome = snap.Total;
            }
            catch { }

            decimal totalPlanned = _budgets.Where(b => b.Active).Sum(b => b.Amount);
            string safetyText;
            if (totalIncome <= 0) safetyText = "Brak danych o przychodach";
            else if (totalPlanned <= totalIncome) safetyText = $"Stabilnie ({Math.Max(0, (int)(100 - (totalPlanned / totalIncome * 100)))}% wolne)";
            else safetyText = "Uwaga — wydajesz więcej niż zarabiasz";

            SafetyText.Text = safetyText;

            foreach (var b in _budgets.OrderBy(b => b.Name))
            {
                var panel = BuildBudgetPanel(b, expandDetails: false);
                BudgetsStack.Children.Add(panel);
            }
        }

        // Zmieniony: opcjonalnie od razu rozwiń sekcję szczegółów
        private UIElement BuildBudgetPanel(BudgetModel b, bool expandDetails = false)
        {
            var border = new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0,6,0,0),
                Tag = b.Id // ułatwia wyszukiwanie panelu
            };

            var stack = new StackPanel();
            border.Child = stack;

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = $"{b.Name} {(b.CategoryName != null ? $"• {b.CategoryName}" : "")}", FontWeight = FontWeights.SemiBold, Width = 300 });
            header.Children.Add(new TextBlock { Text = b.Type.ToString(), Foreground = Brushes.Gray, Margin = new Thickness(8, 0, 0, 0) });
            stack.Children.Add(header);

            var (from, to) = GetPeriodForBudget(b);
            stack.Children.Add(new TextBlock { Text = $"Okres: {from:dd.MM.yyyy} — {to:dd.MM.yyyy}", Foreground = Brushes.Gray, FontSize = 12 });

            decimal spent = CalculateSpent(b, from, to);

            decimal effectiveAmount = b.Amount;
            if (b.Type == BudgetType.Rollover)
                effectiveAmount += b.LastRollover;

            double pct = effectiveAmount > 0 ? (double)spent / (double)effectiveAmount * 100.0 : 0.0;
            if (pct < 0) pct = 0;

            var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Min(100, pct), Height = 18, Margin = new Thickness(0, 6, 0, 6) };
            if (pct < 75) progress.Foreground = _ok;
            else if (pct < 100) progress.Foreground = _warn;
            else progress.Foreground = _danger;
            stack.Children.Add(progress);

            var details = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            details.Children.Add(new TextBlock { Text = $"Plan: {effectiveAmount:N2} zł", Margin = new Thickness(0, 0, 12, 0) });
            details.Children.Add(new TextBlock { Text = $"Wydane: {spent:N2} zł" });
            stack.Children.Add(details);

            var daysTotal = (to - from).TotalDays + 1;
            var daysElapsed = (DateTime.Today - from).TotalDays + 1;
            if (daysElapsed < 1) daysElapsed = 1;
            var predictedTotal = daysElapsed > 0 ? (decimal)(spent / (decimal)daysElapsed * (decimal)daysTotal) : spent;

            var paceText = new TextBlock { Foreground = Brushes.DarkSlateGray, Margin = new Thickness(0, 4, 0, 0) };
            paceText.Text = $"Prognoza przy obecnym tempie: {predictedTotal:N2} zł (z planowanych {effectiveAmount:N2} zł)";
            stack.Children.Add(paceText);

            var chart = new Canvas { Height = 60, Margin = new Thickness(0, 6, 0, 6), Background = Brushes.Transparent };
            chart.SizeChanged += (s, e) => DrawMiniChart(chart, effectiveAmount, spent, predictedTotal);
            DrawMiniChart(chart, effectiveAmount, spent, predictedTotal);
            stack.Children.Add(chart);

            var notif = new TextBlock { FontStyle = FontStyles.Italic, Foreground = Brushes.DarkGray };
            if (pct >= 100)
            {
                notif.Text = $"Przekroczono budżet o {(spent - effectiveAmount):N2} zł";
                ToastService.Show($"Przekroczono budżet {b.Name} o {(spent - effectiveAmount):N2} zł", "warning");
            }
            else
            {
                var remainPct = effectiveAmount > 0
                    ? (double)((effectiveAmount - spent) / effectiveAmount) * 100.0
                    : 0.0;
                var daysLeft = (to - DateTime.Today).TotalDays;
                if (daysLeft > 0 && remainPct < 25)
                {
                    notif.Text = $"Uwaga: w tej kategorii pozostało {remainPct:N0}% środków, zostało {daysLeft:N0} dni.";
                    ToastService.Show($"W budżecie {b.Name} zostało {remainPct:N0}% środków", "info");
                }
            }

            if (!string.IsNullOrEmpty(notif.Text))
                stack.Children.Add(notif);

            // Expander jako widget szczegółów (inline)
            var detailsExpander = new Expander { Header = "Szczegóły", IsExpanded = expandDetails, Margin = new Thickness(0, 8, 0, 0) };
            var detPanel = new StackPanel { Margin = new Thickness(6, 4, 0, 0) };

            var prog = new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Min(100, (double)(effectiveAmount > 0 ? (double)spent / (double)effectiveAmount * 100.0 : 0.0)), Height = 16 };
            prog.Foreground = prog.Value < 75 ? _ok : prog.Value < 100 ? _warn : _danger;
            detPanel.Children.Add(prog);

            detPanel.Children.Add(new TextBlock { Text = $"Plan: {effectiveAmount:N2} zł", Margin = new Thickness(0, 6, 0, 0) });
            detPanel.Children.Add(new TextBlock { Text = $"Wydane: {spent:N2} zł" });
            detPanel.Children.Add(new TextBlock { Text = $"Prognoza: {predictedTotal:N2} zł" });

            var leftover = effectiveAmount - spent;
            detPanel.Children.Add(new TextBlock { Text = $"Pozostało: {leftover:N2} zł", Margin = new Thickness(0, 4, 0, 0) });

            var innerChart = new Canvas { Height = 80, Margin = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent };
            DrawMiniChart(innerChart, effectiveAmount, spent, predictedTotal);
            detPanel.Children.Add(innerChart);

            // akcje wewnątrz widgetu
            var innerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var moveBtn = new Button { Content = "Przenieś niewykorzystane", Width = 160, Margin = new Thickness(8, 0, 0, 0) };
            moveBtn.Click += (s, e) =>
            {
                if (leftover <= 0)
                {
                    ToastService.Info("Brak niewykorzystanych środków do przeniesienia.");
                    return;
                }

                var owner = Window.GetWindow(this);
                var res = MessageBox.Show(owner, $"Przenieść {leftover:N2} zł do oszczędności?", "Potwierdź", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    // Implementacja przeniesienia - tu przykładowe ustawienie i zapis
                    b.LastRollover += leftover;
                    SaveBudgets();
                    ToastService.Success($"Przeniesiono {leftover:N2} zł do oszczędności.");
                    // odśwież UI: zaktualizuj panel
                    // prosty sposób: zamknij expander i odśwież
                    detailsExpander.IsExpanded = false;
                    RefreshAll();
                }
            };
            innerActions.Children.Add(moveBtn);

            var closeDetBtn = new Button { Content = "Zamknij", Width = 80, Margin = new Thickness(8, 0, 0, 0) };
            closeDetBtn.Click += (s, e) => detailsExpander.IsExpanded = false;
            innerActions.Children.Add(closeDetBtn);

            detPanel.Children.Add(innerActions);
            detailsExpander.Content = detPanel;
            stack.Children.Add(detailsExpander);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var del = new Button { Content = "Usuń", Tag = b.Id, Width = 80 };
            del.Click += DeleteBudget_Click;
            actions.Children.Add(del);

            var toggle = new Button { Content = b.Active ? "Wyłącz" : "Włącz", Tag = b.Id, Width = 80, Margin = new Thickness(8, 0, 0, 0) };
            toggle.Click += ToggleBudget_Click;
            actions.Children.Add(toggle);

            var detailsBtn = new Button { Content = "Pokaż", Width = 80, Margin = new Thickness(8, 0, 0, 0) };
            detailsBtn.Click += (s, e) => detailsExpander.IsExpanded = !detailsExpander.IsExpanded;
            actions.Children.Add(detailsBtn);

            stack.Children.Add(actions);

            TryHandleRolloverPeriod(b, from, to);

            return border;
        }

        private void TryHandleRolloverPeriod(BudgetModel b, DateTime from, DateTime to)
        {
            if (b.Type != BudgetType.Rollover) return;

            var key = $"{from:yyyyMM}";
            if (b.LastPeriodKey == key) return;

            decimal spent = 0m;
            try
            {
                DataTable dt;
                if (b.CategoryId.HasValue)
                    dt = DatabaseService.GetExpenses(_userId, from, to, b.CategoryId.Value);
                else
                    dt = DatabaseService.GetExpenses(_userId, from, to);

                if (dt != null)
                {
                    foreach (DataRow r in dt.Rows)
                        try { spent += Math.Abs(Convert.ToDecimal(r["Amount"])); } catch { }
                }
            }
            catch { }

            var leftover = b.Amount - spent;
            if (leftover < 0) leftover = 0;
            b.LastRollover = leftover;
            b.LastPeriodKey = key;
            SaveBudgets();
        }

        private void DeleteBudget_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is Guid id)
            {
                var bud = _budgets.FirstOrDefault(x => x.Id == id);
                if (bud != null)
                {
                    _budgets.Remove(bud);
                    SaveBudgets();
                    ToastService.Info("Usunięto budżet.");
                    RefreshAll();
                }
            }
        }

        private void ToggleBudget_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is Guid id)
            {
                var bud = _budgets.FirstOrDefault(x => x.Id == id);
                if (bud != null)
                {
                    bud.Active = !bud.Active;
                    SaveBudgets();
                    RefreshAll();
                }
            }
        }

        private (DateTime from, DateTime to) GetPeriodForBudget(BudgetModel b)
        {
            var today = DateTime.Today;
            return b.Type switch
            {
                BudgetType.Weekly => (StartOfWeek(today), StartOfWeek(today).AddDays(6)),
                BudgetType.OneTime => (b.StartDate ?? today, b.EndDate ?? today),
                BudgetType.Rollover => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)),
                _ => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1))
            };
        }

        private DateTime StartOfWeek(DateTime dt)
        {
            var diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
            return dt.Date.AddDays(-diff);
        }

        private void DrawMiniChart(Canvas canvas, decimal planAmount, decimal spent, decimal predicted)
        {
            canvas.Children.Clear();
            double w = canvas.ActualWidth;
            if (w <= 0) w = canvas.RenderSize.Width;
            if (w <= 0) w = 300;
            double h = canvas.ActualHeight;
            if (h <= 0) h = canvas.RenderSize.Height;
            if (h <= 0) h = 60;

            double max = Math.Max((double)planAmount, Math.Max((double)spent, (double)predicted));
            if (max <= 0) max = 1;

            var planY = h - (double)planAmount / max * h;
            var planLine = new Line { X1 = 0, Y1 = planY, X2 = w, Y2 = planY, Stroke = Brushes.DarkGray, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 2 } };
            canvas.Children.Add(planLine);

            var spentX = w * 0.6;
            var spentY = h - (double)spent / max * h;
            var spentEllipse = new Ellipse { Width = 8, Height = 8, Fill = Brushes.DodgerBlue };
            Canvas.SetLeft(spentEllipse, spentX - 4);
            Canvas.SetTop(spentEllipse, spentY - 4);
            canvas.Children.Add(spentEllipse);

            var predictedX = w * 0.9;
            var predictedY = h - (double)predicted / max * h;
            var predEllipse = new Ellipse { Width = 6, Height = 6, Fill = Brushes.OrangeRed };
            Canvas.SetLeft(predEllipse, predictedX - 3);
            Canvas.SetTop(predEllipse, predictedY - 3);
            canvas.Children.Add(predEllipse);

            var l1 = new TextBlock { Text = $"Plan: {planAmount:N0} zł", FontSize = 11, IsHitTestVisible = false };
            Canvas.SetLeft(l1, 2);
            Canvas.SetTop(l1, 2);
            canvas.Children.Add(l1);

            var l2 = new TextBlock { Text = $"Wydane: {spent:N0} zł", FontSize = 11, IsHitTestVisible = false };
            Canvas.SetLeft(l2, Math.Max(2, spentX - 30));
            Canvas.SetTop(l2, Math.Max(2, spentY - 14));
            canvas.Children.Add(l2);
        }

        // Pomocnicza metoda: oblicza wydane w okresie dla budżetu.
        private decimal CalculateSpent(BudgetModel b, DateTime from, DateTime to)
        {
            decimal spent = 0m;
            try
            {
                DataTable dt;
                if (b.CategoryId.HasValue)
                    dt = DatabaseService.GetExpenses(_userId, from, to, b.CategoryId.Value);
                else
                    dt = DatabaseService.GetExpenses(_userId, from, to);

                if (dt != null)
                {
                    foreach (DataRow r in dt.Rows)
                        try { spent += Math.Abs(Convert.ToDecimal(r["Amount"])); } catch { }
                }
            }
            catch { }
            return spent;
        }
    }
}


