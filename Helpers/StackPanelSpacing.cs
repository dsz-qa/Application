using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Helpers
{
    public static class StackPanelSpacing
    {
        public static readonly DependencyProperty SpacingProperty =
            DependencyProperty.RegisterAttached(
                "Spacing",
                typeof(double),
                typeof(StackPanelSpacing),
                new PropertyMetadata(0d, OnSpacingChanged));

        public static void SetSpacing(Panel element, double value) => element.SetValue(SpacingProperty, value);
        public static double GetSpacing(Panel element) => (double)element.GetValue(SpacingProperty);

        private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Panel panel)
            {
                panel.Loaded -= Panel_Loaded;
                panel.Loaded += Panel_Loaded;
                Apply(panel);
            }
        }

        private static void Panel_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is Panel p) Apply(p);
        }

        private static void Apply(Panel panel)
        {
            double gap = GetSpacing(panel);
            if (gap <= 0) return;

            bool vertical = panel is not StackPanel sp || sp.Orientation == Orientation.Vertical;

            var children = panel.Children.OfType<FrameworkElement>().ToList();
            for (int i = 0; i < children.Count; i++)
            {
                var el = children[i];
                var m = el.Margin;

                el.Margin = vertical
                    ? new Thickness(m.Left, i == 0 ? m.Top : gap, m.Right, m.Bottom)
                    : new Thickness(i == 0 ? m.Left : gap, m.Top, m.Right, m.Bottom);
            }
        }
    }
}




