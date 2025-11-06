using Finly.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Finly.Pages
{
    public partial class CategoriesPage : UserControl
    {
        private readonly int _userId;

        private sealed class CategorySummary
        {
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public double Sum { get; set; }
        }

        public CategoriesPage()
        {
            InitializeComponent();
            _userId = UserService.GetCurrentUserId();
            LoadSummary();
        }

        public CategoriesPage(int userId) : this()
        {
            _userId = userId;
            LoadSummary();
        }

        private void LoadSummary()
        {
            DateTime? from = FromDate?.SelectedDate;
            DateTime? to = ToDate?.SelectedDate;

            // Bierzemy wszystkie wydatki z nazwą kategorii i grupujemy
            var dt = DatabaseService.GetExpenses(_userId, from, to, null, null);
            var data = dt.AsEnumerable()
                .GroupBy(r => (r["CategoryName"]?.ToString() ?? "(brak)").Trim())
                .Select(g => new CategorySummary
                {
                    Name = string.IsNullOrWhiteSpace(g.Key) ? "(brak)" : g.Key,
                    Count = g.Count(),
                    Sum = g.Sum(r => Convert.ToDouble(r["Amount"]))
                })
                .OrderByDescending(x => x.Sum)
                .ThenBy(x => x.Name)
                .ToList();

            CategorySummaryGrid.ItemsSource = data;
        }

        private void Filter_Click(object sender, RoutedEventArgs e) => LoadSummary();

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            FromDate.SelectedDate = null;
            ToDate.SelectedDate = null;
            PresetRangeCombo.SelectedIndex = 0;
            LoadSummary();
        }

        private void PresetRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string? label = (PresetRangeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            RangePreset preset = label switch
            {
                "Dzisiaj" => RangePreset.Dzisiaj,
                "Ten tydzień" => RangePreset.TenTydzien,
                "Ten miesiąc" => RangePreset.TenMiesiac,
                "Ten rok" => RangePreset.TenRok,
                _ => RangePreset.Brak
            };

            DateRangeService.GetRange(preset, out var from, out var to);
            FromDate.SelectedDate = from;
            ToDate.SelectedDate = to;

            LoadSummary();
        }
    }
}
