namespace Spydomo.DTO
{
    public class ClientDto
    {
        public string? Name { get; set; }
        public string? CountryCode { get; set; }
        public string? RegionCode { get; set; }
        public string? City { get; set; }
        public string? AddressLine1 { get; set; } // 🆕 Street address
        public string? PostalCode { get; set; }   // 🆕 Postal code

        public string? ContactName { get; set; }
        public string? ContactEmail { get; set; }
        public string? BillingEmail { get; set; }
    }

}
