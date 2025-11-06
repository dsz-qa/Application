using System.Windows;
using Finly.Services;
using Finly.Views;

namespace Finly
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            DatabaseService.EnsureTables();

            // Startowy motyw (możesz zmienić na Light)
            ThemeService.Initialize(ThemeService.Theme.Dark);

            var auth = new AuthWindow();
            MainWindow = auth;
            auth.Show();
        }
    }
}
