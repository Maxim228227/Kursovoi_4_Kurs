namespace Kursovoi.Models
{
    public class StockViewModel
    {
        public int ProductID { get; set; }
        public int Quantity { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public System.DateTime UpdatedAt { get; set; }
    }
}
