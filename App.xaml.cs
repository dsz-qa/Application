using Finly.Services;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using Finly.Views;
using QuestPDF.Infrastructure;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Finly
{
    public partial class App : Application
    {
        private static readonly string LogDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Finly", "logs");

        private static readonly string CrashLogPath =
            Path.Combine(LogDir, "crash.log");

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 0) Globalna odporność na wyjątki (zanim cokolwiek wystartuje)
            HookGlobalExceptionHandlers();

            // 1) QuestPDF – licencja
            QuestPDF.Settings.License = LicenseType.Community;

            // 2) Baza danych
            try
            {
                DatabaseService.EnsureTables();
            }
            catch (Exception ex)
            {
                LogException("DatabaseService.EnsureTables", ex);
                // Nie kontynuujemy, bo aplikacja bez bazy nie ma sensu.
                MessageBox.Show(
                    "Nie udało się zainicjalizować bazy danych.\n\n" + ex.Message,
                    "Finly – błąd uruchomienia",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
                return;
            }

            // 3) Motyw
            try
            {
                ThemeService.Initialize(ThemeService.Theme.Dark);
            }
            catch (Exception ex)
            {
                LogException("ThemeService.Initialize", ex);
                // Motyw nie jest krytyczny – lecimy dalej.
            }

            // 4) Toast settings
            try
            {
                ToastService.Position = SettingsService.ToastPosition;
            }
            catch (Exception ex)
            {
                LogException("ToastService.Position", ex);
            }

            // 5) Autologowanie / start okna
            try
            {
                StartMainWindow();
            }
            catch (Exception ex)
            {
                LogException("StartMainWindow", ex);

                MessageBox.Show(
                    "Wystąpił błąd podczas uruchamiania aplikacji.\n\n" + ex.Message,
                    "Finly – błąd uruchomienia",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-2);
            }
        }

        private static void HookGlobalExceptionHandlers()
        {
            // UI thread (WPF Dispatcher)
            Current.DispatcherUnhandledException += (_, args) =>
            {
                if (IsLiveChartsCompositionTickerCrash(args.Exception))
                {
                    args.Handled = true;
                    LogException("LiveCharts CompositionTargetTicker (handled)", args.Exception);

                    return;
                }


                // 2) Inne wyjątki – log + kontrolowane domknięcie (lub możesz args.Handled=true i działać dalej,
                // ale to bywa ryzykowne, bo UI może zostać w niespójnym stanie).
                LogException("DispatcherUnhandledException", args.Exception);

                try
                {
                    MessageBox.Show(
                        "Wystąpił nieoczekiwany błąd aplikacji.\n\n" + args.Exception.Message +
                        "\n\nSzczegóły zapisano w logu: " + CrashLogPath,
                        "Finly – błąd krytyczny",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }

                // Domyślnie: nie “łykać” wszystkiego, bo możesz ukryć realne problemy.
                // Jeśli chcesz, żeby NIE zamykało aplikacji, ustaw args.Handled=true.
                args.Handled = false;
            };

            // Background Task exceptions
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                LogException("UnobservedTaskException", args.Exception);
                args.SetObserved();
            };

            // Non-UI thread exceptions (AppDomain)
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    LogException("AppDomain.UnhandledException", ex);
                else
                    LogText("AppDomain.UnhandledException", "Unknown exception object: " + args.ExceptionObject);

                // Tu już zwykle i tak proces leci, ale log zostaje.
            };
        }

        private void StartMainWindow()
        {
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
                var auth = new AuthWindow();
                MainWindow = auth;
                auth.Show();
            }
        }

        private static bool IsLiveChartsCompositionTickerCrash(Exception ex)
        {
            // Najczęściej to NullReferenceException w CompositionTargetTicker
            // Weryfikujemy po stack trace / typach, żeby nie połknąć “prawdziwych” nulli z Twojego kodu.
            if (ex is not NullReferenceException) return false;

            var st = ex.StackTrace ?? string.Empty;

            // Dwie najczęstsze ścieżki w LiveChartsCore.SkiaSharpView.WPF
            // (nie opieramy się na 1 stringu – wersje pakietów potrafią minimalnie zmieniać)
            return st.Contains("LiveChartsCore.SkiaSharpView.WPF.Rendering.CompositionTargetTicker", StringComparison.OrdinalIgnoreCase)
                || st.Contains("CompositionTargetTicker", StringComparison.OrdinalIgnoreCase)
                && st.Contains("LiveChartsCore", StringComparison.OrdinalIgnoreCase);
        }

        private static void LogException(string where, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(LogDir);

                var msg =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n" +
                    $"{ex.GetType().FullName}: {ex.Message}\n" +
                    $"{ex.StackTrace}\n" +
                    (ex.InnerException != null
                        ? $"INNER: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}\n"
                        : "") +
                    "------------------------------------------------------------\n";

                File.AppendAllText(CrashLogPath, msg);
            }
            catch
            {
                // Nie pozwalamy, by logowanie powodowało kolejne wyjątki.
            }
        }

        private static void LogText(string where, string text)
        {
            try
            {
                Directory.CreateDirectory(LogDir);

                var msg =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}\n" +
                    $"{text}\n" +
                    "------------------------------------------------------------\n";

                File.AppendAllText(CrashLogPath, msg);
            }
            catch { }
        }
    }
}

