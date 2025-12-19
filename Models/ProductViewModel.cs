using System;

namespace Kursovoi.Models
{
    public class ProductViewModel
    {
        public int ProductID { get; set; }
        // Compatibility alias for views that expect ProductId (camelCase)
        public int ProductId { get => ProductID; set => ProductID = value; }

        public string ProductName { get; set; } = string.Empty;

        // If view expects CategoryId/ManufacturerId during edit, expose these IDs alongside names.
        // The original model had CategoryName/ManufacturerName; keep them and add ID fields.
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        public int ManufacturerId { get; set; }
        public string ManufacturerName { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Discount { get; set; }
        public int Quantity { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public int StoreID { get; set; }
        // Compatibility alias for StoreId
        public int StoreId { get => StoreID; set => StoreID = value; }
        // Status column: true = frozen, false = active
        public bool Status { get; set; }
        // Stock last update timestamp (from Stocks.LastUpdate)
        public DateTime StockUpdatedAt { get; set; }
    }
}