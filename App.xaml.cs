using System.Windows;
using Finly.Services;
using Finly.Views;
using QuestPDF.Infrastructure;   // ← DODAJ

namespace Finly
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // QuestPDF – wybór licencji (usuwa komunikat na wykresach PDF)
            QuestPDF.Settings.License = LicenseType.Community;

            DatabaseService.EnsureTables();

            // Startowy motyw
            ThemeService.Initialize(ThemeService.Theme.Dark);

            var auth = new AuthWindow();
            MainWindow = auth;
            auth.Show();
        }
    }
}
