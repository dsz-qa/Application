using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Finly.Views.Controls
{
    public partial class DonutChartControl : UserControl
    {
        private Dictionary<string, decimal>? _data;
        private decimal _total;
        private Brush[]? _brushes;

        public DonutChartControl()
        {
            InitializeComponent();

            Loaded += (_, __) => Redraw();
            SizeChanged += (_, __) => Redraw();
        }

        /// <summary>
        /// Rysuje wykres donut na podstawie s³ownika:
        /// nazwa kategorii -> wartoœæ.
        /// total – ca³kowita suma (jeœli 0 lub mniejsza, liczymy sumê z danych).
        /// </summary>
        public void Draw(Dictionary<string, decimal> data, decimal total, Brush[] brushes)
        {
            _data = data;
            _total = total;
            _brushes = brushes;
            Redraw();
        }

        private void Redraw()
        {
            ChartCanvas.Children.Clear();

            if (!IsLoaded)
                return;

            if (_data == null || _data.Count == 0)
            {
                CenterPanel.Visibility = Visibility.Collapsed;
                NoDataText.Visibility = Visibility.Visible;
                return;
            }

            if (_total <= 0)
                _total = 0m;

            if (_total <= 0)
            {
                foreach (var v in _data.Values)
                    _total += v;
            }

            if (_total <= 0)
            {
                CenterPanel.Visibility = Visibility.Collapsed;
                NoDataText.Visibility = Visibility.Visible;
                return;
            }

            NoDataText.Visibility = Visibility.Collapsed;
            CenterPanel.Visibility = Visibility.Visible;

            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
            {
                width = ActualWidth > 0 ? ActualWidth : 200;
                height = ActualHeight > 0 ? ActualHeight : 200;
            }

            double cx = width / 2.0;
            double cy = height / 2.0;
            double outerRadius = Math.Min(width, height) / 2.0 - 10;
            double innerRadius = outerRadius * 0.6;

            double startAngle = -90.0; // od góry
            int index = 0;

            foreach (var kv in _data)
            {
                var value = kv.Value;
                if (value <= 0) continue;

                double sweepAngle = (double)(value / _total) * 360.0;
                if (sweepAngle <= 0) continue;

                var path = CreateDonutSlice(cx, cy, innerRadius, outerRadius, startAngle, sweepAngle);

                Brush fill;
                if (_brushes != null && _brushes.Length > 0)
                    fill = _brushes[index % _brushes.Length];
                else
                    fill = Brushes.SteelBlue;

                path.Fill = fill;
                path.Stroke = Brushes.Transparent;
                path.Tag = kv; // przechowujemy parê (nazwa, wartoœæ)
                path.Cursor = Cursors.Hand;
                path.MouseLeftButtonDown += Slice_MouseLeftButtonDown;

                ChartCanvas.Children.Add(path);

                startAngle += sweepAngle;
                index++;
            }

            // Domyœlnie w œrodku pokazujemy sumê
            CenterTitleText.Text = "Suma";
            CenterValueText.Text = $"{_total:N2} z³";
        }

        private static Path CreateDonutSlice(
            double cx, double cy,
            double innerR, double outerR,
            double startAngle, double sweepAngle)
        {
            double startRad = startAngle * Math.PI / 180.0;
            double endRad = (startAngle + sweepAngle) * Math.PI / 180.0;

            var p1 = new Point(cx + outerR * Math.Cos(startRad), cy + outerR * Math.Sin(startRad));
            var p2 = new Point(cx + outerR * Math.Cos(endRad), cy + outerR * Math.Sin(endRad));

            var p3 = new Point(cx + innerR * Math.Cos(endRad), cy + innerR * Math.Sin(endRad));
            var p4 = new Point(cx + innerR * Math.Cos(startRad), cy + innerR * Math.Sin(startRad));

            bool largeArc = sweepAngle > 180.0;

            var figure = new PathFigure { StartPoint = p1, IsClosed = true };

            // zewnêtrzny ³uk
            figure.Segments.Add(new ArcSegment
            {
                Point = p2,
                Size = new Size(outerR, outerR),
                IsLargeArc = largeArc,
                SweepDirection = SweepDirection.Clockwise
            });

            // linia do wewnêtrznego okrêgu
            figure.Segments.Add(new LineSegment { Point = p3 });

            // wewnêtrzny ³uk (w drug¹ stronê)
            figure.Segments.Add(new ArcSegment
            {
                Point = p4,
                Size = new Size(innerR, innerR),
                IsLargeArc = largeArc,
                SweepDirection = SweepDirection.Counterclockwise
            });

            figure.Segments.Add(new LineSegment { Point = p1 });

            var geom = new PathGeometry();
            geom.Figures.Add(figure);

            return new Path { Data = geom };
        }

        // ===== EVENTY =====

        public event EventHandler<SliceClickedEventArgs>? SliceClicked;

        private void Slice_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Path p && p.Tag is KeyValuePair<string, decimal> kv)
            {
                // aktualizacja œrodka donuta
                CenterTitleText.Text = kv.Key;
                CenterValueText.Text = $"{kv.Value:N2} z³";

                // powiadom zewnêtrzny kod (ReportsPage)
                SliceClicked?.Invoke(this, new SliceClickedEventArgs(kv.Key, kv.Value));
            }
        }
    }

    public class SliceClickedEventArgs : EventArgs
    {
        public string Name { get; }
        public decimal Value { get; }

        public SliceClickedEventArgs(string name, decimal value)
        {
            Name = name;
            Value = value;
        }
    }
}
