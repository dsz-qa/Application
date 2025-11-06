using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Finly.Views;              // ShellWindow
using Finly.Views.Controls;     // ToastControl

namespace Finly.Services
{
    /// <summary>
    /// Prosty serwis do wyświetlania toastów (Info / Success / Warning / Error).
    /// Wymaga w ShellWindow elementu:
    ///   <Canvas x:Name="ToastLayer" Grid.Row="1"
    ///           HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>
    /// </summary>
    public static class ToastService
    {
        public enum ToastPosition { BottomCenter, TopRight }

        /// <summary>Pozycja wyświetlania tostów.</summary>
        public static ToastPosition Position { get; set; } = ToastPosition.BottomCenter;

        /// <summary>
        /// Główna funkcja – dodaje ToastControl i układa wszystkie tosty
        /// zgodnie z wybraną pozycją.
        /// </summary>
        public static void Show(string message, string type = "info")
            => RunOnUI(() =>
            {
                var shell = Application.Current.Windows.OfType<ShellWindow>().FirstOrDefault();
                if (shell is null) return;

                if (shell.FindName("ToastLayer") is not Canvas canvas) return;

                var toast = new ToastControl(message ?? string.Empty, type ?? "info");
                canvas.Children.Add(toast);

                // Po dołączeniu kontrolki – ułóż wszystkie tosty na Canvasie
                toast.Loaded += (_, __) => LayoutToasts(canvas);
            });

        /// <summary>Układanie wszystkich tostów na danym Canvasie.</summary>
        private static void LayoutToasts(Canvas canvas)
        {
            if (canvas == null) return;

            if (Position == ToastPosition.BottomCenter)
            {
                const double bottom = 24;
                const double spacing = 10;

                // Policz pozycje od dołu do góry, centrowane w poziomie
                double curBottom = bottom;

                foreach (FrameworkElement child in canvas.Children.OfType<FrameworkElement>())
                {
                    child.UpdateLayout();
                    var left = (canvas.ActualWidth - child.ActualWidth) / 2.0;
                    var top = canvas.ActualHeight - curBottom - child.ActualHeight;

                    Canvas.SetLeft(child, left);
                    Canvas.SetTop(child, top);

                    curBottom += child.ActualHeight + spacing;
                }
            }
            else // TopRight
            {
                const double top = 20, right = 20, spacing = 10;
                double y = top;

                foreach (FrameworkElement child in canvas.Children.OfType<FrameworkElement>())
                {
                    child.UpdateLayout();
                    Canvas.SetTop(child, y);
                    Canvas.SetRight(child, right);
                    y += child.ActualHeight + spacing;
                }
            }
        }

        /// <summary>Wygodne skróty.</summary>
        public static void Info(string message) => Show(message, "info");
        public static void Success(string message) => Show(message, "success");
        public static void Warning(string message) => Show(message, "warning");
        public static void Error(string message) => Show(message, "error");

        /// <summary>Czyści wszystkie aktualnie wyświetlane tosty.</summary>
        public static void Clear()
            => RunOnUI(() =>
            {
                var shell = Application.Current.Windows.OfType<ShellWindow>().FirstOrDefault();
                if (shell?.FindName("ToastLayer") is Canvas layer)
                    layer.Children.Clear();
            });

        /// <summary>Zapewnia wykonanie akcji na wątku UI.</summary>
        private static void RunOnUI(System.Action action)
        {
            var app = Application.Current;
            if (app is null) return;

            if (app.Dispatcher.CheckAccess())
                action();
            else
                app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }
    }
}
