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
    // ============================================================
    // Common base (jak u Ciebie) – zostaje
    // ============================================================
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

        private string _description = "";
        public string Description { get => _description; set => Set(ref _description, value); }

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
                return "Ostatnia: " + LastDate.Value.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
            }
        }

        public string LastValueDisplay
        {
            get
            {
                if (LastValue == null) return "—";
                return LastValue.Value.ToString("N2", CultureInfo.CurrentCulture) + " zł";
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
                if (PrevValue.Value <= 0.0001m) return null;
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

    internal sealed class DeltaBarVm
    {
        public string Label { get; init; } = "";
        public double BarHeight { get; init; }   // 0..180
        public Brush Fill { get; init; } = Brushes.Gray;
        public string Tooltip { get; init; } = "";
        public string ValueText { get; init; } = "";
    }

    // ============================================================
    // NEW: ViewModel (spójnie jak DashboardPage)
    // ============================================================
    internal sealed class InvestmentsViewModel : BindableBase
    {
        public ObservableCollection<InvestmentVm> Investments { get; } = new();
        public ObservableCollection<object> Items { get; } = new();
        public ObservableCollection<HistoryRowVm> Valuations { get; } = new();
        public ObservableCollection<DeltaBarVm> DeltaBars { get; } = new();

        private InvestmentVm? _selected;
        public InvestmentVm? SelectedInvestment
        {
            get => _selected;
            set => Set(ref _selected, value);
        }

        // KPI (trzymamy jako stringi dla prostoty)
        private string _totalValueText = "—";
        public string TotalValueText { get => _totalValueText; set => Set(ref _totalValueText, value); }

        private string _totalDeltaText = "—";
        public string TotalDeltaText { get => _totalDeltaText; set => Set(ref _totalDeltaText, value); }

        private Brush _totalDeltaBrush = Brushes.White;
        public Brush TotalDeltaBrush { get => _totalDeltaBrush; set => Set(ref _totalDeltaBrush, value); }

        private string _totalDeltaPctText = "—";
        public string TotalDeltaPctText { get => _totalDeltaPctText; set => Set(ref _totalDeltaPctText, value); }

        private Brush _totalDeltaPctBrush = Brushes.White;
        public Brush TotalDeltaPctBrush { get => _totalDeltaPctBrush; set => Set(ref _totalDeltaPctBrush, value); }

        // Error
        private string _errorText = "";
        public string ErrorText { get => _errorText; set => Set(ref _errorText, value); }

        private bool _hasError;
        public bool HasError { get => _hasError; set => Set(ref _hasError, value); }

        public void ClearError()
        {
            ErrorText = "";
            HasError = false;
        }

        public void ShowError(string msg)
        {
            ErrorText = msg;
            HasError = true;
        }

        public void RebuildItems()
        {
            Items.Clear();
            foreach (var i in Investments) Items.Add(i);
            Items.Add(new AddInvestmentTile());
        }
    }

    // ============================================================
    // InvestmentsPage – DataContext = VM (jak DashboardPage)
    // ============================================================
    public partial class InvestmentsPage : UserControl
    {
        private readonly InvestmentsViewModel _vm = new();

        private bool _initializedOk;
        private bool _isActive;
        private bool _dataChangedHooked;
        private long _reloadToken;

        public InvestmentsPage()
        {
            try
            {
                InitializeComponent();
                _initializedOk = true;

                // KLUCZ: NIE this. Tylko VM.
                DataContext = _vm;

                // Bind ItemsControl i delta bars przez VM (bez ręcznych kolekcji w Page)
                InvestmentsRepeater.ItemsSource = _vm.Items;
                DeltaBars.ItemsSource = _vm.DeltaBars;

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
                ApplySelection(_vm.SelectedInvestment ?? _vm.Investments.FirstOrDefault());
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

            // Czyścimy UI/VM w 100% bezpiecznie
            try
            {
                _vm.DeltaBars.Clear();
                _vm.Valuations.Clear();

                if (DeltaChartHint != null) DeltaChartHint.Visibility = Visibility.Visible;

                if (NoSelectionCard != null) NoSelectionCard.Visibility = Visibility.Visible;
                if (DetailsCard != null) DetailsCard.Visibility = Visibility.Collapsed;
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
                _vm.ShowError(message);

                if (InvestmentsErrorText == null) return;
                InvestmentsErrorText.Text = message;
                InvestmentsErrorText.Visibility = Visibility.Visible;
            }
            catch { }
        }

        private void ClearError()
        {
            try
            {
                _vm.ClearError();

                if (InvestmentsErrorText == null) return;
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

        // ============================================================
        // LOAD
        // ============================================================
        private void LoadInvestments()
        {
            try
            {
                ClearError();

                var uid = _uid;
                var rememberSelectedId = _vm.SelectedInvestment?.Id ?? 0;

                _vm.Investments.Clear();

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
                        _vm.Investments.Add(vm);
                    }
                }

                _vm.RebuildItems();
                RefreshKpis();

                var sel = _vm.Investments.FirstOrDefault(x => x.Id == rememberSelectedId) ?? _vm.Investments.FirstOrDefault();
                ApplySelection(sel);
            }
            catch (Exception ex)
            {
                SafeShowError("Błąd podczas ładowania inwestycji: " + ex.Message);
                System.Diagnostics.Debug.WriteLine("LoadInvestments error: " + ex);

                try
                {
                    _vm.Investments.Clear();
                    _vm.RebuildItems();
                    RefreshKpis();
                    ApplySelection(null);
                }
                catch { }
            }
        }

        private void RefreshKpis()
        {
            try
            {
                var totalValue = _vm.Investments.Sum(i => i.LastValue ?? 0m);

                var eligible = _vm.Investments
                    .Where(i => i.LastValue.HasValue && i.PrevValue.HasValue)
                    .ToList();

                var totalPrevEligible = eligible.Sum(i => i.PrevValue!.Value);
                var totalDeltaEligible = eligible.Sum(i => i.LastValue!.Value - i.PrevValue!.Value);

                _vm.TotalValueText = totalValue.ToString("N2", CultureInfo.CurrentCulture) + " zł";

                var dSign = totalDeltaEligible > 0 ? "+" : (totalDeltaEligible < 0 ? "−" : "");
                _vm.TotalDeltaText = dSign + Math.Abs(totalDeltaEligible).ToString("N2", CultureInfo.CurrentCulture) + " zł";
                _vm.TotalDeltaBrush = totalDeltaEligible > 0 ? Brushes.LimeGreen :
                                      totalDeltaEligible < 0 ? Brushes.IndianRed :
                                      SafeGetBrush("App.Foreground", Brushes.White);

                if (eligible.Count == 0 || totalPrevEligible <= 0.0001m)
                {
                    _vm.TotalDeltaPctText = "—";
                    _vm.TotalDeltaPctBrush = SafeGetBrush("App.Foreground", Brushes.White);
                }
                else
                {
                    var pct = Math.Round(totalDeltaEligible / totalPrevEligible * 100m, 2);
                    var pSign = pct > 0 ? "+" : (pct < 0 ? "−" : "");
                    _vm.TotalDeltaPctText = pSign + Math.Abs(pct).ToString("N2", CultureInfo.CurrentCulture) + "%";
                    _vm.TotalDeltaPctBrush = pct > 0 ? Brushes.LimeGreen :
                                             pct < 0 ? Brushes.IndianRed :
                                             SafeGetBrush("App.Foreground", Brushes.White);
                }

                // Ustawiamy też UI (bo masz TextBlock x:Name, a nie bindingi)
                if (TotalValueText != null) TotalValueText.Text = _vm.TotalValueText;
                if (TotalDeltaText != null)
                {
                    TotalDeltaText.Text = _vm.TotalDeltaText;
                    TotalDeltaText.Foreground = _vm.TotalDeltaBrush;
                }
                if (TotalDeltaPctText != null)
                {
                    TotalDeltaPctText.Text = _vm.TotalDeltaPctText;
                    TotalDeltaPctText.Foreground = _vm.TotalDeltaPctBrush;
                }
            }
            catch
            {
                try
                {
                    _vm.TotalValueText = "—";
                    _vm.TotalDeltaText = "—";
                    _vm.TotalDeltaPctText = "—";

                    if (TotalValueText != null) TotalValueText.Text = "—";
                    if (TotalDeltaText != null) TotalDeltaText.Text = "—";
                    if (TotalDeltaPctText != null) TotalDeltaPctText.Text = "—";
                }
                catch { }
            }
        }

        private static readonly string[] _dateFormats = new[] { "yyyy-MM-dd", "yyyy-M-d" };

        private static bool TryParseDbDate(string? s, out DateTime dt)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (DateTime.TryParseExact(s.Trim(),
                                          _dateFormats,
                                          CultureInfo.InvariantCulture,
                                          DateTimeStyles.None,
                                          out dt))
                {
                    dt = dt.Date;
                    return true;
                }
            }

            dt = DateTime.Today;
            return false;
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

                if (!TryParseDbDate(dateText, out var dt))
                    dt = DateTime.Today;

                var val = Convert.ToDecimal(dbl);

                list.Add(new ValuationRow { Date = dt, Value = val });
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

                if (!TryParseDbDate(dateText, out var dt))
                    dt = DateTime.Today;

                var val = Convert.ToDecimal(dbl);

                list.Add(new ValuationRow { Date = dt, Value = val });
            }

            return list;
        }

        // ============================================================
        // EVENTS (XAML) – zostają
        // ============================================================
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
                foreach (var inv in _vm.Investments) inv.HideDeleteConfirm();

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

                foreach (var inv in _vm.Investments) inv.HideDeleteConfirm();

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
                if (_vm.SelectedInvestment == null) return;
                AddValuationFor(_vm.SelectedInvestment);
            }
            catch { }
        }

        private void DeleteInvestment_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as FrameworkElement)?.DataContext is not InvestmentVm vm) return;

                foreach (var inv in _vm.Investments)
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

                _vm.Investments.Remove(vm);
                _vm.RebuildItems();
                RefreshKpis();

                if (_vm.SelectedInvestment?.Id == vm.Id)
                    ApplySelection(_vm.Investments.FirstOrDefault());

                try { ToastService.Success("Usunięto inwestycję."); } catch { }
            }
            catch (Exception ex)
            {
                try { ToastService.Error("Nie udało się usunąć inwestycji.\n" + ex.Message); } catch { }
            }
        }

        // ============================================================
        // Selection + Right panel
        // ============================================================
        private void ApplySelection(InvestmentVm? vm)
        {
            _vm.SelectedInvestment = vm;

            foreach (var i in _vm.Investments)
                i.IsSelected = (vm != null && i.Id == vm.Id);

            UpdateRightPanel();
        }

        private void UpdateRightPanel()
        {
            if (!_isActive) return;

            try
            {
                if (_vm.SelectedInvestment == null)
                {
                    if (NoSelectionCard != null) NoSelectionCard.Visibility = Visibility.Visible;
                    if (DetailsCard != null) DetailsCard.Visibility = Visibility.Collapsed;

                    _vm.Valuations.Clear();
                    BuildDeltaBars(Array.Empty<ValuationRow>());

                    // MiniTable source
                    if (ValuationsMiniTable != null) ValuationsMiniTable.ItemsSource = _vm.Valuations;
                    return;
                }

                if (NoSelectionCard != null) NoSelectionCard.Visibility = Visibility.Collapsed;
                if (DetailsCard != null) DetailsCard.Visibility = Visibility.Visible;

                if (SelectedTitleText != null) SelectedTitleText.Text = _vm.SelectedInvestment.Name;
                if (SelectedSubtitleText != null) SelectedSubtitleText.Text = _vm.SelectedInvestment.TypeDisplay;

                var rows = GetAllValuations(_uid, _vm.SelectedInvestment.Id);

                if (rows.Count == 0)
                {
                    if (FirstText != null) FirstText.Text = "—";
                    if (LastText != null) LastText.Text = "—";
                    if (ChangeText != null)
                    {
                        ChangeText.Text = "—";
                        ChangeText.Foreground = SafeGetBrush("App.Foreground", Brushes.White);
                    }

                    _vm.Valuations.Clear();
                    if (ValuationsMiniTable != null) ValuationsMiniTable.ItemsSource = _vm.Valuations;

                    BuildDeltaBars(rows);
                    return;
                }

                var first = rows.First();
                var last = rows.Last();

                if (FirstText != null) FirstText.Text = $"{first.Date:dd.MM.yyyy} • {first.Value.ToString("N2", CultureInfo.CurrentCulture)} zł";
                if (LastText != null) LastText.Text = $"{last.Date:dd.MM.yyyy} • {last.Value.ToString("N2", CultureInfo.CurrentCulture)} zł";

                var change = last.Value - first.Value;
                var sign = change > 0 ? "+" : (change < 0 ? "−" : "");
                var abs = Math.Abs(change);

                string pctText = "";
                if (first.Value > 0m)
                {
                    var pct = Math.Round(change / first.Value * 100m, 2);
                    var ps = pct > 0 ? "+" : (pct < 0 ? "−" : "");
                    pctText = $" ({ps}{Math.Abs(pct).ToString("N2", CultureInfo.CurrentCulture)}%)";
                }

                if (ChangeText != null)
                {
                    ChangeText.Text = $"{sign}{abs.ToString("N2", CultureInfo.CurrentCulture)} zł{pctText}";
                    ChangeText.Foreground = change > 0 ? Brushes.LimeGreen :
                                            change < 0 ? Brushes.IndianRed :
                                            SafeGetBrush("App.Foreground", Brushes.White);
                }

                // TABLE -> MiniTable
                _vm.Valuations.Clear();

                ValuationRow? prev = null;
                foreach (var r in rows)
                {
                    string delta = "—";
                    string dpct = "—";

                    if (prev != null)
                    {
                        var d = r.Value - prev.Value;
                        var ds = d > 0 ? "+" : (d < 0 ? "−" : "");
                        delta = $"{ds}{Math.Abs(d).ToString("N2", CultureInfo.CurrentCulture)} zł";

                        if (prev.Value != 0m)
                        {
                            var pct = Math.Round(d / prev.Value * 100m, 2);
                            var ps = pct > 0 ? "+" : (pct < 0 ? "−" : "");
                            dpct = $"{ps}{Math.Abs(pct).ToString("N2", CultureInfo.CurrentCulture)}%";
                        }
                    }

                    _vm.Valuations.Add(new HistoryRowVm
                    {
                        Date = r.Date.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture),
                        Value = r.Value.ToString("N2", CultureInfo.CurrentCulture) + " zł",
                        Delta = delta,
                        DeltaPct = dpct
                    });

                    prev = r;
                }

                if (ValuationsMiniTable != null) ValuationsMiniTable.ItemsSource = _vm.Valuations;

                // CHART
                BuildDeltaBars(rows);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("UpdateRightPanel error: " + ex);
                try
                {
                    if (NoSelectionCard != null) NoSelectionCard.Visibility = Visibility.Visible;
                    if (DetailsCard != null) DetailsCard.Visibility = Visibility.Collapsed;

                    _vm.Valuations.Clear();
                    if (ValuationsMiniTable != null) ValuationsMiniTable.ItemsSource = _vm.Valuations;

                    BuildDeltaBars(Array.Empty<ValuationRow>());
                }
                catch { }
            }
        }

        /// <summary>Wykres delta w 100% WPF</summary>
        private void BuildDeltaBars(IReadOnlyList<ValuationRow> rows)
        {
            try
            {
                _vm.DeltaBars.Clear();

                if (rows == null || rows.Count < 2)
                {
                    if (DeltaChartHint != null) DeltaChartHint.Visibility = Visibility.Visible;
                    return;
                }

                var deltas = new List<(DateTime date, decimal delta)>();
                for (int i = 1; i < rows.Count; i++)
                    deltas.Add((rows[i].Date, rows[i].Value - rows[i - 1].Value));

                var maxAbs = deltas.Select(x => Math.Abs(x.delta)).DefaultIfEmpty(0m).Max();
                if (maxAbs <= 0m)
                {
                    if (DeltaChartHint != null) DeltaChartHint.Visibility = Visibility.Visible;
                    return;
                }

                if (DeltaChartHint != null) DeltaChartHint.Visibility = Visibility.Collapsed;

                const double maxHeight = 180.0;

                foreach (var (date, d) in deltas)
                {
                    var abs = Math.Abs(d);

                    var h = (double)(abs / maxAbs) * maxHeight;
                    if (h > 0 && h < 6) h = 6;

                    var isPos = d > 0m;
                    var isNeg = d < 0m;

                    var fill = isPos ? Brushes.LimeGreen :
                               isNeg ? Brushes.IndianRed :
                               Brushes.Gray;

                    var sign = isPos ? "+" : (isNeg ? "−" : "");

                    var valText = $"{sign}{Math.Round(abs, 0).ToString("N0", CultureInfo.CurrentCulture)}";

                    _vm.DeltaBars.Add(new DeltaBarVm
                    {
                        Label = date.ToString("dd.MM", CultureInfo.CurrentCulture),
                        BarHeight = h,
                        Fill = fill,
                        ValueText = valText,
                        Tooltip = $"{date:dd.MM.yyyy}\nDelta: {sign}{abs.ToString("N2", CultureInfo.CurrentCulture)} zł"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("BuildDeltaBars error: " + ex);
                try
                {
                    _vm.DeltaBars.Clear();
                    if (DeltaChartHint != null) DeltaChartHint.Visibility = Visibility.Visible;
                }
                catch { }
            }
        }

        // ============================================================
        // Add valuation / DB
        // ============================================================
        private void AddValuationFor(InvestmentVm vm)
        {
            try
            {
                foreach (var inv in _vm.Investments) inv.HideDeleteConfirm();

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

        // ============================================================
        // DataChanged -> reload (bezpiecznie)
        // ============================================================
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
