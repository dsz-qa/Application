using System;
using System.Windows;
using System.Windows.Controls;
using Finly.Models; // wspólny enum

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

        // ====== DP ======
        public static readonly DependencyProperty StartDateProperty =
            DependencyProperty.Register(nameof(StartDate), typeof(DateTime), typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangePropChanged));

        public static readonly DependencyProperty EndDateProperty =
            DependencyProperty.Register(nameof(EndDate), typeof(DateTime), typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangePropChanged));

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode), typeof(DateRangeMode), typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(DateRangeMode.Month, OnRangePropChanged));

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

        public string PeriodLabel { get; private set; } = "";
        public event EventHandler? RangeChanged;

        // ====== Label (z nazwami presetów) ======
        private void UpdateLabel()
        {
            string friendly =
                (Mode == DateRangeMode.Day && StartDate.Date == DateTime.Today) ? "Dzisiaj" :
                (Mode == DateRangeMode.Month && StartDate.Year == DateTime.Today.Year && StartDate.Month == DateTime.Today.Month) ? "Ten miesiąc" :
                (Mode == DateRangeMode.Quarter && StartDate.Year == DateTime.Today.Year && ((StartDate.Month - 1) / 3) == ((DateTime.Today.Month - 1) / 3)) ? "Ten kwartał" :
                (Mode == DateRangeMode.Year && StartDate.Year == DateTime.Today.Year) ? "Ten rok" : null;

            PeriodLabel = friendly ?? (Mode switch
            {
                DateRangeMode.Day => StartDate.ToString("dd.MM.yyyy"),
                DateRangeMode.Month => StartDate.ToString("MMM yyyy"),
                DateRangeMode.Quarter => $"Q{((StartDate.Month - 1) / 3) + 1} {StartDate:yyyy}",
                DateRangeMode.Year => StartDate.ToString("yyyy"),
                DateRangeMode.Custom => $"{StartDate:dd.MM.yyyy} — {EndDate:dd.MM.yyyy}",
                _ => $"{StartDate:dd.MM.yyyy} — {EndDate:dd.MM.yyyy}"
            });

            DataContext = null; DataContext = this;
        }

        private void SetMode(DateRangeMode mode, DateTime anchor)
        {
            Mode = mode;
            switch (mode)
            {
                case DateRangeMode.Day:
                    StartDate = EndDate = anchor.Date; break;
                case DateRangeMode.Month:
                    StartDate = new DateTime(anchor.Year, anchor.Month, 1);
                    EndDate = StartDate.AddMonths(1).AddDays(-1); break;
                case DateRangeMode.Quarter:
                    int qStart = (((anchor.Month - 1) / 3) * 3) + 1;
                    StartDate = new DateTime(anchor.Year, qStart, 1);
                    EndDate = StartDate.AddMonths(3).AddDays(-1); break;
                case DateRangeMode.Year:
                    StartDate = new DateTime(anchor.Year, 1, 1);
                    EndDate = new DateTime(anchor.Year, 12, 31); break;
                case DateRangeMode.Custom:
                    break;
            }
            UpdateLabel();
        }

        // Nawigacja ◀ / ▶
        private void Prev_Click(object s, RoutedEventArgs e)
        {
            var a = StartDate;
            switch (Mode)
            {
                case DateRangeMode.Custom:
                    StartDate = StartDate.AddDays(-1);
                    EndDate = EndDate.AddDays(-1);
                    break;
                case DateRangeMode.Day: SetMode(DateRangeMode.Day, a.AddDays(-1)); break;
                case DateRangeMode.Month: SetMode(DateRangeMode.Month, a.AddMonths(-1)); break;
                case DateRangeMode.Quarter: SetMode(DateRangeMode.Quarter, a.AddMonths(-3)); break;
                case DateRangeMode.Year: SetMode(DateRangeMode.Year, a.AddYears(-1)); break;
            }
            UpdateLabel();
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Next_Click(object s, RoutedEventArgs e)
        {
            var a = StartDate;
            switch (Mode)
            {
                case DateRangeMode.Custom:
                    StartDate = StartDate.AddDays(1);
                    EndDate = EndDate.AddDays(1);
                    break;
                case DateRangeMode.Day: SetMode(DateRangeMode.Day, a.AddDays(1)); break;
                case DateRangeMode.Month: SetMode(DateRangeMode.Month, a.AddMonths(1)); break;
                case DateRangeMode.Quarter: SetMode(DateRangeMode.Quarter, a.AddMonths(3)); break;
                case DateRangeMode.Year: SetMode(DateRangeMode.Year, a.AddYears(1)); break;
            }
            UpdateLabel();
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}





