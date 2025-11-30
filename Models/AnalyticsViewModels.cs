using System.Collections.Generic;

namespace Kursovoi.Models
{
    public class StockAnalyticsViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public string LastUpdate { get; set; }
    }
}