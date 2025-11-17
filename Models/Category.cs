namespace Finly.Models
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Type { get; set; }   // 0: Wydatek, 1: Przychód, 2: Obie
        public string? Color { get; set; }
        public string? Icon { get; set; }
        public bool IsArchived { get; set; }
    }
}
