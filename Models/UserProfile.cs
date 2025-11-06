namespace Finly.Models
{
    /// Zbiorczy model profilu użytkownika używany w UI.
    /// Dane mogą fizycznie siedzieć w Users lub PersonalProfiles/CompanyProfiles.
    public class UserProfile
    {
        // Osobiste
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Address { get; set; }  // stary "ulica, kod, miasto" (opcjonalnie)

        // Nowe, granularne pola osobiste
        public string? BirthYear { get; set; }  // przechowujemy rok (lub null)
        public string? City { get; set; }
        public string? PostalCode { get; set; }
        public string? HouseNo { get; set; }

        // Firmowe (opcjonalne)
        public string? CompanyName { get; set; }
        public string? CompanyNip { get; set; }  // mapowane na Users.NIP / Users.CompanyNip (compat)
        public string? CompanyAddress { get; set; }
    }
}
