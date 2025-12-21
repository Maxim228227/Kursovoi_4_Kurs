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

    // Analytics DTOs used by DbHelperClient and SellerController
    public class OrderDto
    {
        public int OrderID { get; set; }
        public int UserID { get; set; }
        public int ProductID { get; set; }
        public System.DateTime CreatedAt { get; set; }
        public string Status { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; }
        public string DeliveryAddress { get; set; }
        public int StoreID { get; set; }
        public string UserLogin { get; set; }
        public string ProductName { get; set; }
    }

    public class TopProductDto
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; }
        public int QuantitySold { get; set; }
        public decimal Revenue { get; set; }
    }

    public class AbandonedBasketDto
    {
        public int UserID { get; set; }
        public string UserLogin { get; set; }
        public int ProductCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public class ReviewDto
    {
        public int ReviewID { get; set; }
        public int UserID { get; set; }
        public string UserLogin { get; set; }
        public string Title { get; set; }
        public string ReviewText { get; set; }
        public int Rating { get; set; }
        public System.DateTime CreatedAt { get; set; }
        public bool IsApproved { get; set; }
    }
}