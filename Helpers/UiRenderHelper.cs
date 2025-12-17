using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Finly.Helpers
{
    /// <summary>
    /// Helper do renderowania kontrolek WPF do PNG (pod eksport PDF/raporty).
    /// - RenderToPng: render 1:1 do wskazanego rozmiaru (bez skalowania).
    /// - RenderToPngFit: render do ramki z automatycznym skalowaniem (Uniform, DownOnly),
    ///   żeby nic nie było ucięte w raporcie.
    /// </summary>
    public static class UiRenderHelper
    {
        /// <summary>
        /// Renderuje element 1:1 do PNG o zadanym rozmiarze.
        /// Jeżeli element jest większy niż width/height, może zostać ucięty.
        /// </summary>
        public static byte[] RenderToPng(FrameworkElement element, int width, int height, int dpi = 192)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);

            // flatten na białym tle (ważne dla PDF)
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
            }

            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Renderuje element do PNG w zadanej ramce (width/height) z dopasowaniem skali,
        /// żeby CAŁOŚĆ się zmieściła (bez ucinania). Idealne do PDF.
        /// </summary>
        public static byte[] RenderToPngFit(FrameworkElement element, int width, int height, int dpi = 192)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            // dla eksportu najlepiej renderować element bez parenta (osobna instancja kontrolki)
            if (element.Parent != null)
                throw new InvalidOperationException("UiRenderHelper.RenderToPngFit: element ma Parent. Utwórz nową instancję kontrolki wyłącznie do eksportu.");

            // root = białe tło + viewbox (skaluje całość do ramki)
            var root = new Border
            {
                Width = width,
                Height = height,
                Background = Brushes.White,
                Child = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    Child = element
                }
            };

            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));
            }

            rtb.Render(dv);

            // odpinamy child, żeby nie trzymać niepotrzebnych referencji
            if (root.Child is Viewbox vb)
                vb.Child = null;
            root.Child = null;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}
