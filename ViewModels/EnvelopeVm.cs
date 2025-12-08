using System;

namespace Finly.ViewModels
{
    // ViewModel for envelope tile displayed in ItemsControl
    public sealed class EnvelopeVm
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Target { get; set; }
        public decimal Allocated { get; set; }
        public string GoalText { get; set; } = "";
        public string Description { get; set; } = "";
        public string Deadline { get; set; } = ""; // formatted date
        public string Note { get; set; } = "";
    }
}
