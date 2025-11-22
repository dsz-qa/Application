namespace Finly.Models
{
    public class UserProfile
    {
        // DANE OSOBISTE
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Address { get; set; }
        public string? BirthYear { get; set; }
        public string? City { get; set; }
        public string? PostalCode { get; set; }
        public string? HouseNo { get; set; }

        // DANE FIRMY
        public string? CompanyName { get; set; }
        public string? CompanyNip { get; set; }
        public string? CompanyRegon { get; set; }
        public string? CompanyKrs { get; set; }
        public string? CompanyAddress { get; set; }
    }
}
