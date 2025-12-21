using System.Collections.Generic;

namespace Kursovoi.Models
{
    public class SellerAnalyticsViewModel
    {
        // Stock Analytics
        public List<StockAnalyticsItem> RunningOutProducts { get; set; } = new List<StockAnalyticsItem>();
        public List<StockAnalyticsItem> OutOfStockProducts { get; set; } = new List<StockAnalyticsItem>();
        public List<StockUpdateTimeline> StockUpdateTimeline { get; set; } = new List<StockUpdateTimeline>();

        // Order Analytics
        public List<OrderPeriodData> OrdersByDay { get; set; } = new List<OrderPeriodData>();
        public List<OrderPeriodData> OrdersByWeek { get; set; } = new List<OrderPeriodData>();
        public List<OrderPeriodData> OrdersByMonth { get; set; } = new List<OrderPeriodData>();
        public decimal AverageOrderAmount { get; set; }
        public List<OrderStatusDistribution> OrderStatusDistribution { get; set; } = new List<OrderStatusDistribution>();

        // Top Selling Products
        public List<TopProductItem> TopSellingProducts { get; set; } = new List<TopProductItem>();

        // Reviews Analytics
        public List<ReviewAnalyticsItem> ReviewsData { get; set; } = new List<ReviewAnalyticsItem>();
        public decimal AverageRating { get; set; }
        public int TotalReviews { get; set; }

        // Basket Analytics
        public List<BasketAnalyticsItem> AbandonedBaskets { get; set; } = new List<BasketAnalyticsItem>();
        public int TotalAbandonedBaskets { get; set; }

        // Product Activity
        public List<ProductActivityItem> NewProducts { get; set; } = new List<ProductActivityItem>();
        public List<ProductActivityItem> InactiveProducts { get; set; } = new List<ProductActivityItem>();

        public int StoreId { get; set; }

        // Diagnostics: debug info about DB/UDP calls
        public string Diagnostics { get; set; } = string.Empty;
    }

    public class StockAnalyticsItem
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int Threshold { get; set; } = 10;
        public System.DateTime LastUpdate { get; set; }
    }

    public class StockUpdateTimeline
    {
        public string Date { get; set; } = string.Empty;
        public int UpdateCount { get; set; }
    }

    public class OrderPeriodData
    {
        public string Period { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public class OrderStatusDistribution
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Percentage { get; set; }
    }

    public class TopProductItem
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int QuantitySold { get; set; }
        public decimal Revenue { get; set; }
    }

    public class ReviewAnalyticsItem
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int ReviewCount { get; set; }
        public decimal AverageRating { get; set; }
    }

    public class BasketAnalyticsItem
    {
        public int UserID { get; set; }
        public string UserLogin { get; set; } = string.Empty;
        public int ProductCount { get; set; }
        public decimal TotalAmount { get; set; }
        public System.DateTime LastActivity { get; set; }
    }

    public class ProductActivityItem
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public System.DateTime CreatedAt { get; set; }
        public System.DateTime LastOrderDate { get; set; }
        public int DaysSinceLastOrder { get; set; }
    }
}

