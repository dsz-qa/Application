// Finly/Pages/SettingsPage.xaml.cs
using System.Windows;
using System.Windows.Controls;
using Finly.Services;

namespace Finly.Pages
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Ustaw radio zgodnie z bieżącym motywem
            if (ThemeService.Current == ThemeService.Theme.Light)
                LightRadio.IsChecked = true;
            else
                DarkRadio.IsChecked = true;

            // (opcjonalnie) lista pozycji tostów – jeśli używasz ToastService
            ToastPosCombo.ItemsSource = new[] { "Prawy górny", "Lewy górny", "Prawy dolny", "Lewy dolny" };
            ToastPosCombo.SelectedIndex = 0;
        }

        private void LightRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeService.Apply(ThemeService.Theme.Light);

        private void DarkRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeService.Apply(ThemeService.Theme.Dark);

        private void PreviewBtn_Click(object sender, RoutedEventArgs e)
        {
            // krótki podgląd działania (jeśli masz ToastService – fajnie dać feedback)
            try { ToastService.Success("Zastosowano motyw: " + ThemeService.Current); }
            catch { /* brak ToastService – ignoruj */ }
        }

        private void ToastPosCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Jeżeli masz obsługę pozycji tostów – wywołaj tutaj swój serwis
            // Np. ToastService.Position = (ToastPosition)ToastPosCombo.SelectedIndex;
        }
    }
}


