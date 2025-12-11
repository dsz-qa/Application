namespace Finly.ViewModels
{
    public class LoanInsightVm
    {
        public string Label { get; set; } = string.Empty;  // opis, np. "Całkowita suma zadłużenia:"
        public string Value { get; set; } = string.Empty;  // wartość, np. "347 219,44 zł"
    }
}
