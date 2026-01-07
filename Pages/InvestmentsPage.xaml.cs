using Microsoft.Data.Sqlite;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Finly.Models;
using Finly.Services;
using Finly.Services.Features;

namespace Finly.Pages
{
    // Stabilna baza pod stan UI (np. confirm delete)
    public abstract class BindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    public sealed class InvestmentVm : BindableBase
    {
        public int Id { get; set; }

        private string _name = "";
        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        public InvestmentType Type { get; set; } = InvestmentType.Other;

        public decimal TargetAmount { get; set; }
        public decimal CurrentAmount { get; set; }
        public DateTime? TargetDate { get; set; }
        public string Description { get; set; } = "";

        // Tylko do wyświetlania (WPF nie może do tego pisać TwoWay)
        public string TypeDisplay => Type switch
        {
            InvestmentType.Stock => "Akcje",
            InvestmentType.Bond => "Obligacje",
            InvestmentType.Etf => "ETF",
            InvestmentType.Fund => "Fundusz",
            InvestmentType.Crypto => "Kryptowaluty",
            InvestmentType.Deposit => "Lokata",
            _ => "Inne"
        };

        public decimal Remaining => Math.Max(0, TargetAmount - CurrentAmount);

        public int MonthsLeft
        {
            get
            {
                if (TargetDate == null) return 0;
                var today = DateTime.Today;
                var d = TargetDate.Value.Date;
                if (d <= today) return 0;

                int months = (d.Year - today.Year) * 12 + (d.Month - today.Month);
                if (d.Day > today.Day) months++;
                if (months <= 0) months = 1;
                return months;
            }
        }

        public decimal MonthlyNeeded
        {
            get
            {
                if (Remaining <= 0) return 0m;
                var m = MonthsLeft;
                if (m <= 0) return Remaining;
                return Math.Round(Remaining / m, 2);
            }
        }

        public decimal CompletionPercent
        {
            get
            {
                if (TargetAmount <= 0) return 0m;
                var pct = (CurrentAmount / TargetAmount) * 100m;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                return Math.Round(pct, 1);
            }
        }

        private bool _isDeleteConfirmVisible;
        public bool IsDeleteConfirmVisible
        {
            get => _isDeleteConfirmVisible;
            set => Set(ref _isDeleteConfirmVisible, value);
        }

        public void HideDeleteConfirm() => IsDeleteConfirmVisible = false;
    }

    public sealed class AddInvestmentTile { }

    public partial class InvestmentsPage : UserControl
    {
        private readonly ObservableCollection<InvestmentVm> _investments = new();
        private readonly ObservableCollection<object> _items = new();

        private int? _editingId = null;
        private bool _initializedOk = false;

        public InvestmentsPage()
        {
            try
            {
                InitializeComponent();
                _initializedOk = true;

                InvestmentsRepeater.ItemsSource = _items;

                Loaded += InvestmentsPage_Loaded;

                try { DatabaseService.DataChanged += DatabaseService_DataChanged; } catch { }
                Unloaded += (_, _) => { try { DatabaseService.DataChanged -= DatabaseService_DataChanged; } catch { } };

                FormBorder.Visibility = Visibility.Collapsed;

                try { if (InvestmentTypeBox != null) InvestmentTypeBox.SelectedIndex = 0; } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("InvestmentsPage init error: " + ex);
                this.Content = new TextBlock
                {
                    Text = "Nie można załadować strony Inwestycje:\n" + ex.Message,
                    Foreground = Brushes.OrangeRed,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16)
                };
            }
        }

        private void InvestmentsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadInvestments();
        }

        private int _uid => UserService.GetCurrentUserId();

        private void LoadInvestments()
        {
            try
            {
                _investments.Clear();

                var uid = _uid;
                if (uid > 0)
                {
                    var rows = DatabaseService.GetInvestments(uid);
                    foreach (var r in rows)
                    {
                        var vm = new InvestmentVm
                        {
                            Id = r.Id,
                            Name = r.Name,
                            Type = r.Type,
                            TargetAmount = r.TargetAmount,
                            CurrentAmount = r.CurrentAmount,
                            TargetDate = string.IsNullOrWhiteSpace(r.TargetDate)
                                ? null
                                : (DateTime.TryParse(r.TargetDate, out var d) ? d : (DateTime?)null),
                            Description = r.Description ?? ""
                        };

                        _investments.Add(vm);
                    }
                }

                RebuildItems();
                RefreshKpis();

                InvestmentsErrorText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                try
                {
                    InvestmentsErrorText.Text = "Błąd podczas ładowania inwestycji: " + ex.Message;
                    InvestmentsErrorText.Visibility = Visibility.Visible;
                }
                catch { }

                System.Diagnostics.Debug.WriteLine("LoadInvestments error: " + ex);
            }
        }

        private void RebuildItems()
        {
            _items.Clear();
            foreach (var i in _investments) _items.Add(i);
            _items.Add(new AddInvestmentTile());
        }

        private void RefreshKpis()
        {
            var totalTarget = _investments.Sum(i => i.TargetAmount);
            var totalCurrent = _investments.Sum(i => i.CurrentAmount);
            var totalMonthly = _investments.Sum(i => i.MonthlyNeeded);

            TotalTargetText.Text = totalTarget.ToString("N2") + " zł";
            TotalCurrentText.Text = totalCurrent.ToString("N2") + " zł";
            TotalMonthlyText.Text = totalMonthly.ToString("N2") + " zł";
        }

        private static decimal ParseDecimal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0m;
            var raw = text.Replace(" ", "");

            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var d))
                return d;
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                return d;

            return 0m;
        }

        private InvestmentType ReadTypeFromForm()
        {
            var idx = InvestmentTypeBox?.SelectedIndex ?? 0;
            return idx switch
            {
                1 => InvestmentType.Stock,
                2 => InvestmentType.Bond,
                3 => InvestmentType.Etf,
                4 => InvestmentType.Fund,
                5 => InvestmentType.Crypto,
                6 => InvestmentType.Deposit,
                _ => InvestmentType.Other
            };
        }

        private void WriteTypeToForm(InvestmentType type)
        {
            if (InvestmentTypeBox == null) return;

            InvestmentTypeBox.SelectedIndex = type switch
            {
                InvestmentType.Stock => 1,
                InvestmentType.Bond => 2,
                InvestmentType.Etf => 3,
                InvestmentType.Fund => 4,
                InvestmentType.Crypto => 5,
                InvestmentType.Deposit => 6,
                _ => 0
            };
        }

        // ========= UI: Dodaj =========

        private void AddInvestmentCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_initializedOk)
            {
                ToastService.Error("Strona Inwestycje nie została poprawnie zainicjalizowana.");
                return;
            }

            _editingId = null;
            FormHeader.Text = "Dodaj inwestycję";
            InvestmentFormMessage.Text = string.Empty;
            FormBorder.Visibility = Visibility.Visible;

            ClearInvestmentForm();
        }

        // ========= UI: Edycja =========

        private void EditInvestment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;

            // chowamy ewentualne panele confirm w innych kartach
            foreach (var inv in _investments) inv.HideDeleteConfirm();

            _editingId = vm.Id;
            FormHeader.Text = "Edytuj inwestycję";
            InvestmentFormMessage.Text = string.Empty;
            FormBorder.Visibility = Visibility.Visible;

            InvestmentNameBox.Text = vm.Name;
            WriteTypeToForm(vm.Type);
            InvestmentTargetBox.Text = vm.TargetAmount.ToString("N2");
            InvestmentCurrentBox.Text = vm.CurrentAmount.ToString("N2");
            InvestmentTargetDatePicker.SelectedDate = vm.TargetDate;
            InvestmentDescriptionBox.Text = vm.Description ?? "";
        }

        // ========= Usuwanie (stabilnie) =========

        private void DeleteInvestment_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;

            // tylko jedna karta naraz w trybie confirm
            foreach (var inv in _investments)
                inv.IsDeleteConfirmVisible = false;

            vm.IsDeleteConfirmVisible = true;
        }

        private void DeleteInvestmentCancel_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is InvestmentVm vm)
                vm.IsDeleteConfirmVisible = false;
        }

        private void DeleteInvestmentConfirm_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;

            try
            {
                DeleteInvestmentFromDb(_uid, vm.Id);

                _investments.Remove(vm);
                RebuildItems();
                RefreshKpis();

                ToastService.Success("Usunięto inwestycję.");
            }
            catch (Exception ex)
            {
                ToastService.Error("Nie udało się usunąć inwestycji.\n" + ex.Message);
            }
        }

        // Lokalny DELETE – bo w Twoim DatabaseService nie ma DeleteInvestment(...)
        private static void DeleteInvestmentFromDb(int userId, int investmentId)
        {
            using var con = DatabaseService.GetConnection();
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
DELETE FROM Investments
WHERE Id = $id AND UserId = $uid;
";
            cmd.Parameters.AddWithValue("$id", investmentId);
            cmd.Parameters.AddWithValue("$uid", userId);

            var affected = cmd.ExecuteNonQuery();
            if (affected <= 0)
                throw new InvalidOperationException("Nie znaleziono inwestycji do usunięcia (lub brak uprawnień użytkownika).");

            try { DatabaseService.NotifyDataChanged(); } catch { }
        }

        // ========= Zapis =========

        private void AddInvestment_Click(object sender, RoutedEventArgs e)
        {
            // chowamy confirmy
            foreach (var inv in _investments) inv.HideDeleteConfirm();

            var name = (InvestmentNameBox.Text ?? "").Trim();
            var type = ReadTypeFromForm();

            var target = ParseDecimal(InvestmentTargetBox.Text);
            var current = ParseDecimal(InvestmentCurrentBox.Text);

            var date = InvestmentTargetDatePicker.SelectedDate;
            var desc = (InvestmentDescriptionBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                InvestmentFormMessage.Text = "Podaj nazwę inwestycji.";
                return;
            }

            if (target <= 0)
            {
                InvestmentFormMessage.Text = "Wartość docelowa musi być większa od zera.";
                return;
            }

            if (current < 0)
            {
                InvestmentFormMessage.Text = "Aktualna wartość nie może być ujemna.";
                return;
            }

            if (current > target)
            {
                InvestmentFormMessage.Text = "Aktualna wartość nie może przekraczać wartości docelowej.";
                return;
            }

            if (date == null)
            {
                InvestmentFormMessage.Text = "Wybierz termin docelowy.";
                return;
            }

            var model = new InvestmentModel
            {
                Id = _editingId ?? 0,
                UserId = _uid,
                Name = name,
                Type = type,
                TargetAmount = target,
                CurrentAmount = current,
                TargetDate = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Description = desc
            };

            try
            {
                if (_editingId.HasValue)
                {
                    DatabaseService.UpdateInvestment(model);
                    ToastService.Success("Zaktualizowano inwestycję.");
                }
                else
                {
                    DatabaseService.InsertInvestment(model);
                    ToastService.Success("Dodano inwestycję.");
                }

                // Źródłem prawdy jest DB – po sukcesie zawsze odświeżamy z DB
                LoadInvestments();

                ClearInvestmentForm();
                FormBorder.Visibility = Visibility.Collapsed;
                _editingId = null;
                InvestmentFormMessage.Text = string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Investment DB error: " + ex);
                InvestmentFormMessage.Text = "Błąd zapisu do bazy: " + ex.Message;
                // Formularz zostaje – użytkowniczka nie traci wpisanych danych.
            }
        }

        private void ClearInvestmentForm_Click(object sender, RoutedEventArgs e)
        {
            ClearInvestmentForm();
            InvestmentFormMessage.Text = string.Empty;
            FormBorder.Visibility = Visibility.Collapsed;
            _editingId = null;
        }

        private void ClearInvestmentForm()
        {
            InvestmentNameBox.Text = "";
            if (InvestmentTypeBox != null) InvestmentTypeBox.SelectedIndex = 0;
            InvestmentTargetBox.Text = "0,00";
            InvestmentCurrentBox.Text = "0,00";
            InvestmentTargetDatePicker.SelectedDate = null;
            InvestmentDescriptionBox.Text = "";
        }

        // ========= 0,00 input =========

        private void AmountBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (tb.Text == "0,00" || tb.Text == "0.00") tb.Clear();
            }
        }

        private void AmountBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                var val = ParseDecimal(tb.Text);
                tb.Text = val.ToString("N2");
            }
        }

        private void DatabaseService_DataChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(LoadInvestments), DispatcherPriority.Background);
            }
            catch { }
        }
    }
}
