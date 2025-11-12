using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Finly.Views;              // ShellWindow
using Finly.Views.Controls;     // ToastControl

namespace Finly.Services
{
    /// <summary>
    /// Serwis do wyświetlania toastów (Info / Success / Warning / Error).
    /// W ShellWindow musi być:
    /// <Canvas x:Name="ToastLayer" Grid.Row="1" HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>
    /// </summary>
    public static class ToastService
    {
        public enum ToastPosition { BottomCenter, TopRight }
        public static ToastPosition Position { get; set; } = ToastPosition.BottomCenter;

        public static void Show(string message, string type = "info")
            => RunOnUI(() =>
            {
                var shell = Application.Current.Windows.OfType<ShellWindow>().FirstOrDefault();
                if (shell is null) return;
                if (shell.FindName("ToastLayer") is not Canvas canvas) return;

                var toast = new ToastControl(message ?? string.Empty, type ?? "info");
                canvas.Children.Add(toast);
                toast.Loaded += (_, __) => LayoutToasts(canvas);
            });

        private static void LayoutToasts(Canvas canvas)
        {
            if (Position == ToastPosition.BottomCenter)
            {
                const double bottom = 24, spacing = 10;
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

        // Twoje „krótkie” metody
        public static void Info(string message) => Show(message, "info");
        public static void Success(string message) => Show(message, "success");
        public static void Warning(string message) => Show(message, "warning");
        public static void Error(string message) => Show(message, "error");

        // >>> Alias-y zgodne z wywołaniami w projekcie
        public static void ShowInfo(string message) => Info(message);
        public static void ShowSuccess(string message) => Success(message);
        public static void ShowWarning(string message) => Warning(message);
        public static void ShowError(string message) => Error(message);

        public static void Clear()
            => RunOnUI(() =>
            {
                var shell = Application.Current.Windows.OfType<ShellWindow>().FirstOrDefault();
                if (shell?.FindName("ToastLayer") is Canvas layer)
                    layer.Children.Clear();
            });

        private static void RunOnUI(System.Action action)
        {
            var app = Application.Current;
            if (app is null) return;
            if (app.Dispatcher.CheckAccess()) action();
            else app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }
    }
}

