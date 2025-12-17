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

            LegendItems.Clear();

            // wymuś layout zanim rysujemy wykres
            UpdateLayout();

            // ===== DONUT =====
            Chart.Draw(
                vm.ChartTotals ?? new(),
                vm.ChartTotalAll,
                brushes);

            // ===== LEGENDA =====
            var details = vm.Details?
                .OrderByDescending(x => x.Amount)
                .ToList()
                ?? new();

            var take = details.Take(maxItems).ToList();

            for (int i = 0; i < take.Count; i++)
            {
                var d = take[i];
                var brush = (brushes != null && brushes.Length > 0)
                    ? brushes[i % brushes.Length]
                    : Brushes.DimGray;

                LegendItems.Add(new LegendItemVm
                {
                    Name = d.Name,
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
            else
            {
                OthersInfo = string.Empty;
                OthersInfoVisibility = Visibility.Collapsed;
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
