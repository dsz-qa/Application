using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Finly.Views; // ShellWindow
using Finly.Views.Controls; // ToastControl

namespace Finly.Services
{
    /// <summary>
    /// Serwis do wyświetlania toastów (Info / Success / Warning / Error).
    /// W ShellWindow musi być:
    /// <Canvas x:Name="ToastLayer" Grid.Row="1"
    /// HorizontalAlignment="Stretch"
    /// VerticalAlignment="Stretch"/>
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

        // simple dedupe to avoid spamming many identical toasts in short period
        private static readonly object _dedupeLock = new();
        private static readonly System.Collections.Generic.Dictionary<string, System.DateTime> _recent = new();
        private const int DedupeMs = 1200; // suppress identical toasts within this window

        // ===== Główna metoda =====
        public static void Show(string message, string type = "info")
        => RunOnUI(() =>
        {
            try
            {
                // short-circuit if shell or canvas missing
                var shell = Application.Current.Windows.OfType<ShellWindow>().FirstOrDefault();
                if (shell is null) return;
                if (shell.FindName("ToastLayer") is not Canvas canvas) return;

                // dedupe by message+type
                var key = (type ?? "info") + "|" + (message ?? string.Empty);
                var now = System.DateTime.UtcNow;
                lock (_dedupeLock)
                {
                    if (_recent.TryGetValue(key, out var prev))
                    {
                        if ((now - prev).TotalMilliseconds < DedupeMs)
                        {
                            // already shown recently – skip
                            return;
                        }
                    }
                    _recent[key] = now;

                    // schedule cleanup after a bit longer than toast lifetime
                    var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                    try
                    {
                        dispatcher.InvokeAsync(async () =>
                        {
                            await System.Threading.Tasks.Task.Delay(DedupeMs + 2500);
                            lock (_dedupeLock)
                            {
                                // remove old entries
                                var threshold = System.DateTime.UtcNow.AddMilliseconds(-DedupeMs - 2000);
                                var keys = _recent.Where(kv => kv.Value < threshold).Select(kv => kv.Key).ToList();
                                foreach (var k in keys) _recent.Remove(k);
                            }
                        }, DispatcherPriority.Background);
                    }
                    catch { }
                }

                var toast = new ToastControl(message ?? string.Empty, type ?? "info");
                canvas.Children.Add(toast);
                toast.Loaded += (_, _) => LayoutToasts(canvas);
            }
            catch
            {
                // ignore any toast errors to avoid recursive failures
            }
        });

        // ===== Rozmieszczanie toastów =====
        private static void LayoutToasts(Canvas canvas)
        {
            const double marginX = 24; // odstęp od krawędzi
            const double marginY = 24;
            const double spacing = 10; // odstęp między toastami

            try
            {
                switch (Position)
                {
                    // DÓŁ – środek
                    case ToastPosition.BottomCenter:
                        {
                            double curBottom = marginY;
                            var children = canvas.Children.OfType<FrameworkElement>().ToList();
                            foreach (FrameworkElement child in children)
                            {
                                // Do not call UpdateLayout here (can cause cross-thread or reentrancy issues when called from various contexts).
                                // Use existing measured sizes or DesiredSize as fallback.
                                double childWidth = (child.ActualWidth > 0) ? child.ActualWidth : (child.DesiredSize.Width > 0 ? child.DesiredSize.Width : 100);
                                double childHeight = (child.ActualHeight > 0) ? child.ActualHeight : (child.DesiredSize.Height > 0 ? child.DesiredSize.Height : 40);

                                double left = (canvas.ActualWidth - childWidth) / 2.0;
                                double top = canvas.ActualHeight - curBottom - childHeight;

                                Canvas.SetLeft(child, left);
                                Canvas.SetTop(child, top);

                                curBottom += childHeight + spacing;
                            }
                            break;
                        }

                    // GÓRA – prawy róg
                    case ToastPosition.TopRight:
                        {
                            double y = marginY;
                            var children = canvas.Children.OfType<FrameworkElement>().ToList();
                            foreach (FrameworkElement child in children)
                            {
                                double childWidth = (child.ActualWidth > 0) ? child.ActualWidth : (child.DesiredSize.Width > 0 ? child.DesiredSize.Width : 100);
                                double childHeight = (child.ActualHeight > 0) ? child.ActualHeight : (child.DesiredSize.Height > 0 ? child.DesiredSize.Height : 40);

                                double left = canvas.ActualWidth - childWidth - marginX;
                                Canvas.SetLeft(child, left);
                                Canvas.SetTop(child, y);

                                y += childHeight + spacing;
                            }
                            break;
                        }

                    // GÓRA – lewy róg
                    case ToastPosition.TopLeft:
                        {
                            double y = marginY;
                            var children = canvas.Children.OfType<FrameworkElement>().ToList();
                            foreach (FrameworkElement child in children)
                            {
                                double childHeight = (child.ActualHeight > 0) ? child.ActualHeight : (child.DesiredSize.Height > 0 ? child.DesiredSize.Height : 40);

                                Canvas.SetLeft(child, marginX);
                                Canvas.SetTop(child, y);

                                y += childHeight + spacing;
                            }
                            break;
                        }

                    // DÓŁ – prawy róg
                    case ToastPosition.BottomRight:
                        {
                            double curBottom = marginY;
                            var children = canvas.Children.OfType<FrameworkElement>().ToList();
                            foreach (FrameworkElement child in children)
                            {
                                double childWidth = (child.ActualWidth > 0) ? child.ActualWidth : (child.DesiredSize.Width > 0 ? child.DesiredSize.Width : 100);
                                double childHeight = (child.ActualHeight > 0) ? child.ActualHeight : (child.DesiredSize.Height > 0 ? child.DesiredSize.Height : 40);

                                double left = canvas.ActualWidth - childWidth - marginX;
                                double top = canvas.ActualHeight - curBottom - childHeight;

                                Canvas.SetLeft(child, left);
                                Canvas.SetTop(child, top);

                                curBottom += childHeight + spacing;
                            }
                            break;
                        }

                    // DÓŁ – lewy róg
                    case ToastPosition.BottomLeft:
                        {
                            double curBottom = marginY;
                            var children = canvas.Children.OfType<FrameworkElement>().ToList();
                            foreach (FrameworkElement child in children)
                            {
                                double childHeight = (child.ActualHeight > 0) ? child.ActualHeight : (child.DesiredSize.Height > 0 ? child.DesiredSize.Height : 40);

                                double left = marginX;
                                double top = canvas.ActualHeight - curBottom - childHeight;

                                Canvas.SetLeft(child, left);
                                Canvas.SetTop(child, top);

                                curBottom += childHeight + spacing;
                            }
                            break;
                        }
                }
            }
            catch
            {
                // Silently ignore layout errors to avoid crashing the app
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
