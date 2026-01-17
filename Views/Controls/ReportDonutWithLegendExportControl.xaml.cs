using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Finly.ViewModels;

namespace Finly.Views.Controls
{
    public partial class ReportDonutWithLegendExportControl : UserControl
    {
        public ObservableCollection<LegendItemVm> LegendItems { get; } = new();

        public string OthersInfo
        {
            get => (string)GetValue(OthersInfoProperty);
            set => SetValue(OthersInfoProperty, value);
        }

        public static readonly DependencyProperty OthersInfoProperty =
            DependencyProperty.Register(
                nameof(OthersInfo),
                typeof(string),
                typeof(ReportDonutWithLegendExportControl),
                new PropertyMetadata(string.Empty));

        public Visibility OthersInfoVisibility
        {
            get => (Visibility)GetValue(OthersInfoVisibilityProperty);
            set => SetValue(OthersInfoVisibilityProperty, value);
        }

        public static readonly DependencyProperty OthersInfoVisibilityProperty =
            DependencyProperty.Register(
                nameof(OthersInfoVisibility),
                typeof(Visibility),
                typeof(ReportDonutWithLegendExportControl),
                new PropertyMetadata(Visibility.Collapsed));

        public ReportDonutWithLegendExportControl()
        {
            InitializeComponent();

            // Kluczowe: legendy i DP pochodzą z tej kontrolki (nie z ReportsViewModel)
            DataContext = this;

            // ===== JASNE ZASOBY TYLKO DLA EKSPORTU =====
            Resources["App.Foreground"] = Brushes.Black;
            Resources["Surface.Background"] = Brushes.White;
            Resources["Surface.Border"] = new SolidColorBrush(Color.FromRgb(220, 220, 220));

            Background = Brushes.White;
        }

        public void Build(ReportsViewModel vm, Brush[] brushes, int maxItems = 9)
        {
            if (vm == null)
                return;

            if (maxItems < 1) maxItems = 1;

            LegendItems.Clear();
            OthersInfo = string.Empty;
            OthersInfoVisibility = Visibility.Collapsed;

            // ===== DONUT =====
            var totals = vm.ChartTotals ?? new Dictionary<string, decimal>();
            var totalAll = vm.ChartTotalAll;

            // Jeżeli totalAll nie jest policzone, policz z totals (bez ryzyka dzielenia przez 0)
            if (totalAll <= 0 && totals.Count > 0)
                totalAll = totals.Values.Sum();

            Chart.Draw(totals, totalAll, brushes);

            // ===== LEGENDA =====
            // 1) Preferuj CategoryBreakdown (Name/Amount/SharePercent już przygotowane w VM)
            var details = (vm.CategoryBreakdown ?? new ObservableCollection<ReportsViewModel.CategoryAmount>())
                .OrderByDescending(x => x.Amount)
                .ToList();

            // 2) Fallback na ChartTotals, jeżeli CategoryBreakdown puste
            if (details.Count == 0 && totals.Count > 0)
            {
                var denom = totalAll <= 0 ? totals.Values.Sum() : totalAll;

                details = totals
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => new ReportsViewModel.CategoryAmount
                    {
                        Name = kv.Key,
                        Amount = kv.Value,
                        SharePercent = denom > 0 ? (double)(kv.Value / denom * 100m) : 0.0
                    })
                    .ToList();
            }

            if (details.Count == 0)
                return;

            var take = details.Take(maxItems).ToList();

            for (int i = 0; i < take.Count; i++)
            {
                var d = take[i];

                var brush = (brushes != null && brushes.Length > 0)
                    ? brushes[i % brushes.Length]
                    : Brushes.DimGray;

                LegendItems.Add(new LegendItemVm
                {
                    Name = d.Name ?? string.Empty,
                    AmountStr = d.Amount.ToString("N2", CultureInfo.CurrentCulture) + " zł",
                    PercentStr = d.SharePercent.ToString("N1", CultureInfo.CurrentCulture) + " %",
                    BulletBrush = brush
                });
            }

            var rest = details.Count - take.Count;
            if (rest > 0)
            {
                OthersInfo = $"Pozostałe kategorie: {rest}";
                OthersInfoVisibility = Visibility.Visible;
            }
        }

        public sealed class LegendItemVm
        {
            public string Name { get; set; } = string.Empty;
            public string AmountStr { get; set; } = string.Empty;
            public string PercentStr { get; set; } = string.Empty;
            public Brush BulletBrush { get; set; } = Brushes.DimGray;
        }
    }
}
