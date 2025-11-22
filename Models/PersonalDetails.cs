using System;

namespace Finly.Models
{
    public class PersonalDetails
    {
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        public DateTime? BirthDate { get; set; }

        public string? City { get; set; }
        public string? PostalCode { get; set; }
        public string? HouseNo { get; set; }

        public string? Phone { get; set; }
        public string? Street { get; set; }
    }
}
