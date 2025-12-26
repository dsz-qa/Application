using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Finly.Models;

namespace Finly.ViewModels
{
    public class BudgetDialogViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { _name = value; Raise(); }
        }

        private BudgetType _type = BudgetType.Monthly;
        public BudgetType Type
        {
            get => _type;
            set
            {
                if (_type == value) return;
                _type = value;
                Raise();
                Raise(nameof(TypeDisplay));
                RecalculateEndDate();
            }
        }

        // Tylko do UI – źródłem prawdy jest enum
        public string TypeDisplay => Type switch
        {
            BudgetType.Weekly => "Tygodniowy",
            BudgetType.Monthly => "Miesięczny",
            BudgetType.Yearly => "Roczny",
            BudgetType.OneTime => "Jednorazowy",
            BudgetType.Rollover => "Przenoszony",
            _ => "Miesięczny"
        };

        private DateTime? _startDate = DateTime.Today;
        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                var v = value?.Date;
                if (_startDate == v) return;
                _startDate = v;
                Raise();
                RecalculateEndDate();
            }
        }

        private DateTime? _endDate;
        public DateTime? EndDate
        {
            get => _endDate;
            private set
            {
                var v = value?.Date;
                if (_endDate == v) return;
                _endDate = v;
                Raise();
            }
        }

        private decimal _plannedAmount;
        public decimal PlannedAmount
        {
            get => _plannedAmount;
            set
            {
                if (_plannedAmount == value) return;
                _plannedAmount = value;
                Raise();
            }
        }

        private decimal _spentAmount;
        public decimal SpentAmount
        {
            get => _spentAmount;
            set
            {
                if (_spentAmount == value) return;
                _spentAmount = value;
                Raise();
            }
        }

        public BudgetDialogViewModel()
        {
            RecalculateEndDate();
        }

        private void RecalculateEndDate()
        {
            if (StartDate == null)
            {
                EndDate = null;
                return;
            }

            var s = StartDate.Value.Date;

            EndDate = Type switch
            {
                BudgetType.Weekly => s.AddDays(6),
                BudgetType.Monthly => s.AddMonths(1).AddDays(-1),
                BudgetType.Yearly => s.AddYears(1).AddDays(-1),

                // Dla OneTime/Rollover – zależy od Twojej logiki.
                // Dla bezpieczeństwa: OneTime = ten sam dzień, Rollover = miesiąc.
                BudgetType.OneTime => s,
                BudgetType.Rollover => s.AddMonths(1).AddDays(-1),

                _ => s.AddMonths(1).AddDays(-1)
            };
        }
    }
}
