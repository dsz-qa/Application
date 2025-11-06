// Finly/Services/ThemeService.cs
using System;
using System.Linq;
using System.Windows;

namespace Finly.Services
{
    public static class ThemeService
    {
        public enum Theme { Light, Dark }

        public static Theme Current { get; private set; } = Theme.Dark;

        // Ścieżki do plików motywów
        private const string LightUri = "/Themes/Light.xaml";
        private const string DarkUri = "/Themes/Dark.xaml";

        /// <summary>Wczytuje motyw i podmienia go w Application.Resources.</summary>
        public static void Apply(Theme theme)
        {
            var app = Application.Current ?? throw new InvalidOperationException("Brak Application.Current");

            // Usuń poprzedni motyw (dowolny słownik z /Themes/)
            var md = app.Resources.MergedDictionaries;
            var toRemove = md.Where(d =>
                d.Source != null &&
                d.Source.OriginalString.Contains("/Themes/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var d in toRemove) md.Remove(d);

            // Dodaj nowy
            var src = theme == Theme.Light ? LightUri : DarkUri;
            md.Add(new ResourceDictionary { Source = new Uri(src, UriKind.Relative) });

            Current = theme;
        }

        /// <summary>Wstępna inicjalizacja (np. przy starcie aplikacji).</summary>
        public static void Initialize(Theme? preferred = null)
        {
            Apply(preferred ?? Theme.Dark); // domyślnie Ciemny (zmień, jeśli chcesz)
        }
    }
}


