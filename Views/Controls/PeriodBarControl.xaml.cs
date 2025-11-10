using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Views.Controls
{
    public enum DateRangeMode { Day, Month, Quarter, Year }

    public partial class PeriodBarControl : UserControl, INotifyPropertyChanged
    {
        public PeriodBarControl()
        {
            InitializeComponent();

            // Domyślna inicjalizacja – NIC nie odwołuje się do części wizualnych.
            if (StartDate == default || EndDate == default)
            {
                var today = DateTime.Today;
                SetMode(DateRangeMode.Month, today);
            }
            else
            {
                UpdateLabel();
            }
        }

        // ===== DependencyProperties =====
        public static readonly DependencyProperty StartDateProperty =
            DependencyProperty.Register(nameof(StartDate), typeof(DateTime), typeof(PeriodBarControl),
                new PropertyMetadata(default(DateTime), OnRangeChanged));

        public static readonly DependencyProperty EndDateProperty =
            DependencyProperty.Register(nameof(EndDate), typeof(DateTime), typeof(PeriodBarControl),
                new PropertyMetadata(default(DateTime), OnRangeChanged));

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode), typeof(DateRangeMode), typeof(PeriodBarControl),
                new PropertyMetadata(DateRangeMode.Month, OnRangeChanged));

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (PeriodBarControl)d;
            ctl.UpdateLabel();
            ctl.RangeChanged?.Invoke(ctl, EventArgs.Empty);
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

        // ===== PeriodLabel jako zwykłe CLR + INotifyPropertyChanged =====
        private string _periodLabel = "";
        public string PeriodLabel
        {
            get => _periodLabel;
            private set { _periodLabel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PeriodLabel))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? RangeChanged;

        // ===== Logika trybów =====
        private static (DateTime start, DateTime end) GetMonthRange(DateTime anchor)
        {
            var s = new DateTime(anchor.Year, anchor.Month, 1);
            var e = s.AddMonths(1).AddDays(-1);
            return (s, e);
        }

        private static (DateTime start, DateTime end) GetQuarterRange(DateTime anchor)
        {
            int qStartMonth = ((anchor.Month - 1) / 3) * 3 + 1;
            var s = new DateTime(anchor.Year, qStartMonth, 1);
            var e = s.AddMonths(3).AddDays(-1);
            return (s, e);
        }

        private static (DateTime start, DateTime end) GetYearRange(DateTime anchor)
        {
            var s = new DateTime(anchor.Year, 1, 1);
            var e = new DateTime(anchor.Year, 12, 31);
            return (s, e);
        }

        private void SetMode(DateRangeMode mode, DateTime anchor)
        {
            Mode = mode;

            (DateTime s, DateTime e) range = mode switch
            {
                DateRangeMode.Day => (anchor, anchor),
                DateRangeMode.Month => GetMonthRange(anchor),
                DateRangeMode.Quarter => GetQuarterRange(anchor),
                DateRangeMode.Year => GetYearRange(anchor),
                _ => (anchor, anchor)
            };

            StartDate = range.s;
            EndDate = range.e;
            UpdateLabel(); // bezpieczne – nie korzysta z wizualnych części
        }

        private void UpdateLabel()
        {
            string lbl = Mode switch
            {
                DateRangeMode.Day => StartDate.ToString("d MMM yyyy", CultureInfo.CurrentCulture),
                DateRangeMode.Month => StartDate.ToString("MMM yyyy", CultureInfo.CurrentCulture),
                DateRangeMode.Quarter =>
                    $"Q{((StartDate.Month - 1) / 3) + 1} {StartDate:yyyy}",
                DateRangeMode.Year => StartDate.ToString("yyyy"),
                _ => ""
            };
            PeriodLabel = lbl;
        }

        // ===== Handlery UI =====
        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            var anchor = StartDate;
            switch (Mode)
            {
                case DateRangeMode.Day: SetMode(Mode, anchor.AddDays(-1)); break;
                case DateRangeMode.Month: SetMode(Mode, anchor.AddMonths(-1)); break;
                case DateRangeMode.Quarter: SetMode(Mode, anchor.AddMonths(-3)); break;
                case DateRangeMode.Year: SetMode(Mode, anchor.AddYears(-1)); break;
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            var anchor = StartDate;
            switch (Mode)
            {
                case DateRangeMode.Day: SetMode(Mode, anchor.AddDays(1)); break;
                case DateRangeMode.Month: SetMode(Mode, anchor.AddMonths(1)); break;
                case DateRangeMode.Quarter: SetMode(Mode, anchor.AddMonths(3)); break;
                case DateRangeMode.Year: SetMode(Mode, anchor.AddYears(1)); break;
            }
        }

        private void Center_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PickerPopup.IsOpen = true;
        }

        private void ClosePopup_Click(object sender, RoutedEventArgs e)
        {
            PickerPopup.IsOpen = false;
            UpdateLabel();
        }

        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                var today = DateTime.Today;
                switch (tag)
                {
                    case "Day": SetMode(DateRangeMode.Day, today); break;
                    case "Month": SetMode(DateRangeMode.Month, today); break;
                    case "Quarter": SetMode(DateRangeMode.Quarter, today); break;
                    case "Year": SetMode(DateRangeMode.Year, today); break;
                }
            }
        }

        // Szybkie wybory
        private void Pick_Today(object sender, RoutedEventArgs e) { SetMode(DateRangeMode.Day, DateTime.Today); PickerPopup.IsOpen = false; }
        private void Pick_ThisMonth(object sender, RoutedEventArgs e) { SetMode(DateRangeMode.Month, DateTime.Today); PickerPopup.IsOpen = false; }
        private void Pick_ThisQuarter(object sender, RoutedEventArgs e) { SetMode(DateRangeMode.Quarter, DateTime.Today); PickerPopup.IsOpen = false; }
        private void Pick_ThisYear(object sender, RoutedEventArgs e) { SetMode(DateRangeMode.Year, DateTime.Today); PickerPopup.IsOpen = false; }
    }
}

