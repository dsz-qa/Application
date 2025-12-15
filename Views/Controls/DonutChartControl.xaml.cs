using System;
using System.Collections.Generic;
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

        // zachowanie stanu
        private string? _selectedKey;   // klikniêty wycinek (zapamiêtany)
        private Path? _hoveredPath;      // aktualnie pod myszk¹

        // parametry „wysuwania”
        private const double ExplodeOffset = 8.0;  // o ile px wycinek "wyje¿d¿a"
        private const double HoverStrokeThickness = 1.5;

        // geometry cache for positioning
        private double _lastCx = 0, _lastCy = 0, _lastOuterRadius = 0, _lastInnerRadius = 0, _lastCanvasWidth = 0, _lastCanvasHeight = 0;

        // New event: hover (enter/leave) -> reports page will update side panel
        public event EventHandler<SliceHoverEventArgs>? SliceHovered;

        public event EventHandler<SliceClickedEventArgs>? SliceClicked;

        public DonutChartControl()
        {
            InitializeComponent();

            Loaded += (s, e) => Redraw();
            SizeChanged += (s, e) => Redraw();
        }

        /// <summary>
        /// Rysuje wykres donut na podstawie s³ownika:
        /// nazwa -> wartoœæ.
        /// total – ca³kowita suma (jeœli0 lub mniejsza, liczymy sumê z danych).
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
            // Clear only the slices
            ChartCanvas.Children.Clear();

            if (!IsLoaded)
                return;

            if (_data == null || _data.Count == 0)
            {
                CenterPanel.Visibility = Visibility.Collapsed;
                CenterOverflowPanel.Visibility = Visibility.Collapsed;
                NoDataText.Visibility = Visibility.Visible;
                return;
            }

            if (_total <= 0)
                _total = _data.Values.Sum();

            if (_total <= 0)
            {
                CenterPanel.Visibility = Visibility.Collapsed;
                CenterOverflowPanel.Visibility = Visibility.Collapsed;
                NoDataText.Visibility = Visibility.Visible;
                return;
            }

            NoDataText.Visibility = Visibility.Collapsed;
            CenterPanel.Visibility = Visibility.Visible;

            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
            {
                // If control hasn't been measured yet, schedule redraw later when layout is ready
                if (Dispatcher != null)
                {
                    Dispatcher.BeginInvoke(new Action(Redraw), System.Windows.Threading.DispatcherPriority.Loaded);
                    return;
                }

                width = ActualWidth > 0 ? ActualWidth : 200;
                height = ActualHeight > 0 ? ActualHeight : 200;
            }

            double cx = width / 2.0;
            double cy = height / 2.0;

            double outerRadius = Math.Min(width, height) / 2.0 - 10;
            if (outerRadius < 30) outerRadius = Math.Min(width, height) / 2.0 - 4;

            // dynamiczne dopasowanie innerRadius aby œrodek nie by³ za du¿y i nie nachodzi³ na wycinki
            double innerRadius = outerRadius * 0.58;
            if (Math.Min(width, height) < 180) // ma³y kontrol
                innerRadius = outerRadius * 0.48;

            // cache geometry
            _lastCx = cx; _lastCy = cy; _lastOuterRadius = outerRadius; _lastInnerRadius = innerRadius; _lastCanvasWidth = width; _lastCanvasHeight = height;

            // adjust CenterPanel size to be proportional to innerRadius
            double centerDiameter = Math.Max(48, innerRadius * 1.6);
            CenterPanel.Width = centerDiameter;
            CenterPanel.Height = centerDiameter;
            CenterPanel.CornerRadius = new CornerRadius(centerDiameter / 2.0);

            // draw slices
            double startAngle = -90.0; // od góry
            int index = 0;

            // Sortuj malej¹co, ¿eby najwiêksze wartoœci by³y rysowane spod spodu
            var ordered = _data.OrderByDescending(kv => kv.Value).ToList();

            foreach (var kv in ordered)
            {
                var value = kv.Value;
                if (value <= 0) continue;

                double sweepAngle = (double)(value / _total) * 360.0;
                if (sweepAngle <= 0) continue;

                // drobna separacja miêdzy wycinkami - zmniejszamy sweep o epsilon
                double epsilon = Math.Min(1.0, sweepAngle * 0.0025);
                double drawSweep = Math.Max(0.6, sweepAngle - epsilon);

                Brush fill;
                if (_brushes != null && _brushes.Length > 0)
                    fill = _brushes[index % _brushes.Length];
                else
                    fill = Brushes.SteelBlue;

                var path = CreateDonutSlice(cx, cy, innerRadius, outerRadius, startAngle, drawSweep);
                path.Fill = fill;
                // remove white stroke to avoid visible white seams
                path.Stroke = Brushes.Transparent;
                path.StrokeThickness = 0.6;
                path.StrokeLineJoin = PenLineJoin.Round;

                // meta do hover/klik
                path.Tag = new SliceMeta
                {
                    Name = kv.Key,
                    Value = kv.Value,
                    StartAngle = startAngle,
                    SweepAngle = drawSweep
                };

                path.Cursor = Cursors.Hand;
                path.MouseEnter += Slice_MouseEnter;
                path.MouseLeave += Slice_MouseLeave;
                path.MouseMove += Slice_MouseMove;
                path.MouseLeftButtonDown += Slice_MouseLeftButtonDown;

                ChartCanvas.Children.Add(path);

                startAngle += sweepAngle; // u¿ywaj oryginalnego sweep do pozycji startowej nastêpnego
                index++;
            }

            // center is empty minimal hole
            CenterStaticLabel.Visibility = Visibility.Collapsed;
            CenterTitleText.Visibility = Visibility.Collapsed;
            CenterValueText.Visibility = Visibility.Collapsed;
            CenterPercentText.Visibility = Visibility.Collapsed;
            CenterOverflowPanel.Visibility = Visibility.Collapsed;
        }

        private void Slice_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Path p) return;
            if (p.Tag is not SliceMeta meta) return;

            ResetHoverVisual();
            _hoveredPath = p;

            // wysuñ wycinek w kierunku œrodka k¹ta
            var midAngle = meta.StartAngle + (meta.SweepAngle / 2.0);
            var rad = midAngle * Math.PI / 180.0;

            var dx = ExplodeOffset * Math.Cos(rad);
            var dy = ExplodeOffset * Math.Sin(rad);

            p.RenderTransform = new TranslateTransform(dx, dy);

            // subtle highlight shadow
            p.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                Opacity = 0.18,
                ShadowDepth = 0
            };

            // Raise hover event with details
            var percent = _total > 0 ? (double)(meta.Value / _total) * 100.0 : 0.0;
            SliceHovered?.Invoke(this, new SliceHoverEventArgs(meta.Name, meta.Value, percent));
        }

        private void Slice_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not Path p) return;
            if (p.Tag is not SliceMeta meta) return;

            // Update hover event for side panel without flicker
            var percent = _total > 0 ? (double)(meta.Value / _total) * 100.0 : 0.0;
            SliceHovered?.Invoke(this, new SliceHoverEventArgs(meta.Name, meta.Value, percent));
        }

        private void Slice_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not Path p) return;

            // reset tylko jeœli to faktycznie hovered
            if (_hoveredPath == p)
            {
                ResetHoverVisual();

                // signal clear
                SliceHovered?.Invoke(this, new SliceHoverEventArgs(string.Empty, 0m, 0.0));
            }
        }

        private void ResetHoverVisual()
        {
            if (_hoveredPath == null) return;

            _hoveredPath.RenderTransform = Transform.Identity;
            _hoveredPath.Effect = null;

            _hoveredPath = null;
        }

        // ===== CLICK =====

        private void Slice_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Path p) return;
            if (p.Tag is not SliceMeta meta) return;

            _selectedKey = meta.Name;

            // fire event for external handling
            SliceClicked?.Invoke(this, new SliceClickedEventArgs(meta.Name, meta.Value));
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

    public class SliceHoverEventArgs : EventArgs
    {
        public string Name { get; }
        public decimal Value { get; }
        public double Percent { get; }

        public SliceHoverEventArgs(string name, decimal value, double percent)
        {
            Name = name;
            Value = value;
            Percent = percent;
        }
    }
}