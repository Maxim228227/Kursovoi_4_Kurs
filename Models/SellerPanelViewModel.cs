using System.Collections.Generic;

namespace Kursovoi.Models
{
    public class SellerPanelViewModel
    {
        public List<ProductViewModel> Products { get; set; }
        public StoreViewModel Store { get; set; }
        public List<StockViewModel> Stocks { get; set; }
    }
}
