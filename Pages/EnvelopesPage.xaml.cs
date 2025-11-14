using Finly.Services;
using Finly.Views;
using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Finly.Pages
{
    public partial class EnvelopesPage : UserControl
    {
        private int _userId;
        private DataTable? _dt;
        private int? _editingId = null;

        public EnvelopesPage()
        {
            InitializeComponent();
            Loaded += EnvelopesPage_Loaded;
        }

        // opcjonalnie – jeśli gdzieś tworzysz stronę z userId:
        public EnvelopesPage(int userId) : this()
        {
            _userId = userId;
        }

        private void EnvelopesPage_Loaded(object sender, RoutedEventArgs e)
        {
            // jeśli nie został przekazany w konstruktorze – bierzemy z UserService
            if (_userId <= 0)
                _userId = UserService.GetCurrentUserId();

            if (_userId <= 0)
                return;

            // 1) ekran powitalny tylko raz na użytkownika
            if (!UserService.HasSeenEnvelopesIntro(_userId))
            {
                var dlg = new EnvelopesIntroWindow
                {
                    Owner = Application.Current.MainWindow
                };

                if (dlg.ShowDialog() == true)
                {
                    UserService.MarkEnvelopesIntroSeen(_userId);
                }
            }

            // 2) normalne załadowanie danych
            LoadAll();
            SetAddMode();
        }

        // ================= LOAD ==================

        private void LoadAll()
        {
            try
            {
                // 1) Wolna gotówka + odłożona gotówka
                var freeCash = DatabaseService.GetCashOnHand(_userId);  // możesz wydawać
                var savedTotal = DatabaseService.GetSavedCash(_userId);   // cała odłożona pula

                // w textboxie edytujesz CAŁĄ odłożoną gotówkę
                CashBox.Text = savedTotal.ToString("N2");

                // 2) Koperty
                _dt = DatabaseService.GetEnvelopesTable(_userId);

                if (_dt != null)
                {
                    if (!_dt.Columns.Contains("Remaining"))
                        _dt.Columns.Add("Remaining", typeof(decimal));

                    foreach (DataRow r in _dt.Rows)
                    {
                        var target = SafeDec(r["Target"]);
                        var alloc = SafeDec(r["Allocated"]);
                        r["Remaining"] = target - alloc;
                    }
                }

                EnvelopesGrid.ItemsSource = _dt?.DefaultView;
                EnvelopesCards.ItemsSource = _dt?.DefaultView;

                // 3) Sumy
                var allocated = _dt?.AsEnumerable().Sum(r => SafeDec(r["Allocated"])) ?? 0m;
                var unassigned = savedTotal - allocated;   // odłożona, ale nie w kopertach

                FreeCashText.Text = freeCash.ToString("N2") + " zł";
                EnvelopesSumText.Text = allocated.ToString("N2") + " zł";
                UnassignedText.Text = unassigned.ToString("N2") + " zł";

                UnassignedText.Foreground = unassigned >= 0
                    ? SystemColors.ControlTextBrush
                    : Brushes.IndianRed;
            }
            catch (Exception ex)
            {
                FormMessage.Text = "Błąd odczytu: " + ex.Message;
            }
        }


        // ================= HELPERS ==================

        private static decimal SafeParse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            var raw = s.Replace(" ", "");
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var d)) return d;
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
            return 0m;
        }

        private static decimal SafeDec(object? o)
        {
            if (o == null || o == DBNull.Value) return 0m;
            try { return Convert.ToDecimal(o); } catch { return 0m; }
        }

        private void SetAddMode()
        {
            _editingId = null;
            FormHeader.Text = "Dodaj kopertę";
            NameBox.Text = string.Empty;
            TargetBox.Text = "0,00";
            AllocatedBox.Text = "0,00";
            NoteBox.Text = string.Empty;
            FormMessage.Text = string.Empty;
            SaveEnvelopeBtn.Content = "Dodaj";
        }

        private void SetEditMode(int id, DataRow row)
        {
            _editingId = id;
            FormHeader.Text = $"Edytuj kopertę #{id}";
            NameBox.Text = row["Name"]?.ToString() ?? "";
            TargetBox.Text = SafeDec(row["Target"]).ToString("N2");
            AllocatedBox.Text = SafeDec(row["Allocated"]).ToString("N2");
            NoteBox.Text = row["Note"]?.ToString() ?? "";
            FormMessage.Text = string.Empty;
            SaveEnvelopeBtn.Content = "Zapisz zmiany";
        }

        // ================= GOTÓWKA ==================

        // przycisk w sekcji „Gotówka odłożona”
        private void SaveCash_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saved = SafeParse(CashBox.Text);
                if (saved < 0)
                {
                    FormMessage.Text = "Kwota odłożonej gotówki nie może być ujemna.";
                    return;
                }

                DatabaseService.SetSavedCash(_userId, saved);
                LoadAll();
                FormMessage.Text = "Zapisano odłożoną gotówkę.";
            }
            catch (Exception ex)
            {
                FormMessage.Text = "Błąd zapisu gotówki: " + ex.Message;
            }
        }

        // ================= GRID / AKCJE ==================

        private void StartAdd_Click(object sender, RoutedEventArgs e) => SetAddMode();

        private void EditEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if (_dt == null) return;
            if ((sender as Button)?.Tag is not object tag) return;
            if (!int.TryParse(tag.ToString(), out var id)) return;

            var row = _dt.AsEnumerable().FirstOrDefault(r => Convert.ToInt32(r["Id"]) == id);
            if (row == null) return;

            SetEditMode(id, row);
        }

        private void DeleteEnvelope_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not object tag) return;
            if (!int.TryParse(tag.ToString(), out var id)) return;

            var ask = MessageBox.Show(
                "Usunąć tę kopertę? Operacja nieodwracalna.",
                "Usuń kopertę",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            try
            {
                DatabaseService.DeleteEnvelope(id);
                LoadAll();
                SetAddMode();
                FormMessage.Text = "Kopertę usunięto.";
            }
            catch (Exception ex)
            {
                FormMessage.Text = "Błąd usuwania: " + ex.Message;
            }
        }

        private void EnvelopesGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_dt == null) return;
            if (EnvelopesGrid.SelectedItem is not DataRowView drv) return;
            var id = Convert.ToInt32(drv.Row["Id"]);
            SetEditMode(id, drv.Row);
        }

        // ================= FORMULARZ ==================

        private void SaveEnvelope_Click(object sender, RoutedEventArgs e)
        {
            var name = (NameBox.Text ?? "").Trim();
            var target = SafeParse(TargetBox.Text);
            var allocated = SafeParse(AllocatedBox.Text);
            var note = NoteBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                FormMessage.Text = "Podaj nazwę koperty.";
                return;
            }
            if (allocated < 0 || target < 0)
            {
                FormMessage.Text = "Kwoty nie mogą być ujemne.";
                return;
            }

            try
            {
                if (_editingId is int id)   // update
                {
                    DatabaseService.UpdateEnvelope(id, _userId, name, target, allocated, note);
                    FormMessage.Text = "Zapisano zmiany.";
                }
                else                        // insert
                {
                    DatabaseService.InsertEnvelope(_userId, name, target, allocated, note);
                    FormMessage.Text = "Dodano kopertę.";
                }

                LoadAll();
                SetAddMode();
            }
            catch (Exception ex)
            {
                FormMessage.Text = "Błąd zapisu: " + ex.Message;
            }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e) => SetAddMode();
    }
}



