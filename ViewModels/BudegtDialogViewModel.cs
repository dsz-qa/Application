using System;
using System.ComponentModel;

namespace Finly.ViewsModels
{
    public class BudgetDialogViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        private string _type = "Miesięczny";
        public string Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(nameof(Type)); RecalculateEndDate(); }
        }

        public string TypeDisplay { get; set; } = string.Empty;

        private DateTime? _startDate = DateTime.Today;
        public DateTime? StartDate
        {
            get => _startDate;
            set { _startDate = value?.Date; OnPropertyChanged(nameof(StartDate)); RecalculateEndDate(); }
        }

        private DateTime? _endDate;
        public DateTime? EndDate
        {
            get => _endDate;
            private set { _endDate = value?.Date; OnPropertyChanged(nameof(EndDate)); }
        }

        public decimal PlannedAmount { get; set; }
        public decimal SpentAmount { get; set; }

        public BudgetDialogViewModel()
        {
            //  Dzięki temu przy otwarciu okna od razu widać EndDate
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
                // ✅ 7 DNI (17 → 23)
                "Tygodniowy" => s.AddDays(6),

                // ✅ 1 MIESIĄC (17.01 → 16.02)
                "Miesięczny" => s.AddMonths(1).AddDays(-1),

                // ✅ 1 ROK (17.01.2026 → 16.01.2027)
                "Roczny" => s.AddYears(1).AddDays(-1),

                _ => s
            };
        }
    }
}