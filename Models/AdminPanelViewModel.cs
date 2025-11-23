using System.Collections.Generic;

namespace Kursovoi.Models
{
    public class AdminPanelViewModel
    {
        public List<ProductViewModel> Products { get; set; } = new List<ProductViewModel>();
        public List<StoreViewModel> Stores { get; set; } = new List<StoreViewModel>();
        public List<AdminUserViewModel> Users { get; set; } = new List<AdminUserViewModel>();
        public List<CategoryViewModel> Categories { get; set; } = new List<CategoryViewModel>();
        public List<ReviewViewModel> Reviews { get; set; } = new List<ReviewViewModel>();
        public List<OrderViewModel> Orders { get; set; } = new List<OrderViewModel>();
    }

    public class AdminUserViewModel
    {
        public int UserID { get; set; }
        public string Login { get; set; }
        public string RoleName { get; set; }
        public bool IsActive { get; set; }
        public string Phone { get; set; }
    }

    public class ReviewViewModel
    {
        public int ReviewId { get; set; }
        public int UserId { get; set; }
        public string Login { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public int Rating { get; set; }
        public string CreatedAt { get; set; }
        public bool IsApproved { get; set; }
    }

    public class OrderViewModel
    {
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public string CreatedAt { get; set; }
        public string Status { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; }
        public string DeliveryAddress { get; set; }
        public int StoreId { get; set; }
    }
}
