using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Finly.Views.Controls
{
    public partial class DonutChartControl : UserControl
    {
        private Dictionary<string, decimal>? _data;
        private decimal _total;
        private Brush[]? _brushes;

        private string? _selectedKey;
        private Path? _hoveredPath;

        private const double ExplodeOffset = 8.0;

        public event EventHandler<SliceHoverEventArgs>? SliceHovered;
        public event EventHandler<SliceClickedEventArgs>? SliceClicked;

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(DonutChartControl),
                new PropertyMetadata(string.Empty, (d, e) =>
                {
                    if (d is DonutChartControl c)
                        c.LegendHeader.Text = string.IsNullOrWhiteSpace(c.Title) ? "Legenda" : c.Title;
                }));

        public DonutChartControl()
        {
            InitializeComponent();
            Loaded += (_, __) => Redraw();
            SizeChanged += (_, __) => Redraw();
        }

        public void Draw(Dictionary<string, decimal> data, decimal total, Brush[] brushes)
        {
            _data = data ?? new Dictionary<string, decimal>();
            _total = total;
            _brushes = brushes;
            _selectedKey = null;
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
                LegendList.ItemsSource = Array.Empty<LegendItem>();
                return;
            }

            if (_total <= 0)
                _total = _data.Values.Sum();

            if (_total <= 0)
            {
                CenterPanel.Visibility = Visibility.Collapsed;
                NoDataText.Visibility = Visibility.Visible;
                LegendList.ItemsSource = Array.Empty<LegendItem>();
                return;
            }

            NoDataText.Visibility = Visibility.Collapsed;
            CenterPanel.Visibility = Visibility.Visible;

            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
            {
                Dispatcher?.BeginInvoke(new Action(Redraw), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            double cx = width / 2.0;
            double cy = height / 2.0;

            double outerRadius = Math.Min(width, height) / 2.0 - 10;
            if (outerRadius < 30) outerRadius = Math.Min(width, height) / 2.0 - 4;

            double innerRadius = outerRadius * 0.58;
            if (Math.Min(width, height) < 180)
                innerRadius = outerRadius * 0.48;

            // center size
            double centerDiameter = Math.Max(56, innerRadius * 1.6);
            CenterPanel.Width = centerDiameter;
            CenterPanel.Height = centerDiameter;
            CenterPanel.CornerRadius = new CornerRadius(centerDiameter / 2.0);

            // order slices
            var ordered = _data
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .ToList();

            // legend list
            var legend = new List<LegendItem>();
            int index = 0;

            double startAngle = -90.0;

            foreach (var kv in ordered)
            {
                double sweepAngle = (double)(kv.Value / _total) * 360.0;
                if (sweepAngle <= 0) continue;

                double epsilon = Math.Min(1.0, sweepAngle * 0.0025);
                double drawSweep = Math.Max(0.6, sweepAngle - epsilon);

                Brush fill = (_brushes != null && _brushes.Length > 0)
                    ? _brushes[index % _brushes.Length]
                    : Brushes.SteelBlue;

                var path = CreateDonutSlice(cx, cy, innerRadius, outerRadius, startAngle, drawSweep);
                path.Fill = fill;
                path.Stroke = Brushes.Transparent;
                path.StrokeThickness = 0.6;
                path.StrokeLineJoin = PenLineJoin.Round;

                var meta = new SliceMeta
                {
                    Name = kv.Key,
                    Value = kv.Value,
                    StartAngle = startAngle,
                    SweepAngle = drawSweep
                };
                path.Tag = meta;

                path.Cursor = Cursors.Hand;
                path.MouseEnter += Slice_MouseEnter;
                path.MouseLeave += Slice_MouseLeave;
                path.MouseMove += Slice_MouseMove;
                path.MouseLeftButtonDown += Slice_MouseLeftButtonDown;

                ChartCanvas.Children.Add(path);

                var percent = _total > 0 ? (double)(kv.Value / _total) * 100.0 : 0.0;
                legend.Add(new LegendItem
                {
                    Name = kv.Key,
                    Value = kv.Value,
                    Percent = percent,
                    Brush = fill,
                    IsSelected = false
                });

                startAngle += sweepAngle;
                index++;
            }

            LegendList.ItemsSource = legend;

            // center default
            ShowAllInCenter();
            UpdateLegendSelection();
        }

        private void ShowAllInCenter()
        {
            CenterTitleText.Text = "Wszystko";
            CenterValueText.Text = _total.ToString("N2", CultureInfo.CurrentCulture) + " z³";
            CenterPercentText.Text = "";
        }

        private void ShowSliceInCenter(string name, decimal value)
        {
            var percent = _total > 0 ? (double)(value / _total) * 100.0 : 0.0;

            CenterTitleText.Text = name;
            CenterValueText.Text = value.ToString("N2", CultureInfo.CurrentCulture) + " z³";
            CenterPercentText.Text = percent.ToString("N1", CultureInfo.CurrentCulture) + "% udzia³u";
        }

        private void UpdateLegendSelection()
        {
            if (LegendList.ItemsSource is not IEnumerable<LegendItem> items) return;

            foreach (var it in items)
                it.IsSelected = (!string.IsNullOrWhiteSpace(_selectedKey) && string.Equals(it.Name, _selectedKey, StringComparison.OrdinalIgnoreCase));

            // wymuœ odœwie¿enie (ItemsControl bez ObservableCollection)
            LegendList.ItemsSource = items.ToList();
        }

        private void Slice_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Path p) return;
            if (p.Tag is not SliceMeta meta) return;

            ResetHoverVisual();
            _hoveredPath = p;

            Explode(p, meta);

            // hover = podgl¹d w centrum (bez zmiany selectedKey)
            ShowSliceInCenter(meta.Name, meta.Value);

            var percent = _total > 0 ? (double)(meta.Value / _total) * 100.0 : 0.0;
            SliceHovered?.Invoke(this, new SliceHoverEventArgs(meta.Name, meta.Value, percent));
        }

        private void Slice_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not Path p) return;
            if (p.Tag is not SliceMeta meta) return;

            var percent = _total > 0 ? (double)(meta.Value / _total) * 100.0 : 0.0;
            SliceHovered?.Invoke(this, new SliceHoverEventArgs(meta.Name, meta.Value, percent));
        }

        private void Slice_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not Path p) return;

            if (_hoveredPath == p)
            {
                ResetHoverVisual();

                // po hover wróæ do klikniêtego, a jak nie ma – do „Wszystko”
                if (!string.IsNullOrWhiteSpace(_selectedKey) && _data != null && _data.TryGetValue(_selectedKey, out var val))
                    ShowSliceInCenter(_selectedKey, val);
                else
                    ShowAllInCenter();

                SliceHovered?.Invoke(this, new SliceHoverEventArgs(string.Empty, 0m, 0.0));
            }
        }

        private void Slice_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Path p) return;
            if (p.Tag is not SliceMeta meta) return;

            _selectedKey = meta.Name;

            ShowSliceInCenter(meta.Name, meta.Value);
            UpdateLegendSelection();

            SliceClicked?.Invoke(this, new SliceClickedEventArgs(meta.Name, meta.Value));
        }

        private void Explode(Path p, SliceMeta meta)
        {
            var midAngle = meta.StartAngle + (meta.SweepAngle / 2.0);
            var rad = midAngle * Math.PI / 180.0;

            var dx = ExplodeOffset * Math.Cos(rad);
            var dy = ExplodeOffset * Math.Sin(rad);

            p.RenderTransform = new TranslateTransform(dx, dy);
            p.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                Opacity = 0.18,
                ShadowDepth = 0
            };
        }

        private void ResetHoverVisual()
        {
            if (_hoveredPath == null) return;
            _hoveredPath.RenderTransform = Transform.Identity;
            _hoveredPath.Effect = null;
            _hoveredPath = null;
        }

        private static Path CreateDonutSlice(double cx, double cy, double innerR, double outerR, double startAngle, double sweepAngle)
        {
            double startRad = startAngle * Math.PI / 180.0;
            double endRad = (startAngle + sweepAngle) * Math.PI / 180.0;

            var p1 = new Point(cx + outerR * Math.Cos(startRad), cy + outerR * Math.Sin(startRad));
            var p2 = new Point(cx + outerR * Math.Cos(endRad), cy + outerR * Math.Sin(endRad));

            var p3 = new Point(cx + innerR * Math.Cos(endRad), cy + innerR * Math.Sin(endRad));
            var p4 = new Point(cx + innerR * Math.Cos(startRad), cy + innerR * Math.Sin(startRad));

            bool largeArc = sweepAngle > 180.0;

            var figure = new PathFigure { StartPoint = p1, IsClosed = true };

            figure.Segments.Add(new ArcSegment
            {
                Point = p2,
                Size = new Size(outerR, outerR),
                IsLargeArc = largeArc,
                SweepDirection = SweepDirection.Clockwise
            });

            figure.Segments.Add(new LineSegment { Point = p3 });

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

        private sealed class LegendItem
        {
            public string Name { get; set; } = "";
            public decimal Value { get; set; }
            public double Percent { get; set; }
            public Brush Brush { get; set; } = Brushes.Gray;

            public bool IsSelected { get; set; }

            public string AmountStr => Value.ToString("N2", CultureInfo.CurrentCulture) + " z³";
            public string PercentStr => Percent.ToString("N1", CultureInfo.CurrentCulture) + "%";
        }
    }

    public class SliceClickedEventArgs : EventArgs
    {
        public string Name { get; }
        public decimal Value { get; }
        public SliceClickedEventArgs(string name, decimal value) { Name = name; Value = value; }
    }

    public class SliceHoverEventArgs : EventArgs
    {
        public string Name { get; }
        public decimal Value { get; }
        public double Percent { get; }
        public SliceHoverEventArgs(string name, decimal value, double percent) { Name = name; Value = value; Percent = percent; }
    }
}
