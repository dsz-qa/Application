using Finly.Models;
using Finly.Services;
using Finly.Services.Features;
using Finly.Views.Dialogs;
using System;
using System.Collections.Generic;
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

namespace Finly.Pages
{
    // Stabilna baza pod stan UI (confirm delete) + binding
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

    internal sealed class ValuationRow
    {
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
    }

    public sealed class HistoryRowVm
    {
        public string Date { get; set; } = "";
        public string Value { get; set; } = "";
        public string Delta { get; set; } = "";
        public string DeltaPct { get; set; } = "";
    }

    public sealed class AddInvestmentTile { }

    public sealed class InvestmentVm : BindableBase
    {
        public int Id { get; set; }

        private string _name = "";
        public string Name { get => _name; set => Set(ref _name, value); }

        public InvestmentType Type { get; set; } = InvestmentType.Other;

        public string Description { get; set; } = "";

        // ====== wycena (ostatnia + poprzednia) ======
        public DateTime? LastDate { get; set; }
        public decimal? LastValue { get; set; }
        public DateTime? PrevDate { get; set; }
        public decimal? PrevValue { get; set; }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

        public string TypeDisplay => Type switch
        {
            InvestmentType.Stock => "Akcje",
            InvestmentType.Bond => "Obligacje",
            InvestmentType.Etf => "ETF",
            InvestmentType.Fund => "Fundusz",
            InvestmentType.Crypto => "Kryptowaluty",
            InvestmentType.Deposit => "Lokata",
            InvestmentType.Gold => "Złoto",
            _ => "Inne"
        };

        public string LastDateDisplay
        {
            get
            {
                if (LastDate == null) return "Brak wycen";
                return "Ostatnia: " + LastDate.Value.ToString("dd.MM.yyyy");
            }
        }

        public string LastValueDisplay
        {
            get
            {
                if (LastValue == null) return "—";
                return LastValue.Value.ToString("N2") + " zł";
            }
        }

        public decimal? DeltaValue
        {
            get
            {
                if (LastValue == null || PrevValue == null) return null;
                return LastValue.Value - PrevValue.Value;
            }
        }

        public decimal? DeltaPercent
        {
            get
            {
                if (LastValue == null || PrevValue == null) return null;
                if (PrevValue.Value == 0m) return null;
                return Math.Round((LastValue.Value - PrevValue.Value) / PrevValue.Value * 100m, 2);
            }
        }

        public Brush DeltaBrush
        {
            get
            {
                var dv = DeltaValue;
                if (dv == null) return Brushes.Gainsboro;
                if (dv.Value > 0) return Brushes.LimeGreen;
                if (dv.Value < 0) return Brushes.IndianRed;
                return Brushes.Gainsboro;
            }
        }

        public string DeltaDisplay
        {
            get
            {
                if (LastValue == null) return "Dodaj pierwszą wycenę, aby zobaczyć zmianę.";
                if (PrevValue == null) return "Brak poprzedniej wyceny (dodaj kolejną).";

                var dv = DeltaValue ?? 0m;
                var dp = DeltaPercent;

                var sign = dv > 0 ? "+" : (dv < 0 ? "−" : "");
                var abs = Math.Abs(dv);

                if (dp == null)
                    return $"{sign}{abs:N2} zł";

                var pctSign = dp.Value > 0 ? "+" : (dp.Value < 0 ? "−" : "");
                var pctAbs = Math.Abs(dp.Value);

                return $"{sign}{abs:N2} zł   ({pctSign}{pctAbs:N2}%)";
            }
        }

        private bool _isDeleteConfirmVisible;
        public bool IsDeleteConfirmVisible
        {
            get => _isDeleteConfirmVisible;
            set => Set(ref _isDeleteConfirmVisible, value);
        }

        public void HideDeleteConfirm() => IsDeleteConfirmVisible = false;

        public void RaiseAllComputed()
        {
            Raise(nameof(TypeDisplay));
            Raise(nameof(LastDateDisplay));
            Raise(nameof(LastValueDisplay));
            Raise(nameof(DeltaDisplay));
            Raise(nameof(DeltaBrush));
        }
    }

    // VM słupka wykresu (WPF)
    internal sealed class DeltaBarVm
    {
        public string Label { get; init; } = "";
        public double BarHeight { get; init; }   // 0..180
        public Brush Fill { get; init; } = Brushes.Gray;
        public string Tooltip { get; init; } = "";
        public string ValueText { get; init; } = "";
    }

    public partial class InvestmentsPage : UserControl, INotifyPropertyChanged
    {
        private readonly ObservableCollection<InvestmentVm> _investments = new();
        private readonly ObservableCollection<object> _items = new();

        // wykres WPF
        private readonly ObservableCollection<DeltaBarVm> _deltaBars = new();

        private bool _initializedOk;
        private bool _isActive;           // Loaded->true, Unloaded->false
        private bool _dataChangedHooked;  // żeby nie podpiąć 2x
        private long _reloadToken;        // chroni przed “zaległym” BeginInvoke

        private InvestmentVm? _selected;
        public InvestmentVm? SelectedInvestment
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedInvestment)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public InvestmentsPage()
        {
            try
            {
                InitializeComponent();
                _initializedOk = true;

                DataContext = this;
                InvestmentsRepeater.ItemsSource = _items;

                // wykres
                DeltaBars.ItemsSource = _deltaBars;

                Loaded += InvestmentsPage_Loaded;
                Unloaded += InvestmentsPage_Unloaded;
            }
            catch (Exception ex)
            {
                _initializedOk = false;
                System.Diagnostics.Debug.WriteLine("InvestmentsPage init error: " + ex);

                Content = new TextBlock
                {
                    Text = "Nie można załadować strony Inwestycje:\n" + ex.Message,
                    Foreground = Brushes.OrangeRed,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16)
                };
            }
        }

        private int _uid
        {
            get
            {
                try { return UserService.GetCurrentUserId(); }
                catch { return 0; }
            }
        }

        private Window? OwnerWindow
        {
            get
            {
                try { return Window.GetWindow(this); }
                catch { return null; }
            }
        }

        private void InvestmentsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_initializedOk) return;

            _isActive = true;
            _reloadToken++;

            HookDataChanged();

            try { EnsureValuationsSchema(); }
            catch (Exception ex) { SafeShowError("Błąd bazy (schema): " + ex.Message); }

            try
            {
                LoadInvestments();
                ApplySelection(SelectedInvestment ?? _investments.FirstOrDefault());
            }
            catch (Exception ex)
            {
                SafeShowError("Błąd ładowania inwestycji: " + ex.Message);
            }
        }

        private void InvestmentsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isActive = false;
            _reloadToken++;

            UnhookDataChanged();

            // czyścimy prawą stronę w 100% bezpiecznie
            try
            {
                _deltaBars.Clear();
                DeltaChartHint.Visibility = Visibility.Visible;

                GridValuations.ItemsSource = Array.Empty<HistoryRowVm>();
                NoSelectionCard.Visibility = Visibility.Visible;
                DetailsCard.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private void HookDataChanged()
        {
            if (_dataChangedHooked) return;

            try
            {
                DatabaseService.DataChanged += DatabaseService_DataChanged;
                _dataChangedHooked = true;
            }
            catch
            {
                _dataChangedHooked = false;
            }
        }

        private void UnhookDataChanged()
        {
            if (!_dataChangedHooked) return;

            try { DatabaseService.DataChanged -= DatabaseService_DataChanged; }
            catch { }

            _dataChangedHooked = false;
        }

        private void SafeShowError(string message)
        {
            try
            {
                InvestmentsErrorText.Text = message;
                InvestmentsErrorText.Visibility = Visibility.Visible;
            }
            catch { }
        }

        private void ClearError()
        {
            try
            {
                InvestmentsErrorText.Text = "";
                InvestmentsErrorText.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private Brush SafeGetBrush(string resourceKey, Brush fallback)
        {
            try
            {
                var obj = FindResource(resourceKey);
                if (obj is Brush b) return b;
                return fallback;
            }
            catch { return fallback; }
        }

        private static void EnsureValuationsSchema()
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS InvestmentValuations (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId        INTEGER NOT NULL,
    InvestmentId  INTEGER NOT NULL,
    Date          TEXT    NOT NULL, -- yyyy-MM-dd
    Value         REAL    NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_InvestmentValuations_User_Investment_Date
ON InvestmentValuations(UserId, InvestmentId, Date);
";
            cmd.ExecuteNonQuery();
        }

        private void LoadInvestments()
        {
            try
            {
                ClearError();

                var uid = _uid;
                var rememberSelectedId = SelectedInvestment?.Id ?? 0;

                _investments.Clear();

                if (uid > 0)
                {
                    var rows = DatabaseService.GetInvestments(uid) ?? Enumerable.Empty<InvestmentModel>();

                    foreach (var r in rows)
                    {
                        var vm = new InvestmentVm
                        {
                            Id = r.Id,
                            Name = r.Name ?? "",
                            Type = r.Type,
                            Description = r.Description ?? ""
                        };

                        var lastTwo = GetLastTwoValuations(uid, r.Id);
                        if (lastTwo.Count > 0)
                        {
                            vm.LastDate = lastTwo[0].Date;
                            vm.LastValue = lastTwo[0].Value;
                        }
                        if (lastTwo.Count > 1)
                        {
                            vm.PrevDate = lastTwo[1].Date;
                            vm.PrevValue = lastTwo[1].Value;
                        }

                        vm.IsSelected = (rememberSelectedId != 0 && vm.Id == rememberSelectedId);
                        vm.RaiseAllComputed();
                        _investments.Add(vm);
                    }
                }

                RebuildItems();
                RefreshKpis();

                var sel = _investments.FirstOrDefault(x => x.Id == rememberSelectedId) ?? _investments.FirstOrDefault();
                ApplySelection(sel);
            }
            catch (Exception ex)
            {
                SafeShowError("Błąd podczas ładowania inwestycji: " + ex.Message);
                System.Diagnostics.Debug.WriteLine("LoadInvestments error: " + ex);

                try
                {
                    _investments.Clear();
                    RebuildItems();
                    RefreshKpis();
                    ApplySelection(null);
                }
                catch { }
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
            try
            {
                var totalValue = _investments.Sum(i => i.LastValue ?? 0m);
                var totalPrev = _investments.Sum(i => i.PrevValue ?? 0m);

                var totalDelta = _investments.Sum(i =>
                {
                    if (i.LastValue == null || i.PrevValue == null) return 0m;
                    return i.LastValue.Value - i.PrevValue.Value;
                });

                TotalValueText.Text = totalValue.ToString("N2") + " zł";

                var dSign = totalDelta > 0 ? "+" : (totalDelta < 0 ? "−" : "");
                TotalDeltaText.Text = dSign + Math.Abs(totalDelta).ToString("N2") + " zł";
                TotalDeltaText.Foreground = totalDelta > 0 ? Brushes.LimeGreen :
                                            totalDelta < 0 ? Brushes.IndianRed :
                                            SafeGetBrush("App.Foreground", Brushes.White);

                if (totalPrev <= 0m)
                {
                    TotalDeltaPctText.Text = "—";
                    TotalDeltaPctText.Foreground = SafeGetBrush("App.Foreground", Brushes.White);
                }
                else
                {
                    var pct = Math.Round((totalValue - totalPrev) / totalPrev * 100m, 2);
                    var pSign = pct > 0 ? "+" : (pct < 0 ? "−" : "");
                    TotalDeltaPctText.Text = pSign + Math.Abs(pct).ToString("N2") + "%";
                    TotalDeltaPctText.Foreground = pct > 0 ? Brushes.LimeGreen :
                                                   pct < 0 ? Brushes.IndianRed :
                                                   SafeGetBrush("App.Foreground", Brushes.White);
                }
            }
            catch
            {
                try
                {
                    TotalValueText.Text = "—";
                    TotalDeltaText.Text = "—";
                    TotalDeltaPctText.Text = "—";
                }
                catch { }
            }
        }

        private static List<ValuationRow> GetLastTwoValuations(int userId, int investmentId)
        {
            var list = new List<ValuationRow>();

            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Date, Value
FROM InvestmentValuations
WHERE UserId = $uid AND InvestmentId = $iid
ORDER BY Date DESC, Id DESC
LIMIT 2;";
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$iid", investmentId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dateText = r.IsDBNull(0) ? "" : r.GetString(0);
                var dbl = r.IsDBNull(1) ? 0.0 : r.GetDouble(1);
                var val = Convert.ToDecimal(dbl);

                if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    dt = DateTime.Today;

                list.Add(new ValuationRow { Date = dt.Date, Value = val });
            }

            return list;
        }

        private static List<ValuationRow> GetAllValuations(int userId, int investmentId)
        {
            var list = new List<ValuationRow>();

            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
SELECT Date, Value
FROM InvestmentValuations
WHERE UserId = $uid AND InvestmentId = $iid
ORDER BY Date ASC, Id ASC;";
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$iid", investmentId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dateText = r.IsDBNull(0) ? "" : r.GetString(0);
                var dbl = r.IsDBNull(1) ? 0.0 : r.GetDouble(1);
                var val = Convert.ToDecimal(dbl);

                if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    dt = DateTime.Today;

                list.Add(new ValuationRow { Date = dt.Date, Value = val });
            }

            return list;
        }

        // =======================
        // Eventy z XAML
        // =======================

        private void InvestmentCard_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.DataContext is InvestmentVm vm)
                    ApplySelection(vm);
            }
            catch { }
        }

        private void AddInvestmentCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_initializedOk)
            {
                try { ToastService.Error("Strona Inwestycje nie została poprawnie zainicjalizowana."); } catch { }
                return;
            }

            try
            {
                foreach (var inv in _investments) inv.HideDeleteConfirm();

                var dlg = new AddEditInvestmentDialog { Owner = OwnerWindow };
                var ok = dlg.ShowDialog() == true;
                if (!ok) return;

                var model = dlg.BuildModelForDb(id: 0, userId: _uid);
                DatabaseService.InsertInvestment(model);

                try { ToastService.Success("Dodano inwestycję."); } catch { }

                LoadInvestments();
            }
            catch (Exception ex)
            {
                try { ToastService.Error("Błąd zapisu do bazy:\n" + ex.Message); } catch { }
            }
        }

        private void EditInvestment_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;

                foreach (var inv in _investments) inv.HideDeleteConfirm();

                var dlg = new AddEditInvestmentDialog { Owner = OwnerWindow };

                var model = new InvestmentModel
                {
                    Id = vm.Id,
                    UserId = _uid,
                    Name = vm.Name,
                    Type = vm.Type,
                    TargetAmount = 0m,
                    CurrentAmount = 0m,
                    TargetDate = null,
                    Description = vm.Description
                };

                dlg.LoadForEdit(model);

                var ok = dlg.ShowDialog() == true;
                if (!ok) return;

                var updated = dlg.BuildModelForDb(id: vm.Id, userId: _uid);
                DatabaseService.UpdateInvestment(updated);

                try { ToastService.Success("Zaktualizowano inwestycję."); } catch { }

                LoadInvestments();
            }
            catch (Exception ex)
            {
                try { ToastService.Error("Błąd zapisu do bazy:\n" + ex.Message); } catch { }
            }
        }

        private void AddValuation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;
                AddValuationFor(vm);
            }
            catch { }
        }

        private void AddValuationRight_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedInvestment == null) return;
                AddValuationFor(SelectedInvestment);
            }
            catch { }
        }

        private void DeleteInvestment_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;

                foreach (var inv in _investments)
                    inv.IsDeleteConfirmVisible = false;

                vm.IsDeleteConfirmVisible = true;
            }
            catch { }
        }

        private void DeleteInvestmentCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.DataContext is InvestmentVm vm)
                    vm.IsDeleteConfirmVisible = false;
            }
            catch { }
        }

        private void DeleteInvestmentConfirm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;

                DeleteInvestmentFromDb(_uid, vm.Id);

                _investments.Remove(vm);
                RebuildItems();
                RefreshKpis();

                if (SelectedInvestment?.Id == vm.Id)
                    ApplySelection(_investments.FirstOrDefault());

                try { ToastService.Success("Usunięto inwestycję."); } catch { }
            }
            catch (Exception ex)
            {
                try { ToastService.Error("Nie udało się usunąć inwestycji.\n" + ex.Message); } catch { }
            }
        }

        // =======================
        // Logika wyboru i prawa strona
        // =======================

        private void ApplySelection(InvestmentVm? vm)
        {
            SelectedInvestment = vm;

            foreach (var i in _investments)
                i.IsSelected = (vm != null && i.Id == vm.Id);

            UpdateRightPanel();
        }

        private void UpdateRightPanel()
        {
            if (!_isActive) return;

            try
            {
                if (SelectedInvestment == null)
                {
                    NoSelectionCard.Visibility = Visibility.Visible;
                    DetailsCard.Visibility = Visibility.Collapsed;

                    GridValuations.ItemsSource = Array.Empty<HistoryRowVm>();
                    BuildDeltaBars(Array.Empty<ValuationRow>());
                    return;
                }

                NoSelectionCard.Visibility = Visibility.Collapsed;
                DetailsCard.Visibility = Visibility.Visible;

                SelectedTitleText.Text = SelectedInvestment.Name;
                SelectedSubtitleText.Text = SelectedInvestment.TypeDisplay;

                var rows = GetAllValuations(_uid, SelectedInvestment.Id);

                if (rows.Count == 0)
                {
                    FirstText.Text = "—";
                    LastText.Text = "—";
                    ChangeText.Text = "—";
                    ChangeText.Foreground = SafeGetBrush("App.Foreground", Brushes.White);

                    GridValuations.ItemsSource = Array.Empty<HistoryRowVm>();
                    BuildDeltaBars(rows);
                    return;
                }

                var first = rows.First();
                var last = rows.Last();

                FirstText.Text = $"{first.Date:dd.MM.yyyy} • {first.Value:N2} zł";
                LastText.Text = $"{last.Date:dd.MM.yyyy} • {last.Value:N2} zł";

                var change = last.Value - first.Value;
                var sign = change > 0 ? "+" : (change < 0 ? "−" : "");
                var abs = Math.Abs(change);

                string pctText = "";
                if (first.Value > 0m)
                {
                    var pct = Math.Round(change / first.Value * 100m, 2);
                    var ps = pct > 0 ? "+" : (pct < 0 ? "−" : "");
                    pctText = $" ({ps}{Math.Abs(pct):N2}%)";
                }

                ChangeText.Text = $"{sign}{abs:N2} zł{pctText}";
                ChangeText.Foreground = change > 0 ? Brushes.LimeGreen :
                                        change < 0 ? Brushes.IndianRed :
                                        SafeGetBrush("App.Foreground", Brushes.White);

                // tabela
                var vms = new List<HistoryRowVm>();
                ValuationRow? prev = null;

                foreach (var r in rows)
                {
                    string delta = "—";
                    string dpct = "—";

                    if (prev != null)
                    {
                        var d = r.Value - prev.Value;
                        var ds = d > 0 ? "+" : (d < 0 ? "−" : "");
                        delta = $"{ds}{Math.Abs(d):N2} zł";

                        if (prev.Value != 0m)
                        {
                            var pct = Math.Round(d / prev.Value * 100m, 2);
                            var ps = pct > 0 ? "+" : (pct < 0 ? "−" : "");
                            dpct = $"{ps}{Math.Abs(pct):N2}%";
                        }
                    }

                    vms.Add(new HistoryRowVm
                    {
                        Date = r.Date.ToString("dd.MM.yyyy"),
                        Value = r.Value.ToString("N2") + " zł",
                        Delta = delta,
                        DeltaPct = dpct
                    });

                    prev = r;
                }

                GridValuations.ItemsSource = vms;

                // wykres WPF
                BuildDeltaBars(rows);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("UpdateRightPanel error: " + ex);
                try
                {
                    NoSelectionCard.Visibility = Visibility.Visible;
                    DetailsCard.Visibility = Visibility.Collapsed;
                    GridValuations.ItemsSource = Array.Empty<HistoryRowVm>();
                    BuildDeltaBars(Array.Empty<ValuationRow>());
                }
                catch { }
            }
        }

        /// <summary>
        /// Wykres delta w 100% WPF: zero wyjątków z LiveCharts.
        /// </summary>
        private void BuildDeltaBars(IReadOnlyList<ValuationRow> rows)
        {
            try
            {
                _deltaBars.Clear();

                if (rows == null || rows.Count < 2)
                {
                    try { DeltaChartHint.Visibility = Visibility.Visible; } catch { }
                    return;
                }

                var deltas = new List<(DateTime date, decimal delta)>();
                for (int i = 1; i < rows.Count; i++)
                    deltas.Add((rows[i].Date, rows[i].Value - rows[i - 1].Value));

                var maxAbs = deltas.Select(x => Math.Abs(x.delta)).DefaultIfEmpty(0m).Max();
                if (maxAbs <= 0m)
                {
                    try { DeltaChartHint.Visibility = Visibility.Visible; } catch { }
                    return;
                }

                try { DeltaChartHint.Visibility = Visibility.Collapsed; } catch { }

                const double maxHeight = 180.0;

                foreach (var (date, d) in deltas)
                {
                    var abs = Math.Abs(d);
                    var h = (double)(abs / maxAbs) * maxHeight;

                    if (h < 6) h = 6; // żeby było widać słupki

                    var isPos = d > 0m;
                    var isNeg = d < 0m;

                    var fill = isPos ? Brushes.LimeGreen :
                               isNeg ? Brushes.IndianRed :
                               Brushes.Gray;

                    var sign = isPos ? "+" : (isNeg ? "−" : "");
                    var valText = $"{sign}{abs:N0}";

                    _deltaBars.Add(new DeltaBarVm
                    {
                        Label = date.ToString("dd.MM", CultureInfo.CurrentCulture),
                        BarHeight = h,
                        Fill = fill,
                        ValueText = valText,
                        Tooltip = $"{date:dd.MM.yyyy}\nDelta: {sign}{abs:N2} zł"
                    });
                }
            }
            catch (Exception ex)
            {
                // wykres NIGDY nie może ubić strony
                System.Diagnostics.Debug.WriteLine("BuildDeltaBars error: " + ex);
                try
                {
                    _deltaBars.Clear();
                    DeltaChartHint.Visibility = Visibility.Visible;
                }
                catch { }
            }
        }

        // =======================
        // Dodawanie wyceny / DB
        // =======================

        private void AddValuationFor(InvestmentVm vm)
        {
            try
            {
                foreach (var inv in _investments) inv.HideDeleteConfirm();

                var dlg = new AddValuationDialog(vm.Name) { Owner = OwnerWindow };
                var ok = dlg.ShowDialog() == true;
                if (!ok) return;

                InsertValuation(_uid, vm.Id, dlg.SelectedDate, dlg.Value);

                try { ToastService.Success("Dodano wycenę."); } catch { }

                LoadInvestments();
            }
            catch (Exception ex)
            {
                try { ToastService.Error("Nie udało się dodać wyceny:\n" + ex.Message); } catch { }
            }
        }

        private static void InsertValuation(int userId, int investmentId, DateTime date, decimal value)
        {
            using var con = DatabaseService.GetConnection();
            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
INSERT INTO InvestmentValuations(UserId, InvestmentId, Date, Value)
VALUES($uid, $iid, $date, $val);";
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$iid", investmentId);
            cmd.Parameters.AddWithValue("$date", date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$val", value);

            cmd.ExecuteNonQuery();

            try { DatabaseService.NotifyDataChanged(); } catch { }
        }

        private static void DeleteInvestmentFromDb(int userId, int investmentId)
        {
            using var con = DatabaseService.GetConnection();

            using (var cmd1 = con.CreateCommand())
            {
                cmd1.CommandText = @"DELETE FROM InvestmentValuations WHERE UserId = $uid AND InvestmentId = $id;";
                cmd1.Parameters.AddWithValue("$id", investmentId);
                cmd1.Parameters.AddWithValue("$uid", userId);
                cmd1.ExecuteNonQuery();
            }

            using (var cmd2 = con.CreateCommand())
            {
                cmd2.CommandText = @"DELETE FROM Investments WHERE Id = $id AND UserId = $uid;";
                cmd2.Parameters.AddWithValue("$id", investmentId);
                cmd2.Parameters.AddWithValue("$uid", userId);

                var affected = cmd2.ExecuteNonQuery();
                if (affected <= 0)
                    throw new InvalidOperationException("Nie znaleziono inwestycji do usunięcia (lub brak uprawnień użytkownika).");
            }

            try { DatabaseService.NotifyDataChanged(); } catch { }
        }

        // =======================
        // DataChanged -> reload
        // =======================

        private void DatabaseService_DataChanged(object? sender, EventArgs e)
        {
            if (!_isActive) return;

            var tokenSnapshot = _reloadToken;

            try
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isActive) return;
                    if (tokenSnapshot != _reloadToken) return;
                    if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                    try
                    {
                        EnsureValuationsSchema();
                        LoadInvestments();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("DataChanged reload error: " + ex);
                        SafeShowError("Błąd odświeżania danych: " + ex.Message);
                    }
                }), DispatcherPriority.Background);
            }
            catch
            {
                // ignorujemy – event może przyjść w trakcie zamykania
            }
        }
    }
}
