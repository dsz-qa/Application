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
 public DonutChartControl()
 {
 InitializeComponent();
 }

 public event EventHandler<SliceClickedEventArgs>? SliceClicked;

 public void Draw(Dictionary<string, decimal> totals, decimal totalAll, Brush[] brushes)
 {
 ChartCanvas.Children.Clear();
 NoDataText.Visibility = Visibility.Collapsed;

 if (totalAll <=0 || totals == null || totals.Values.Sum() <=0)
 {
 NoDataText.Visibility = Visibility.Visible;
 return;
 }

 double width = ChartCanvas.ActualWidth;
 double height = ChartCanvas.ActualHeight;
 if (width <=0 || height <=0)
 {
 width =220;
 height =220;
 }

 double size = Math.Min(width, height) *0.9;
 double cx = width /2.0;
 double cy = height /2.0;
 double r = size /2.0;
 double startAngle = -90.0;

 int idx =0;
 foreach (var kv in totals)
 {
 var value = (double)kv.Value;
 if (value <=0) { idx++; continue; }
 double sweep = value / (double)totalAll *360.0;

 var path = CreatePieSlice(cx, cy, r, startAngle, sweep, brushes[idx % brushes.Length]);
 path.ToolTip = $"{kv.Key}\n{kv.Value:N2} z³ • {(sweep /360.0 *100.0):N1}%";
 var name = kv.Key;
 var amount = kv.Value;
 path.MouseLeftButtonDown += (s, e) => SliceClicked?.Invoke(this, new SliceClickedEventArgs(name, amount));
 ChartCanvas.Children.Add(path);

 startAngle += sweep;
 idx++;
 }
 }

 private static Path CreatePieSlice(double cx, double cy, double r, double startAngleDeg, double sweepAngleDeg, Brush fill)
 {
 double startRad = startAngleDeg * Math.PI /180.0;
 double endRad = (startAngleDeg + sweepAngleDeg) * Math.PI /180.0;

 Point p0 = new Point(cx, cy);
 Point p1 = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
 Point p2 = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

 bool isLarge = Math.Abs(sweepAngleDeg) >180.0;

 var pf = new PathFigure { StartPoint = p0, IsClosed = true, IsFilled = true };
 pf.Segments.Add(new LineSegment(p1, true));
 pf.Segments.Add(new ArcSegment
 {
 Point = p2,
 Size = new Size(r, r),
 SweepDirection = SweepDirection.Clockwise,
 IsLargeArc = isLarge,
 RotationAngle =0
 });

 var pg = new PathGeometry();
 pg.Figures.Add(pf);

 var path = new Path
 {
 Data = pg,
 Fill = fill,
 Stroke = Brushes.White,
 StrokeThickness =1
 };

 return path;
 }
 }

 public class SliceClickedEventArgs : EventArgs
 {
 public string Name { get; }
 public decimal Amount { get; }
 public SliceClickedEventArgs(string name, decimal amount)
 {
 Name = name;
 Amount = amount;
 }
 }
}
