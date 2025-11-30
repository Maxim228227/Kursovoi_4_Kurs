using System.Collections.Generic;

namespace Kursovoi.Models
{
    public class AdminAnalyticsViewModel
    {
        // Store Performance
        public List<StorePerformanceItem> StorePerformance { get; set; } = new List<StorePerformanceItem>();
        public List<StoreRegistrationTimeline> StoreRegistrationTimeline { get; set; } = new List<StoreRegistrationTimeline>();

        // Category Analytics
        public List<CategoryAnalyticsItem> CategoryAnalytics { get; set; } = new List<CategoryAnalyticsItem>();

        // Global Sales Dynamics
        public List<SalesPeriodData> SalesByMonth { get; set; } = new List<SalesPeriodData>();
        public decimal TotalTurnover { get; set; }
        public decimal CancelledOrdersPercentage { get; set; }
        public int TotalOrders { get; set; }
        public int CancelledOrders { get; set; }

        // User Analytics
        public List<UserAnalyticsItem> UserAnalytics { get; set; } = new List<UserAnalyticsItem>();
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }

        // Product Performance
        public List<ProductPerformanceItem> BestProducts { get; set; } = new List<ProductPerformanceItem>();
        public List<ProductPerformanceItem> WorstProducts { get; set; } = new List<ProductPerformanceItem>();

        // Review Analytics
        public List<GlobalReviewAnalyticsItem> ReviewAnalytics { get; set; } = new List<GlobalReviewAnalyticsItem>();
        public decimal GlobalAverageRating { get; set; }

        // Price & Discount Analytics
        public List<PriceAnalyticsItem> PriceAnalytics { get; set; } = new List<PriceAnalyticsItem>();
        public decimal AveragePrice { get; set; }
        public decimal AverageDiscount { get; set; }

        // Summary Statistics
        public int TotalProducts { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class StorePerformanceItem
    {
        public int StoreID { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public decimal TotalSales { get; set; }
        public decimal AverageOrderValue { get; set; }
        public int OrderCount { get; set; }
        public string RegistrationDate { get; set; } = string.Empty;
    }

    public class StoreRegistrationTimeline
    {
        public string Date { get; set; } = string.Empty;
        public int StoreCount { get; set; }
    }

    public class CategoryAnalyticsItem
    {
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int ProductCount { get; set; }
        public int OrderCount { get; set; }
        public decimal Revenue { get; set; }
    }

    public class SalesPeriodData
    {
        public string Month { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public class UserAnalyticsItem
    {
        public int UserID { get; set; }
        public string Login { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal TotalSpent { get; set; }
        public bool IsActive { get; set; }
    }

    public class ProductPerformanceItem
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal AverageRating { get; set; }
    }

    public class GlobalReviewAnalyticsItem
    {
        public int ProductID { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int ReviewCount { get; set; }
        public decimal AverageRating { get; set; }
    }

    public class PriceAnalyticsItem
    {
        public string PriceRange { get; set; } = string.Empty;
        public int ProductCount { get; set; }
        public decimal AverageDiscount { get; set; }
    }
}

