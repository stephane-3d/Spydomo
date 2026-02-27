namespace Spydomo.Infrastructure.Billing.DTO
{
    public class InvoiceDto
    {
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = "";
        public string PdfUrl { get; set; } = "";

    }

}
