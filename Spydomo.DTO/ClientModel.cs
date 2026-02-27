using System.ComponentModel.DataAnnotations;

namespace Spydomo.DTO
{
    public class ClientModel
    {
        [Required(ErrorMessage = "Organization name is required.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "City is required.")]
        public string City { get; set; }

        [Required(ErrorMessage = "Street address is required.")]
        public string AddressLine1 { get; set; }

        [Required(ErrorMessage = "ZIP / Postal code is required.")]
        public string PostalCode { get; set; }

        [Required(ErrorMessage = "Contact name is required.")]
        public string ContactName { get; set; }

        [Required(ErrorMessage = "Contact email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public string ContactEmail { get; set; }

        [Required(ErrorMessage = "Billing email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public string BillingEmail { get; set; }

        [Required(ErrorMessage = "Country is required.")]
        public string CountryCode { get; set; }

        [Required(ErrorMessage = "Region is required.")]
        public string? RegionCode { get; set; }
    }


}
