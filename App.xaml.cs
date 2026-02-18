using Finly.Services;
using Finly.Services.Features;
using Finly.Services.SpecificPages;
using Finly.Views;
using QuestPDF.Infrastructure;
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

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
            HookGlobalExceptionHandlers();

            QuestPDF.Settings.License = LicenseType.Community;

            // 2) Baza danych
            try
            {
                DatabaseService.EnsureTables();

#if DEBUG
                Debug.WriteLine("=== Finly DB CHECK scan START ===");
                try
                {
                    using var con = DatabaseService.GetConnection();
                    if (con.State != ConnectionState.Open)
                        con.Open();

                    DbDebugTools.PrintTablesWithTypeCheck(con);
                }
                catch (Exception exDbg)
                {
                    LogException("DbDebugTools.PrintTablesWithTypeCheck", exDbg);
                    Debug.WriteLine("DB CHECK scan ERROR: " + exDbg);
                }
                Debug.WriteLine("=== Finly DB CHECK scan END ===");
#endif
            }
            catch (Exception ex)
            {
                LogException("DatabaseService.EnsureTables", ex);

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
            Current.DispatcherUnhandledException += (_, args) =>
            {
                if (IsLiveChartsCompositionTickerCrash(args.Exception))
                {
                    args.Handled = true;
                    LogException("LiveCharts CompositionTargetTicker (handled)", args.Exception);
                    return;
                }

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

                args.Handled = false;
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                LogException("UnobservedTaskException", args.Exception);
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    LogException("AppDomain.UnhandledException", ex);
                else
                    LogText("AppDomain.UnhandledException", "Unknown exception object: " + args.ExceptionObject);
            };
        }

        private void StartMainWindow()
        {
            if (SettingsService.AutoLoginEnabled &&
                SettingsService.LastUserId is int uid &&
                uid > 0)
            {
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
            if (ex is not NullReferenceException) return false;

            var st = ex.StackTrace ?? string.Empty;

            return st.Contains("LiveChartsCore.SkiaSharpView.WPF.Rendering.CompositionTargetTicker", StringComparison.OrdinalIgnoreCase)
                || (st.Contains("CompositionTargetTicker", StringComparison.OrdinalIgnoreCase)
                    && st.Contains("LiveChartsCore", StringComparison.OrdinalIgnoreCase));
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
            catch { }
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
