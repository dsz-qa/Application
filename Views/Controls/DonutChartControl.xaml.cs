using System;
using System.Collections.Generic;
using System.Linq;
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

        // zachowanie stanu
        private string? _selectedKey;   // klikniêty wycinek (zapamiêtany)
        private Path? _hoveredPath;      // aktualnie pod myszk¹

        // parametry „wysuwania”
        private const double ExplodeOffset = 10.0;  // o ile px wycinek „wyje¿d¿a”
        private const double HoverStrokeThickness = 1.5;

        public DonutChartControl()
        {
            InitializeComponent();

            Loaded += (_, __) => Redraw();
            SizeChanged += (_, __) => Redraw();
        }

        /// <summary>
        /// Rysuje wykres donut na podstawie s³ownika:
        /// nazwa -> wartoœæ.
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
                _total = _data.Values.Sum();

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
            double innerRadius = outerRadius * 0.62;

            double startAngle = -90.0; // od góry
            int index = 0;

            foreach (var kv in _data)
            {
                var value = kv.Value;
                if (value <= 0) continue;

                double sweepAngle = (double)(value / _total) * 360.0;
                if (sweepAngle <= 0) continue;

                Brush fill;
                if (_brushes != null && _brushes.Length > 0)
                    fill = _brushes[index % _brushes.Length];
                else
                    fill = Brushes.SteelBlue;

                var path = CreateDonutSlice(cx, cy, innerRadius, outerRadius, startAngle, sweepAngle);
                path.Fill = fill;
                path.Stroke = Brushes.Transparent;
                path.StrokeThickness = 0;

                // meta do hover/klik
                path.Tag = new SliceMeta
                {
                    Name = kv.Key,
                    Value = kv.Value,
                    StartAngle = startAngle,
                    SweepAngle = sweepAngle
                };

                path.Cursor = Cursors.Hand;
                path.MouseEnter += Slice_MouseEnter;
                path.MouseLeave += Slice_MouseLeave;
                path.MouseLeftButtonDown += Slice_MouseLeftButtonDown;

                ChartCanvas.Children.Add(path);

                // jeœli to wybrany wycinek - poka¿ go w œrodku po redraw
                if (!string.IsNullOrWhiteSpace(_selectedKey) && kv.Key == _selectedKey)
                {
                    SetCenterForSlice(kv.Key, kv.Value);
                }

                startAngle += sweepAngle;
                index++;
            }

            // jeœli nic nie jest wybrane, w œrodku suma
            if (string.IsNullOrWhiteSpace(_selectedKey))
                SetCenterToTotal();
        }

        private void Slice_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Path p) return;
            if (p.Tag is not SliceMeta meta) return;

            // zdejmij hover z poprzedniego
            ResetHoverVisual();

            _hoveredPath = p;

            // ustaw info w œrodku na hover
            SetCenterForSlice(meta.Name, meta.Value);

            // „wysuñ” wycinek w kierunku œrodka k¹ta
            var midAngle = meta.StartAngle + (meta.SweepAngle / 2.0);
            var rad = midAngle * Math.PI / 180.0;

            var dx = ExplodeOffset * Math.Cos(rad);
            var dy = ExplodeOffset * Math.Sin(rad);

            p.RenderTransform = new TranslateTransform(dx, dy);

            // delikatny outline
            p.Stroke = new SolidColorBrush(Color.FromArgb(130, 255, 255, 255));
            p.StrokeThickness = HoverStrokeThickness;
        }

        private void Slice_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not Path p) return;

            // reset tylko jeœli to faktycznie hovered
            if (_hoveredPath == p)
            {
                ResetHoverVisual();

                // jeœli jest wybór (klik), wróæ do wybranego; jeœli nie, wróæ do sumy
                if (!string.IsNullOrWhiteSpace(_selectedKey) && _data != null && _data.TryGetValue(_selectedKey, out var v))
                    SetCenterForSlice(_selectedKey, v);
                else
                    SetCenterToTotal();
            }
        }

        private void ResetHoverVisual()
        {
            if (_hoveredPath == null) return;

            _hoveredPath.RenderTransform = Transform.Identity;
            _hoveredPath.Stroke = Brushes.Transparent;
            _hoveredPath.StrokeThickness = 0;

            _hoveredPath = null;
        }

        // ===== CLICK =====

        public event EventHandler<SliceClickedEventArgs>? SliceClicked;

        private void Slice_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Path p) return;
            if (p.Tag is not SliceMeta meta) return;

            _selectedKey = meta.Name;

            // ustaw info w œrodku na klik
            SetCenterForSlice(meta.Name, meta.Value);

            // powiadom zewnêtrzny kod
            SliceClicked?.Invoke(this, new SliceClickedEventArgs(meta.Name, meta.Value));
        }

        // ===== CENTER TEXT =====

        private void SetCenterToTotal()
        {
            CenterTitleText.Text = "Suma";
            CenterValueText.Text = $"{_total:N2} z³";
            CenterPercentText.Text = "";
        }

        private void SetCenterForSlice(string name, decimal value)
        {
            CenterTitleText.Text = name;
            CenterValueText.Text = $"{value:N2} z³";

            var percent = _total > 0 ? (double)(value / _total) * 100.0 : 0.0;
            CenterPercentText.Text = $"{percent:N0}%";
        }

        // ===== GEOMETRY =====

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

        private sealed class SliceMeta
        {
            public string Name { get; set; } = "";
            public decimal Value { get; set; }
            public double StartAngle { get; set; }
            public double SweepAngle { get; set; }
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
