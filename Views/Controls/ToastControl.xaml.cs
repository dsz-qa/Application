using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Finly.Views.Controls
{
    public partial class ToastControl : UserControl
    {
        private const int SHOW_MS = 200;
        private const int HOLD_MS = 2500;
        private const int HIDE_MS = 250;

        public ToastControl() { InitializeComponent(); }

        public ToastControl(string message, string type = "info")
        {
            InitializeComponent();
            Msg.Text = message ?? string.Empty;

            switch ((type ?? "info").ToLowerInvariant())
            {
                case "success":
                    Root.Background = new SolidColorBrush(Color.FromRgb(35, 83, 46));
                    Icon.Text = "✔";
                    Icon.Foreground = Brushes.White;
                    break;

                case "error":
                    Root.Background = new SolidColorBrush(Color.FromRgb(115, 34, 34));
                    Icon.Text = "✖";
                    Icon.Foreground = Brushes.White;
                    break;

                case "warning":
                    Root.Background = new SolidColorBrush(Color.FromRgb(115, 88, 34));
                    Icon.Text = "⚠";
                    Icon.Foreground = Brushes.White;
                    break;

                default: // info
                    Root.Background = new SolidColorBrush(Color.FromRgb(43, 43, 43));
                    Icon.Text = "ℹ";
                    Icon.Foreground = Brushes.White;
                    break;
            }

            Loaded += async (_, __) => await RunAsync();
        }

        private async Task RunAsync()
        {
            await FadeAsync(0, 1, SHOW_MS);
            await Task.Delay(HOLD_MS);
            await FadeAsync(1, 0, HIDE_MS);

            if (Parent is Panel p) p.Children.Remove(this);
        }

        private Task FadeAsync(double from, double to, int ms)
        {
            var tcs = new TaskCompletionSource<bool>();
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            anim.Completed += (_, __) => tcs.SetResult(true);
            BeginAnimation(OpacityProperty, anim);
            return tcs.Task;
        }
    }
}
