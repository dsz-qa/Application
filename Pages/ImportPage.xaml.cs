using Microsoft.Win32;
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class ImportPage : UserControl
    {
        public ImportPage()
        {
            InitializeComponent();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|Wszystkie pliki (*.*)|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog() == true)
            {
                FilePathBox.Text = dlg.FileName;
                LoadPreview();
            }
        }

        private void ReloadPreview_Click(object sender, RoutedEventArgs e) => LoadPreview();

        private void LoadPreview()
        {
            PreviewGrid.ItemsSource = null;

            if (!File.Exists(FilePathBox.Text))
                return;

            char delimiter = ',';
            var delimStr = (DelimiterBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (delimStr == ";") delimiter = ';';
            else if (delimStr == "Tab") delimiter = '\t';

            var dt = new DataTable();
            using (var sr = new StreamReader(FilePathBox.Text, Encoding.UTF8, true))
            {
                string? line;
                string[]? headers = null;

                if (HasHeaderCheckBox.IsChecked == true)
                {
                    headers = ReadCsvLine(sr, delimiter);
                    if (headers == null) return;
                    foreach (var h in headers) dt.Columns.Add(string.IsNullOrWhiteSpace(h) ? "Kolumna" + (dt.Columns.Count + 1) : h);
                }

                while ((line = sr.ReadLine()) != null)
                {
                    var cells = SplitCsv(line, delimiter);

                    if (headers == null)
                    {
                        // Brak nagłówków – utwórz kolumny do wielkości najdłuższego wiersza
                        if (dt.Columns.Count < cells.Length)
                        {
                            for (int i = dt.Columns.Count; i < cells.Length; i++)
                                dt.Columns.Add($"Col{i + 1}");
                        }
                    }

                    var row = dt.NewRow();
                    for (int i = 0; i < Math.Min(cells.Length, dt.Columns.Count); i++)
                        row[i] = cells[i];
                    dt.Rows.Add(row);
                }
            }

            PreviewGrid.ItemsSource = dt.DefaultView;

            // Zasilenie list kolumn
            var names = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            AmountCol.ItemsSource = names;
            DateCol.ItemsSource = names;
            CatCol.ItemsSource = names;
            DescCol.ItemsSource = names;

            if (AmountCol.SelectedIndex < 0 && names.Count > 0) AmountCol.SelectedIndex = 0;
            if (DateCol.SelectedIndex < 0 && names.Count > 1) DateCol.SelectedIndex = 1;
        }

        private static string[]? ReadCsvLine(StreamReader sr, char delimiter)
        {
            var line = sr.ReadLine();
            return line == null ? null : SplitCsv(line, delimiter);
        }

        // Prosty parser CSV (obsługa cudzysłowów i tabulatora)
        private static string[] SplitCsv(string line, char delimiter)
        {
            var list = new System.Collections.Generic.List<string>();
            var sb = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"'); i++; // escaped quote
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (ch == delimiter && !quoted)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }
            list.Add(sb.ToString());
            return list.ToArray();
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewGrid.ItemsSource is not DataView dv || dv.Table.Rows.Count == 0)
            {
                MessageBox.Show("Brak danych do importu.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string colAmount = AmountCol.SelectedItem?.ToString() ?? "";
            string colDate = DateCol.SelectedItem?.ToString() ?? "";
            string colCat = CatCol.SelectedItem?.ToString() ?? "";
            string colDesc = DescCol.SelectedItem?.ToString() ?? "";
            string defaultCat = DefaultCategoryBox.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(colAmount) || string.IsNullOrEmpty(colDate))
            {
                MessageBox.Show("Wskaż minimum kolumny: Kwota oraz Data.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fmt = DateFormatBox.Text?.Trim();
            var culture = CultureInfo.InvariantCulture;
            int uid = Finly.Services.UserService.CurrentUserId;

            int imported = 0;
            foreach (DataRow row in dv.Table.Rows)
            {
                // Kwota
                if (!TryParseDecimal(row[colAmount]?.ToString(), out var amount))
                    continue;

                // Data
                if (!TryParseDate(row[colDate]?.ToString(), fmt, culture, out var date))
                    continue;

                // Kategoria: z kolumny lub domyślna
                string? catName = null;
                if (!string.IsNullOrWhiteSpace(colCat))
                    catName = row[colCat]?.ToString();
                if (string.IsNullOrWhiteSpace(catName))
                    catName = defaultCat;

                int? categoryId = null;
                if (!string.IsNullOrWhiteSpace(catName))
                {
                    try
                    {
                        // Utwórz jeśli nie istnieje
                        categoryId = Finly.Services.DatabaseService.GetOrCreateCategoryId(uid, catName!.Trim());
                    }
                    catch
                    {
                        categoryId = null; // w razie błędu wstawiamy NULL
                    }
                }

                var desc = !string.IsNullOrWhiteSpace(colDesc) ? row[colDesc]?.ToString() : null;

                try
                {
                    using var c = Finly.Services.DatabaseService.GetConnection();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = @"INSERT INTO Expenses(UserId, Amount, Date, Description, CategoryId)
                                        VALUES (@u, @a, @d, @desc, @c);";
                    cmd.Parameters.AddWithValue("@u", uid);
                    cmd.Parameters.AddWithValue("@a", amount);
                    cmd.Parameters.AddWithValue("@d", date.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@desc", (object?)desc ?? DBNull.Value);
                    if (categoryId is int cid) cmd.Parameters.AddWithValue("@c", cid);
                    else cmd.Parameters.AddWithValue("@c", DBNull.Value);
                    cmd.ExecuteNonQuery();
                    imported++;
                }
                catch
                {
                    // Pomijamy wadliwy wiersz i lecimy dalej
                }
            }

            MessageBox.Show($"Zaimportowano {imported} rekordów.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static bool TryParseDecimal(string? s, out decimal value)
        {
            // Obsługa kropki/przecinka
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                return true;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("pl-PL"), out value))
                return true;
            value = 0m;
            return false;
        }

        private static bool TryParseDate(string? s, string? format, CultureInfo culture, out DateTime date)
        {
            if (!string.IsNullOrWhiteSpace(format) &&
                DateTime.TryParseExact(s, format, culture, DateTimeStyles.None, out date))
                return true;

            return DateTime.TryParse(s, culture, DateTimeStyles.None, out date);
        }
    }
}


