using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Finly.ViewModels
{
    // bez sealed!
    public class LoanCardVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _id;
        private int _userId;
        private string _name = "";
        private decimal _principal;
        private decimal _interestRate;
        private DateTime _startDate;
        private int _termMonths;
        private int _paymentDay; // 0 = unspecified

        // ======= Snapshot z harmonogramu (CSV) =======
        private bool _hasSchedule;

        private decimal? _scheduleOriginalPrincipal;
        private decimal? _scheduleRemainingPrincipal;

        private decimal? _scheduleNextPaymentAmount;
        private DateTime? _scheduleNextPaymentDate;
        private int? _scheduleRemainingInstallments;

        // NOWE: podział kolejnej raty
        private decimal? _scheduleNextPaymentPrincipalPart;
        private decimal? _scheduleNextPaymentInterestPart;

        public int Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        public int UserId
        {
            get => _userId;
            set { if (_userId != value) { _userId = value; OnPropertyChanged(); } }
        }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Kapitał z inputu użytkownika / DB (fallback).
        /// Jeśli jest harmonogram, UI i KPI powinny korzystać z wartości z harmonogramu.
        /// </summary>
        public decimal Principal
        {
            get => _principal;
            set
            {
                if (_principal != value)
                {
                    _principal = value;
                    OnPropertyChanged();
                    NotifyDerived();
                }
            }
        }

        public decimal InterestRate
        {
            get => _interestRate;
            set
            {
                if (_interestRate != value)
                {
                    _interestRate = value;
                    OnPropertyChanged();
                    NotifyDerived();
                }
            }
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (_startDate != value)
                {
                    _startDate = value;
                    OnPropertyChanged();
                    NotifyDerived();
                }
            }
        }

        public int TermMonths
        {
            get => _termMonths;
            set
            {
                if (_termMonths != value)
                {
                    _termMonths = value;
                    OnPropertyChanged();
                    NotifyDerived();
                }
            }
        }

        public int PaymentDay
        {
            get => _paymentDay;
            set
            {
                if (_paymentDay != value)
                {
                    _paymentDay = value;
                    OnPropertyChanged();
                    NotifyDerived();
                }
            }
        }

        // ======= Informacja dla UI: czy liczymy z harmonogramu =======
        public bool HasSchedule
        {
            get => _hasSchedule;
            private set
            {
                if (_hasSchedule != value)
                {
                    _hasSchedule = value;
                    OnPropertyChanged();
                    NotifyDerived();
                }
            }
        }

        /// <summary>
        /// Wywołuj z LoansPage po sparsowaniu harmonogramu.
        /// Snapshot zawiera też rozbicie kolejnej raty (kapitał/odsetki) jeśli dostępne.
        /// </summary>
        public void ApplyScheduleSnapshot(
            decimal? originalPrincipal,
            decimal? remainingPrincipal,
            decimal? nextPaymentAmount,
            DateTime? nextPaymentDate,
            int? remainingInstallments,
            decimal? nextPaymentPrincipalPart,
            decimal? nextPaymentInterestPart)
        {
            HasSchedule = true;

            _scheduleOriginalPrincipal = originalPrincipal;
            _scheduleRemainingPrincipal = remainingPrincipal;

            _scheduleNextPaymentAmount = nextPaymentAmount;
            _scheduleNextPaymentDate = nextPaymentDate;
            _scheduleRemainingInstallments = remainingInstallments;

            _scheduleNextPaymentPrincipalPart = nextPaymentPrincipalPart;
            _scheduleNextPaymentInterestPart = nextPaymentInterestPart;

            NotifyDerived();
        }

        // kompatybilność wstecz (jeśli gdzieś jeszcze wołasz starą wersję)
        public void ApplyScheduleSnapshot(
            decimal? originalPrincipal,
            decimal? remainingPrincipal,
            decimal? nextPaymentAmount,
            DateTime? nextPaymentDate,
            int? remainingInstallments)
        {
            ApplyScheduleSnapshot(
                originalPrincipal,
                remainingPrincipal,
                nextPaymentAmount,
                nextPaymentDate,
                remainingInstallments,
                nextPaymentPrincipalPart: null,
                nextPaymentInterestPart: null);
        }

        public void ClearScheduleSnapshot()
        {
            HasSchedule = false;

            _scheduleOriginalPrincipal = null;
            _scheduleRemainingPrincipal = null;

            _scheduleNextPaymentAmount = null;
            _scheduleNextPaymentDate = null;
            _scheduleRemainingInstallments = null;

            _scheduleNextPaymentPrincipalPart = null;
            _scheduleNextPaymentInterestPart = null;

            NotifyDerived();
        }

        // ======= Wartości “display” (z harmonogramu jeśli jest) =======

        public decimal DisplayRemainingPrincipal =>
            _scheduleRemainingPrincipal.HasValue && _scheduleRemainingPrincipal.Value >= 0m
                ? _scheduleRemainingPrincipal.Value
                : Principal;

        public decimal DisplayOriginalPrincipal =>
            _scheduleOriginalPrincipal.HasValue && _scheduleOriginalPrincipal.Value > 0m
                ? _scheduleOriginalPrincipal.Value
                : (Principal > 0m ? Principal : 0m);

        public string PrincipalStr => DisplayRemainingPrincipal.ToString("N0") + " zł";

        public double PercentPaidClamped
        {
            get
            {
                var original = DisplayOriginalPrincipal;
                var remaining = DisplayRemainingPrincipal;

                if (original <= 0m) return 0.0;

                var paid = original - remaining;
                var pct = (double)(paid / original) * 100.0;

                if (pct < 0.0) pct = 0.0;
                if (pct > 100.0) pct = 100.0;
                return pct;
            }
        }

        // ======= Kolejna rata i termin — z harmonogramu, fallback do logiki “umownej” =======

        public DateTime NextPaymentDate
        {
            get
            {
                if (_scheduleNextPaymentDate.HasValue)
                    return _scheduleNextPaymentDate.Value.Date;

                var today = DateTime.Today;

                if (PaymentDay <= 0)
                {
                    var fallback = StartDate.AddMonths(1);
                    if (fallback < today) fallback = today.AddMonths(1);
                    return fallback.Date;
                }

                var candidateMonth = new DateTime(today.Year, today.Month, 1);
                int daysInThisMonth = DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month);
                int day = Math.Min(PaymentDay, daysInThisMonth);

                var candidate = new DateTime(candidateMonth.Year, candidateMonth.Month, day);
                if (candidate <= today)
                {
                    candidateMonth = candidateMonth.AddMonths(1);
                    int dim = DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month);
                    day = Math.Min(PaymentDay, dim);
                    candidate = new DateTime(candidateMonth.Year, candidateMonth.Month, day);
                }

                return candidate.Date;
            }
        }

        /// <summary>
        /// Kwota kolejnej raty (2 miejsca po przecinku).
        /// Z harmonogramu jeśli jest; fallback annuitetowy gdy nie ma harmonogramu.
        /// </summary>
        public decimal NextPayment
        {
            get
            {
                if (_scheduleNextPaymentAmount.HasValue && _scheduleNextPaymentAmount.Value > 0m)
                    return Math.Round(_scheduleNextPaymentAmount.Value, 2);

                // fallback (tylko gdy nie ma harmonogramu):
                var remaining = DisplayRemainingPrincipal;

                if (remaining <= 0m) return 0m;
                var monthsLeft = RemainingMonthsFallback();
                if (monthsLeft <= 0) monthsLeft = 1;

                var r = InterestRate / 100m / 12m;
                if (r == 0m) return Math.Round(remaining / monthsLeft, 2);

                var denom = 1m - (decimal)Math.Pow((double)(1m + r), -monthsLeft);
                if (denom == 0m) return Math.Round(remaining / monthsLeft, 2);

                var payment = remaining * r / denom;
                return Math.Round(payment, 2);
            }
        }

        // NOWE: data jako string (bez kwoty) do UI
        public string NextPaymentDateStr => NextPaymentDate.ToString("dd.MM.yyyy");

        // NOWE: stringi kwot (2 miejsca po przecinku)
        public string NextPaymentStr => NextPayment.ToString("N2") + " zł";

        public string NextPaymentInfo =>
            NextPayment.ToString("N2") + " zł · " + NextPaymentDate.ToString("dd.MM.yyyy");

        // NOWE: podział raty (jeżeli harmonogram podał; inaczej — “—”)
        public string NextPaymentPrincipalStr =>
            _scheduleNextPaymentPrincipalPart.HasValue
                ? Math.Round(_scheduleNextPaymentPrincipalPart.Value, 2).ToString("N2") + " zł"
                : "—";

        public string NextPaymentInterestStr =>
            _scheduleNextPaymentInterestPart.HasValue
                ? Math.Round(_scheduleNextPaymentInterestPart.Value, 2).ToString("N2") + " zł"
                : "—";

        public string RemainingTermStr
        {
            get
            {
                // jeśli harmonogram: liczba rat do końca
                if (_scheduleRemainingInstallments.HasValue)
                {
                    int monthsLeft = Math.Max(0, _scheduleRemainingInstallments.Value);
                    int years = monthsLeft / 12;
                    int months = monthsLeft % 12;
                    return $"{years} lat {months} mies.";
                }

                // fallback (bez harmonogramu)
                int fallbackMonths = RemainingMonthsFallback();
                int y = fallbackMonths / 12;
                int m = fallbackMonths % 12;
                return $"{y} lat {m} mies.";
            }
        }

        private int RemainingMonthsFallback()
        {
            if (TermMonths <= 0) return 0;

            var monthsElapsed =
                (DateTime.Today.Year - StartDate.Year) * 12 +
                (DateTime.Today.Month - StartDate.Month);

            return Math.Max(0, TermMonths - monthsElapsed);
        }

        private void NotifyDerived()
        {
            OnPropertyChanged(nameof(DisplayRemainingPrincipal));
            OnPropertyChanged(nameof(DisplayOriginalPrincipal));

            OnPropertyChanged(nameof(PrincipalStr));
            OnPropertyChanged(nameof(PercentPaidClamped));

            OnPropertyChanged(nameof(NextPaymentDate));
            OnPropertyChanged(nameof(NextPaymentDateStr));

            OnPropertyChanged(nameof(NextPayment));
            OnPropertyChanged(nameof(NextPaymentInfo));
            OnPropertyChanged(nameof(NextPaymentStr));

            OnPropertyChanged(nameof(NextPaymentPrincipalStr));
            OnPropertyChanged(nameof(NextPaymentInterestStr));

            OnPropertyChanged(nameof(RemainingTermStr));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
