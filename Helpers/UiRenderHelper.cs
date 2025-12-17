using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Finly.Helpers
{
    public static class UiRenderHelper
    {
        public static byte[] RenderToPng(FrameworkElement element, int pixelWidth, int pixelHeight, double dpi = 192)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            element.Measure(new Size(pixelWidth, pixelHeight));
            element.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));
            element.UpdateLayout();

            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}
