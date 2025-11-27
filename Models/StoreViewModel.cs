namespace Kursovoi.Models
{
    public class StoreViewModel
    {
        public int StoreID { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string LegalPerson { get; set; } = string.Empty;
        // Status: true = active, false = frozen
        public bool Status { get; set; }
        // Registration date as string (yyyy-MM-dd) if available
        public string RegistrationDate { get; set; } = string.Empty;
    }
}
