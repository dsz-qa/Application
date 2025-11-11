using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Finly.Models;

namespace Finly.Views.Controls
{
    public partial class PeriodBarControl : UserControl
    {
        public PeriodBarControl()
        {
            InitializeComponent();
            // start: Dzisiaj
            SetMode(DateRangeMode.Day, DateTime.Today);
        }

        // ===== DependencyProperties =====
        public static readonly DependencyProperty StartDateProperty =
            DependencyProperty.Register(nameof(StartDate), typeof(DateTime), typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangeChanged));

        public static readonly DependencyProperty EndDateProperty =
            DependencyProperty.Register(nameof(EndDate), typeof(DateTime), typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangeChanged));

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode), typeof(DateRangeMode), typeof(PeriodBarControl),
                new FrameworkPropertyMetadata(DateRangeMode.Day, OnRangeChanged));

        // ReadOnly DP – żeby binding Text na XAML reagował bez sztuczek z DataContext
        private static readonly DependencyPropertyKey PeriodLabelPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(PeriodLabel), typeof(string), typeof(PeriodBarControl),
                new PropertyMetadata(""));

        public static readonly DependencyProperty PeriodLabelProperty = PeriodLabelPropertyKey.DependencyProperty;

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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
            private set => SetValue(PeriodLabelPropertyKey, value);
        }

        public event EventHandler? RangeChanged;

        // ===== Helpers =====
        private static DateTime StartOfWeek(DateTime day, DayOfWeek firstDay)
        {
            int diff = (7 + (int)day.DayOfWeek - (int)firstDay) % 7;
            return day.AddDays(-diff).Date;
        }

        private void SetMode(DateRangeMode mode, DateTime anchor)
        {
            Mode = mode;

            switch (mode)
            {
                case DateRangeMode.Day:
                    StartDate = EndDate = anchor.Date;
                    break;

                case DateRangeMode.Week:
                    var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek; // w PL: Monday
                    StartDate = StartOfWeek(anchor, first);
                    EndDate = StartDate.AddDays(6);
                    break;

                case DateRangeMode.Month:
                    StartDate = new DateTime(anchor.Year, anchor.Month, 1);
                    EndDate = StartDate.AddMonths(1).AddDays(-1);
                    break;

                case DateRangeMode.Quarter:
                    int qStart = (((anchor.Month - 1) / 3) * 3) + 1;
                    StartDate = new DateTime(anchor.Year, qStart, 1);
                    EndDate = StartDate.AddMonths(3).AddDays(-1);
                    break;

                case DateRangeMode.Year:
                    StartDate = new DateTime(anchor.Year, 1, 1);
                    EndDate = new DateTime(anchor.Year, 12, 31);
                    break;

                case DateRangeMode.Custom:
                    // pozostaw zakres taki jak jest
                    break;
            }

            UpdateLabel();
        }

        private void UpdateLabel()
        {
            var today = DateTime.Today;
            string? friendly = Mode switch
            {
                DateRangeMode.Day when StartDate.Date == today => "Dzisiaj",
                DateRangeMode.Week when StartDate == StartOfWeek(today, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek) => "Ten tydzień",
                DateRangeMode.Month when StartDate.Year == today.Year && StartDate.Month == today.Month => "Ten miesiąc",
                DateRangeMode.Quarter when StartDate.Year == today.Year && ((StartDate.Month - 1) / 3) == ((today.Month - 1) / 3) => "Ten kwartał",
                DateRangeMode.Year when StartDate.Year == today.Year => "Ten rok",
                _ => null
            };

            PeriodLabel = friendly ?? Mode switch
            {
                DateRangeMode.Day => StartDate.ToString("dd.MM.yyyy"),
                DateRangeMode.Week => $"{StartDate:dd.MM} – {EndDate:dd.MM.yyyy}",
                DateRangeMode.Month => StartDate.ToString("MMM yyyy"),
                DateRangeMode.Quarter => $"Q{((StartDate.Month - 1) / 3) + 1} {StartDate:yyyy}",
                DateRangeMode.Year => StartDate.ToString("yyyy"),
                DateRangeMode.Custom => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}",
                _ => $"{StartDate:dd.MM.yyyy} – {EndDate:dd.MM.yyyy}"
            };
        }

        // ===== Strzałki – zmiana TRYBU (nie przesuwanie dat) =====
        private static readonly DateRangeMode[] ModeOrder =
            { DateRangeMode.Day, DateRangeMode.Week, DateRangeMode.Month, DateRangeMode.Quarter, DateRangeMode.Year };

        private void Prev_Click(object s, RoutedEventArgs e)
        {
            int i = Array.IndexOf(ModeOrder, Mode);
            if (i < 0) i = 0;
            i = (i - 1 + ModeOrder.Length) % ModeOrder.Length;
            SetMode(ModeOrder[i], DateTime.Today);
        }

        private void Next_Click(object s, RoutedEventArgs e)
        {
            int i = Array.IndexOf(ModeOrder, Mode);
            if (i < 0) i = 0;
            i = (i + 1) % ModeOrder.Length;
            SetMode(ModeOrder[i], DateTime.Today);
        }
    }
}






