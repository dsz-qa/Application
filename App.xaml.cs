using System.Windows;
using Finly.Services;
using Finly.Views;
using QuestPDF.Infrastructure;   // QuestPDF

namespace Finly
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // QuestPDF – wybór licencji (usuwa komunikat na wykresach PDF)
            QuestPDF.Settings.License = LicenseType.Community;

            // Baza danych
            DatabaseService.EnsureTables();

            // Startowy motyw
            ThemeService.Initialize(ThemeService.Theme.Dark);

            // Wczytaj ustawienia – pozycja toastów
            ToastService.Position = SettingsService.ToastPosition;

            // ===== AUTO-LOGOWANIE =====
            if (SettingsService.AutoLoginEnabled &&
                SettingsService.LastUserId is int uid &&
                uid > 0)
            {
                // Ustaw aktualnego użytkownika
                UserService.CurrentUserId = uid;
                UserService.CurrentUserEmail = UserService.GetEmail(uid);

                bool onboarded = DatabaseService.IsUserOnboarded(uid);

                Window next = onboarded
                    ? new ShellWindow()
                    : new FirstRunWindow(uid);

                MainWindow = next;
                next.Show();
            }
            else
            {
                // Standardowo – okno logowania
                var auth = new AuthWindow();
                MainWindow = auth;
                auth.Show();
            }
        }
    }
}
