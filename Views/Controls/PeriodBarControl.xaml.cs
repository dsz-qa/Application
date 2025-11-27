using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Finly.Models;

namespace Finly.Views.Controls
{
    public partial class PeriodBarControl : UserControl
    {
        // Kolejność presetów na strzałkach — dopasowane do DashboardPage
        private static readonly DateRangeMode[] PresetOrder =
        {
            DateRangeMode.Day,
            DateRangeMode.Week,
            DateRangeMode.Month,
            DateRangeMode.Quarter,
            DateRangeMode.Year
        };

        private DateTime? _tempStart;
        private DateTime? _tempEnd;
        private enum CalendarTarget { Start, End }
        private CalendarTarget? _calendarTarget;

        private int _displayYear;
        private int _displayMonth; //1-12

        public PeriodBarControl()
        {
            InitializeComponent();

            // DOMYŚLNIE: TEN MIESIĄC — zgodne z Dashboard
            ApplyPreset(DateRangeMode.Month, DateTime.Today);

            // ensure textboxes show nothing initially (dashboards hide concrete dates)
            var tbStart = FindName("StartTextBox") as TextBox;
            var tbEnd = FindName("EndTextBox") as TextBox;
            if (tbStart != null) tbStart.Text = string.Empty;
            if (tbEnd != null) tbEnd.Text = string.Empty;
        }

        // ===== Dependency Properties =====

        public static readonly DependencyProperty StartDateProperty =
            DependencyProperty.Register(
                nameof(StartDate),
                typeof(DateTime),
                typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(
                    DateTime.Today,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnRangePropChanged));

        public static readonly DependencyProperty EndDateProperty =
            DependencyProperty.Register(
                nameof(EndDate),
                typeof(DateTime),
                typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(
                    DateTime.Today,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnRangePropChanged));

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(
                nameof(Mode),
                typeof(DateRangeMode),
                typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(
                    DateRangeMode.Month, // spójne z konstruktorem
                    OnRangePropChanged));

        public static readonly DependencyProperty PeriodLabelProperty =
            DependencyProperty.Register(
                nameof(PeriodLabel),
                typeof(string),
                typeof(PeriodBarControl),
                new PropertyMetadata(string.Empty));

        private static void OnRangePropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (PeriodBarControl)d;
            c.UpdateLabel();
            c.RangeChanged?.Invoke(c, EventArgs.Empty);
        }

        public DateTime StartDate
        {
            get => (DateTime)GetValue(StartDateProperty);
            set => SetValue(StartDateProperty, value);
        }

        public DateTime EndDate
        {
            get => (DateTime)GetValue(EndDateProperty);
            set => SetValue(EndDateProperty, value);
        }

        public DateRangeMode Mode
        {
            get => (DateRangeMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        public string PeriodLabel
        {
            get => (string)GetValue(PeriodLabelProperty);
            set => SetValue(PeriodLabelProperty, value);
        }

        /// <summary>
        /// Zdarzenie dla Dashboardu – sygnał: zakres dat się zmienił.
        /// </summary>
        public event EventHandler? RangeChanged;
        public event EventHandler? SearchClicked;
        public event EventHandler? ClearClicked;

        // ===== Tekst na pasku =====

        private void UpdateLabel()
        {
            PeriodLabel = Mode switch
            {
                DateRangeMode.Day => "Dzisiaj",
                DateRangeMode.Week => "Ten tydzień",
                DateRangeMode.Month => "Ten miesiąc",
                DateRangeMode.Quarter => "Ten kwartał",
                DateRangeMode.Year => "Ten rok",
                DateRangeMode.Custom => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}",
                _ => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}"
            };
        }

        private void Search_Click(object s, RoutedEventArgs e)
        {
            // Apply temporary selections only when user clicks Search
            if (!_tempStart.HasValue || !_tempEnd.HasValue)
                return;

            var sDate = _tempStart.Value;
            var eDate = _tempEnd.Value;
            if (sDate > eDate) (sDate, eDate) = (eDate, sDate);

            StartDate = sDate.Date;
            EndDate = eDate.Date;
            Mode = DateRangeMode.Custom;

            // clear temps after applying
            _tempStart = null;
            _tempEnd = null;
            var tbStart = FindName("StartTextBox") as TextBox;
            var tbEnd = FindName("EndTextBox") as TextBox;
            if (tbStart != null) tbStart.Text = string.Empty;
            if (tbEnd != null) tbEnd.Text = string.Empty;

            SearchClicked?.Invoke(this, EventArgs.Empty);
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Clear_Click(object s, RoutedEventArgs e)
        {
            // Reset to textual preset "Ten miesiąc" and clear visible pickers
            SetPreset(DateRangeMode.Month);

            _tempStart = null;
            _tempEnd = null;
            var tbStart = FindName("StartTextBox") as TextBox;
            var tbEnd = FindName("EndTextBox") as TextBox;
            if (tbStart != null) tbStart.Text = string.Empty;
            if (tbEnd != null) tbEnd.Text = string.Empty;

            ClearClicked?.Invoke(this, EventArgs.Empty);
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }

        // ===== Presety =====

        private void ApplyPreset(DateRangeMode mode, DateTime anchor)
        {
            Mode = mode;

            switch (mode)
            {
                case DateRangeMode.Day:
                    StartDate = EndDate = anchor.Date;
                    break;

                case DateRangeMode.Week:
                    int diff = ((int)anchor.DayOfWeek + 6) % 7; // pon =0
                    StartDate = anchor.AddDays(-diff).Date;
                    EndDate = StartDate.AddDays(6);
                    break;

                case DateRangeMode.Month:
                    StartDate = new DateTime(anchor.Year, anchor.Month, 1);
                    EndDate = StartDate.AddMonths(1).AddDays(-1);
                    break;

                case DateRangeMode.Quarter:
                    int qStartMonth = (((anchor.Month - 1) / 3) * 3) + 1;
                    StartDate = new DateTime(anchor.Year, qStartMonth, 1);
                    EndDate = StartDate.AddMonths(3).AddDays(-1);
                    break;

                case DateRangeMode.Year:
                    StartDate = new DateTime(anchor.Year, 1, 1);
                    EndDate = new DateTime(anchor.Year, 12, 31);
                    break;

                case DateRangeMode.Custom:
                    break;
            }

            // After applying preset, hide concrete dates in textboxes to match Dashboard appearance
            var tbStart = FindName("StartTextBox") as TextBox;
            var tbEnd = FindName("EndTextBox") as TextBox;
            if (tbStart != null) tbStart.Text = string.Empty;
            if (tbEnd != null) tbEnd.Text = string.Empty;

            _tempStart = null;
            _tempEnd = null;

            UpdateLabel();
        }

        // ===== Strzałki – TYLKO presety (napisy) =====

        private void Prev_Click(object s, RoutedEventArgs e)
        {
            var idx = Array.IndexOf(PresetOrder, Mode);
            if (idx < 0) idx = 0;

            idx = (idx - 1 + PresetOrder.Length) % PresetOrder.Length;
            ApplyPreset(PresetOrder[idx], DateTime.Today);
        }

        private void Next_Click(object s, RoutedEventArgs e)
        {
            var idx = Array.IndexOf(PresetOrder, Mode);
            if (idx < 0) idx = 0;

            idx = (idx + 1) % PresetOrder.Length;
            ApplyPreset(PresetOrder[idx], DateTime.Today);
        }

        // ===== Publiczne API dla Dashboardu =====

        /// <summary>
        /// Ustawia preset (Dzień / Tydzień / Miesiąc / Kwartał / Rok).
        /// Użyj np. po kliknięciu przycisku "Wyczyść": SetPreset(DateRangeMode.Month).
        /// </summary>
        public void SetPreset(DateRangeMode mode) => ApplyPreset(mode, DateTime.Today);

        /// <summary>
        /// Ustawia własny zakres – używane po kliknięciu "Szukaj".
        /// Wtedy na pasku pojawia się tekst01.01.2025 –31.01.2025 itp.
        /// </summary>
        public void SetCustomRange(DateTime start, DateTime end)
        {
            if (start > end)
                (start, end) = (end, start);

            StartDate = start.Date;
            EndDate = end.Date;
            Mode = DateRangeMode.Custom;
            UpdateLabel();
        }

        private void PrevMonthBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_displayMonth == 1) { _displayMonth = 12; _displayYear--; }
            else _displayMonth--;
            RenderPopupMonth();
        }

        private void NextMonthBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_displayMonth == 12) { _displayMonth = 1; _displayYear++; }
            else _displayMonth++;
            RenderPopupMonth();
        }

        private void RenderPopupMonth()
        {
            var grid = FindName("PopupDaysGrid") as UniformGrid;
            var monthText = FindName("PopupMonthText") as TextBlock;
            if (grid == null || monthText == null) return;

            grid.Children.Clear();
            var first = new DateTime(_displayYear, _displayMonth, 1);
            int days = DateTime.DaysInMonth(_displayYear, _displayMonth);
            // Determine day of week index (Monday=0 ... Sunday=6)
            int startIndex = ((int)first.DayOfWeek + 6) % 7;

            // add blank slots before first day
            for (int i = 0; i < startIndex; i++) grid.Children.Add(new TextBlock());

            for (int d = 1; d <= days; d++)
            {
                var btn = new Button { Content = d.ToString(), Padding = new Thickness(6), Margin = new Thickness(2) };
                btn.Click += PopupDayButton_Click;
                btn.Tag = new DateTime(_displayYear, _displayMonth, d);
                grid.Children.Add(btn);
            }

            // fill remaining cells to keep grid consistent
            int totalCells = 42; //7x6
            int added = grid.Children.Count;
            for (int i = added; i < totalCells; i++) grid.Children.Add(new TextBlock());

            monthText.Text = new DateTime(_displayYear, _displayMonth, 1).ToString("MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
        }

        private void PopupDayButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is DateTime dt)
            {
                if (_calendarTarget == CalendarTarget.Start) { _tempStart = dt; var tb = FindName("StartTextBox") as TextBox; if (tb != null) tb.Text = dt.ToString("dd.MM.yyyy"); }
                else if (_calendarTarget == CalendarTarget.End) { _tempEnd = dt; var tb2 = FindName("EndTextBox") as TextBox; if (tb2 != null) tb2.Text = dt.ToString("dd.MM.yyyy"); }

                var popup = FindName("CalendarPopup") as Popup;
                if (popup != null) popup.IsOpen = false;
            }
        }

        // When opening popup, set displayed month to appropriate date
        private void OpenCalendarFor(DateTime date)
        {
            _displayYear = date.Year; _displayMonth = date.Month;
            RenderPopupMonth();
        }

        private void StartDateIcon_Click(object sender, RoutedEventArgs e)
        {
            _calendarTarget = CalendarTarget.Start;
            var popup = FindName("CalendarPopup") as Popup;
            if (popup != null)
            {
                DateTime start;
                if (_tempStart.HasValue) start = _tempStart.Value;
                else if (Mode == DateRangeMode.Custom) start = StartDate;
                else start = DateTime.Today;

                OpenCalendarFor(start);
                popup.PlacementTarget = sender as UIElement;
                popup.Placement = PlacementMode.Bottom;
                popup.IsOpen = true;
            }
        }

        private void EndDateIcon_Click(object sender, RoutedEventArgs e)
        {
            _calendarTarget = CalendarTarget.End;
            var popup = FindName("CalendarPopup") as Popup;
            if (popup != null)
            {
                DateTime end;
                if (_tempEnd.HasValue) end = _tempEnd.Value;
                else if (Mode == DateRangeMode.Custom) end = EndDate;
                else end = DateTime.Today;

                OpenCalendarFor(end);
                popup.PlacementTarget = sender as UIElement;
                popup.Placement = PlacementMode.Bottom;
                popup.IsOpen = true;
            }
        }

        // Popup OK/Cancel already implemented earlier; ensure signatures match XAML
        private void PopupOk_Click(object sender, RoutedEventArgs e)
        {
            // Selection already applied when day button clicked; simply close popup
            var popup = FindName("CalendarPopup") as Popup;
            if (popup != null) popup.IsOpen = false;
        }

        private void PopupCancel_Click(object sender, RoutedEventArgs e)
        {
            var popup = FindName("CalendarPopup") as Popup;
            if (popup != null) popup.IsOpen = false;
        }
    }
}

















































































