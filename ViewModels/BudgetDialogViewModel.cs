using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Finly.Views.Dialogs
{
    public sealed class BudgetDialogViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _type = "Monthly"; // trzymamy stringi: Weekly/Monthly/Yearly/OneTime/Rollover
        private DateTime? _startDate = DateTime.Today;
        private DateTime? _endDate;
        private decimal _plannedAmount;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Type
        {
            get => _type;
            set
            {
                if (_type == value) return;
                _type = value ?? "Monthly";
                OnPropertyChanged();
                RecomputeEndDate();
            }
        }

        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                if (_startDate == value) return;
                _startDate = value;
                OnPropertyChanged();
                RecomputeEndDate();
            }
        }

        public DateTime? EndDate
        {
            get => _endDate;
            set
            {
                if (_endDate == value) return;
                _endDate = value;
                OnPropertyChanged();
            }
        }


        public decimal PlannedAmount
        {
            get => _plannedAmount;
            set { _plannedAmount = value; OnPropertyChanged(); }
        }

        private void RecomputeEndDate()
        {
            if (_startDate == null)
            {
                EndDate = null;
                return;
            }

            var s = _startDate.Value.Date;
            var t = (_type ?? "").Trim();

            EndDate = t switch
            {
                "Weekly" => s.AddDays(6),
                "Monthly" => new DateTime(s.Year, s.Month, 1).AddMonths(1).AddDays(-1),
                "Yearly" => new DateTime(s.Year, 12, 31),
                "OneTime" => s,
                "Rollover" => new DateTime(s.Year, s.Month, 1).AddMonths(1).AddDays(-1),
                _ => new DateTime(s.Year, s.Month, 1).AddMonths(1).AddDays(-1) // domyślnie Monthly
            };
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
