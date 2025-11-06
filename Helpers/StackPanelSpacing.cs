using System.Windows;
using System.Windows.Controls;

namespace Finly.Helpers
{
    /// <summary>
    /// Attached property: helpers:StackPanelSpacing.Spacing="8"
    /// Ustawia Margin między dziećmi StackPanel.
    /// </summary>
    public static class StackPanelSpacing
    {
        public static double GetSpacing(DependencyObject obj) => (double)obj.GetValue(SpacingProperty);
        public static void SetSpacing(DependencyObject obj, double value) => obj.SetValue(SpacingProperty, value);

        public static readonly DependencyProperty SpacingProperty =
            DependencyProperty.RegisterAttached(
                "Spacing",
                typeof(double),
                typeof(StackPanelSpacing),
                new PropertyMetadata(0d, OnSpacingChanged));

        private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not StackPanel panel) return;

            panel.Loaded -= Panel_Loaded;
            panel.Loaded += Panel_Loaded;

            // Dla dynamicznych zmian (np. ItemsControl z PanelTemplate = StackPanel)
            if (panel.IsLoaded) Apply(panel);
        }

        private static void Panel_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is StackPanel p) Apply(p);
        }

        private static void Apply(StackPanel panel)
        {
            var spacing = GetSpacing(panel);
            var count = panel.Children.Count;
            for (int i = 0; i < count; i++)
            {
                if (panel.Children[i] is FrameworkElement fe)
                {
                    var m = fe.Margin;
                    if (panel.Orientation == Orientation.Horizontal)
                        fe.Margin = new Thickness(i == 0 ? 0 : spacing, m.Top, 0, m.Bottom);
                    else
                        fe.Margin = new Thickness(m.Left, i == 0 ? 0 : spacing, m.Right, 0);
                }
            }
        }
    }
}


