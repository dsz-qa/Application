using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Finly.Services;

namespace Finly.Pages
{
    public partial class SettingsPage : UserControl
    {
        // proste, statyczne „pamiętanie” ustawień (na czas działania aplikacji)
        private static bool _autoLoginEnabled = false;
        private static bool _animationsEnabled = true;

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 1) Pozycja toastów -> ComboBox
            ToastPositionCombo.SelectedIndex = PositionToIndex(ToastService.Position);

            // 2) Auto-login / animacje
            AutoLoginCheckBox.IsChecked = _autoLoginEnabled;
            AnimationsCheckBox.IsChecked = _animationsEnabled;
        }

        // =============== POWIADOMIENIA ===============

        private int PositionToIndex(ToastService.ToastPosition pos)
        {
            return pos switch
            {
                ToastService.ToastPosition.TopRight => 0,
                ToastService.ToastPosition.BottomRight => 1,
                ToastService.ToastPosition.BottomCenter => 2,
                _ => 0
            };
        }

        private ToastService.ToastPosition IndexToPosition(int index)
        {
            return index switch
            {
                0 => ToastService.ToastPosition.TopRight,
                1 => ToastService.ToastPosition.BottomRight,
                2 => ToastService.ToastPosition.BottomCenter,
                _ => ToastService.ToastPosition.TopRight
            };
        }

        private void ToastPositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ToastPositionCombo.SelectedIndex < 0)
                return;

            var pos = IndexToPosition(ToastPositionCombo.SelectedIndex);
            ToastService.Position = pos;
        }

        private void BtnShowTestToast_Click(object sender, RoutedEventArgs e)
        {
            ToastService.Info("To jest testowe powiadomienie z ustawień.");
        }

        // =============== LOGOWANIE ===============

        private void AutoLoginCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _autoLoginEnabled = AutoLoginCheckBox.IsChecked == true;
            ToastService.Info(_autoLoginEnabled
                ? "Auto-logowanie: włączone (logika może być podpięta w AuthWindow)."
                : "Auto-logowanie: wyłączone.");
        }

        // =============== INTERFEJS ===============

        private void AnimationsCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _animationsEnabled = AnimationsCheckBox.IsChecked == true;
            ToastService.Info(_animationsEnabled
                ? "Animacje interfejsu: włączone."
                : "Animacje interfejsu: wyłączone.");
        }
    }
}
