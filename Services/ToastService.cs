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
    /// <Canvas x:Name="ToastLayer" Grid.Row="1"
    ///         HorizontalAlignment="Stretch"
    ///         VerticalAlignment="Stretch"/>
    /// </summary>
    public static class ToastService
    {
        // ===== Pozycja toasta =====
        public enum ToastPosition
        {
            BottomCenter = 0,
            TopRight = 1,
            TopLeft = 2,
            BottomRight = 3,
            BottomLeft = 4,

            // aliasy – jeśli gdzieś w kodzie użyłaś z podkreśleniami
            _TopLeft = TopLeft,
            _BottomRight = BottomRight,
            _BottomLeft = BottomLeft
        }

        public static ToastPosition Position { get; set; } = ToastPosition.BottomCenter;

        // ===== Główna metoda =====
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

        // ===== Rozmieszczanie toastów =====
        private static void LayoutToasts(Canvas canvas)
        {
            const double marginX = 24;   // odstęp od krawędzi
            const double marginY = 24;
            const double spacing = 10;   // odstęp między toastami

            switch (Position)
            {
                // DÓŁ – środek
                case ToastPosition.BottomCenter:
                    {
                        double curBottom = marginY;
                        foreach (FrameworkElement child in canvas.Children.OfType<FrameworkElement>())
                        {
                            child.UpdateLayout();

                            double left = (canvas.ActualWidth - child.ActualWidth) / 2.0;
                            double top = canvas.ActualHeight - curBottom - child.ActualHeight;

                            Canvas.SetLeft(child, left);
                            Canvas.SetTop(child, top);

                            curBottom += child.ActualHeight + spacing;
                        }
                        break;
                    }

                // GÓRA – prawy róg
                case ToastPosition.TopRight:
                    {
                        double y = marginY;
                        foreach (FrameworkElement child in canvas.Children.OfType<FrameworkElement>())
                        {
                            child.UpdateLayout();

                            double left = canvas.ActualWidth - child.ActualWidth - marginX;
                            Canvas.SetLeft(child, left);
                            Canvas.SetTop(child, y);

                            y += child.ActualHeight + spacing;
                        }
                        break;
                    }

                // GÓRA – lewy róg
                case ToastPosition.TopLeft:
                    {
                        double y = marginY;
                        foreach (FrameworkElement child in canvas.Children.OfType<FrameworkElement>())
                        {
                            child.UpdateLayout();

                            Canvas.SetLeft(child, marginX);
                            Canvas.SetTop(child, y);

                            y += child.ActualHeight + spacing;
                        }
                        break;
                    }

                // DÓŁ – prawy róg
                case ToastPosition.BottomRight:
                    {
                        double curBottom = marginY;
                        foreach (FrameworkElement child in canvas.Children.OfType<FrameworkElement>())
                        {
                            child.UpdateLayout();

                            double left = canvas.ActualWidth - child.ActualWidth - marginX;
                            double top = canvas.ActualHeight - curBottom - child.ActualHeight;

                            Canvas.SetLeft(child, left);
                            Canvas.SetTop(child, top);

                            curBottom += child.ActualHeight + spacing;
                        }
                        break;
                    }

                // DÓŁ – lewy róg
                case ToastPosition.BottomLeft:
                    {
                        double curBottom = marginY;
                        foreach (FrameworkElement child in canvas.Children.OfType<FrameworkElement>())
                        {
                            child.UpdateLayout();

                            double left = marginX;
                            double top = canvas.ActualHeight - curBottom - child.ActualHeight;

                            Canvas.SetLeft(child, left);
                            Canvas.SetTop(child, top);

                            curBottom += child.ActualHeight + spacing;
                        }
                        break;
                    }
            }
        }

        // ===== Krótkie aliasy typu =====
        public static void Info(string message) => Show(message, "info");
        public static void Success(string message) => Show(message, "success");
        public static void Warning(string message) => Show(message, "warning");
        public static void Error(string message) => Show(message, "error");

        // Zgodność z wcześniejszymi wywołaniami
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

            if (app.Dispatcher.CheckAccess())
                action();
            else
                app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }
    }
}
