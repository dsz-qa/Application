using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Finly.Models;

namespace Finly.Views.Controls
{
    public partial class PeriodBarControl : UserControl
    {
        public PeriodBarControl()
        {
            InitializeComponent();
            if (StartDate == default) SetMode(DateRangeMode.Month, DateTime.Today);
            UpdateLabel();
        }

        // ========== DP ==========
        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode), typeof(DateRangeMode), typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(DateRangeMode.Month, OnRangeChanged));

        public static readonly DependencyProperty StartDateProperty =
            DependencyProperty.Register(nameof(StartDate), typeof(DateTime), typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(DateTime.Today, OnRangeChanged));

        public static readonly DependencyProperty EndDateProperty =
            DependencyProperty.Register(nameof(EndDate), typeof(DateTime), typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(DateTime.Today, OnRangeChanged));

        public DateRangeMode Mode
        {
            get => (DateRangeMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
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

        // Tekst w centrum
        public string PeriodLabel { get; private set; } = "";

        public event EventHandler? RangeChanged;

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (PeriodBarControl)d;
            c.Normalize();
            c.UpdateLabel();
            c.RangeChanged?.Invoke(c, EventArgs.Empty);
        }

        private void Normalize()
        {
            if (EndDate < StartDate) EndDate = StartDate;
        }

        private void UpdateLabel()
        {
            PeriodLabel = Mode switch
            {
                DateRangeMode.Day => StartDate.ToString("d"),
                DateRangeMode.Month => StartDate.ToString("MMM yyyy"),
                DateRangeMode.Quarter => $"Q{GetQuarter(StartDate)} {StartDate:yyyy}",
                _ => StartDate.ToString("yyyy")
            };
            // odśwież binding OneWayToSource do TextBlocka
            this.DataContext = null; this.DataContext = this;
        }

        // ========== Logika ==========
        private static int GetQuarter(DateTime d) => ((d.Month - 1) / 3) + 1;
        private static DateTime QuarterStart(DateTime d)
        {
            int q = GetQuarter(d);
            return new DateTime(d.Year, (q - 1) * 3 + 1, 1);
        }

        private void SetMode(DateRangeMode mode, DateTime anchor)
        {
            Mode = mode;
            switch (mode)
            {
                case DateRangeMode.Day:
                    StartDate = anchor.Date;
                    EndDate = anchor.Date;
                    break;
                case DateRangeMode.Month:
                    StartDate = new DateTime(anchor.Year, anchor.Month, 1);
                    EndDate = StartDate.AddMonths(1).AddDays(-1);
                    break;
                case DateRangeMode.Quarter:
                    StartDate = QuarterStart(anchor);
                    EndDate = StartDate.AddMonths(3).AddDays(-1);
                    break;
                case DateRangeMode.Year:
                    StartDate = new DateTime(anchor.Year, 1, 1);
                    EndDate = new DateTime(anchor.Year, 12, 31);
                    break;
            }
        }

        private void Shift(int dir)
        {
            switch (Mode)
            {
                case DateRangeMode.Day: SetMode(Mode, StartDate.AddDays(dir)); break;
                case DateRangeMode.Month: SetMode(Mode, StartDate.AddMonths(dir)); break;
                case DateRangeMode.Quarter: SetMode(Mode, StartDate.AddMonths(3 * dir)); break;
                case DateRangeMode.Year: SetMode(Mode, StartDate.AddYears(dir)); break;
            }
        }

        // ========== Handlery UI ==========
        private void Prev_Click(object sender, RoutedEventArgs e) => Shift(-1);
        private void Next_Click(object sender, RoutedEventArgs e) => Shift(+1);

        private void Center_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PickerPopup.IsOpen = true;
        }

        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is DateRangeMode m)
                SetMode(m, StartDate);
        }

        private void Pick_Today(object sender, RoutedEventArgs e) { SetMode(DateRangeMode.Day, DateTime.Today); }
        private void Pick_ThisMonth(object sender, RoutedEventArgs e) { SetMode(DateRangeMode.Month, DateTime.Today); }
        private void Pick_ThisQuarter(object sender, RoutedEventArgs e) { SetMode(DateRangeMode.Quarter, DateTime.Today); }
        private void Pick_ThisYear(object sender, RoutedEventArgs e) { SetMode(DateRangeMode.Year, DateTime.Today); }
        private void ClosePopup_Click(object sender, RoutedEventArgs e) { PickerPopup.IsOpen = false; }
    }
}
