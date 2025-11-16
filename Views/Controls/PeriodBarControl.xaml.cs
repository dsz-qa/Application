using System;
using System.Windows;
using System.Windows.Controls;
using Finly.Models;

namespace Finly.Views.Controls
{
    public partial class PeriodBarControl : UserControl
    {
        // Kolejność presetów na strzałkach
        private static readonly DateRangeMode[] PresetOrder =
        {
            DateRangeMode.Day,
            DateRangeMode.Week,
            DateRangeMode.Month,
            DateRangeMode.Quarter,
            DateRangeMode.Year
        };

        public PeriodBarControl()
        {
            InitializeComponent();

            // DOMYŚLNIE: TEN MIESIĄC
            ApplyPreset(DateRangeMode.Month, DateTime.Today);
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
                    DateRangeMode.Month,   // spójne z konstruktorem
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
                    int diff = ((int)anchor.DayOfWeek + 6) % 7; // pon=0
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
                    // ustawiane ręcznie przyciskiem "Szukaj"
                    break;
            }

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
        /// Użyj np. po kliknięciu przycisku "Wyczyść": SetPreset(DateRangeMode.Day).
        /// </summary>
        public void SetPreset(DateRangeMode mode) => ApplyPreset(mode, DateTime.Today);

        /// <summary>
        /// Ustawia własny zakres – używane po kliknięciu "Szukaj".
        /// Wtedy na pasku pojawia się tekst 01.01.2025 – 31.01.2025 itp.
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
    }
}










