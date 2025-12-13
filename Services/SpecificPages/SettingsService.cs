using System;
using System.IO;
using System.Text.Json;

namespace Finly.Services.SpecificPages
{
    /// <summary>
    /// Prosty zapis ustawień aplikacji do pliku JSON w %AppData%\Finly\settings.json
    /// </summary>
    public static class SettingsService
    {
        private class AppSettings
        {
            public string ToastPosition { get; set; } = nameof(ToastService.ToastPosition.BottomCenter);
            public bool AutoLoginEnabled { get; set; } = false;
            public bool InterfaceAnimationsEnabled { get; set; } = true;
            public int? LastUserId { get; set; }
        }

        private static readonly string _settingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "Finly", "settings.json");

        private static AppSettings _settings = Load();

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return s;
                }
            }
            catch
            {
                // w razie problemu wracamy do domyślnych
            }

            return new AppSettings();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // brak dramatu – ustawienia nie są krytyczne
            }
        }

        public static ToastService.ToastPosition ToastPosition
        {
            get
            {
                if (Enum.TryParse<ToastService.ToastPosition>(_settings.ToastPosition, out var pos))
                    return pos;
                return ToastService.ToastPosition.BottomCenter;
            }
            set
            {
                _settings.ToastPosition = value.ToString();
                Save();
            }
        }

        public static bool AutoLoginEnabled
        {
            get => _settings.AutoLoginEnabled;
            set
            {
                _settings.AutoLoginEnabled = value;
                Save();
            }
        }

        public static bool InterfaceAnimationsEnabled
        {
            get => _settings.InterfaceAnimationsEnabled;
            set
            {
                _settings.InterfaceAnimationsEnabled = value;
                Save();
            }
        }

        public static int? LastUserId
        {
            get => _settings.LastUserId;
            set
            {
                _settings.LastUserId = value;
                Save();
            }
        }
    }
}
