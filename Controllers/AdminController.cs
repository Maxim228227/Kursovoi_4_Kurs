using Kursovoi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System;

namespace Kursovoi.Controllers
{
    [Authorize(Roles = "admin")]
    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            var vm = new AdminPanelViewModel();

            // fetch products
            var prodResp = Models.UdpClientHelper.SendUdpMessage("getproducts");
            vm.Products = ParseProducts(prodResp);

            // fetch stores
            var storesResp = Models.UdpClientHelper.SendUdpMessage("getallstores");
            vm.Stores = ParseStores(storesResp);

            // fetch users
            var usersResp = Models.UdpClientHelper.SendUdpMessage("getusers");
            vm.Users = ParseUsers(usersResp);

            // fetch categories
            var catsResp = Models.UdpClientHelper.SendUdpMessage("getallcategories");
            vm.Categories = ParseCategories(catsResp);

            // fetch reviews for all products (admin should see all reviews)
            var reviews = new List<ReviewViewModel>();
            foreach (var p in vm.Products)
            {
                var rresp = Models.UdpClientHelper.SendUdpMessage($"getproductreviewsall|{p.ProductID}");
                reviews.AddRange(ParseReviews(rresp, p));
            }
            vm.Reviews = reviews;

            // fetch orders: use getuserorders for every user and aggregate
            var orders = new List<OrderViewModel>();
            foreach (var u in vm.Users)
            {
                var or = Models.UdpClientHelper.SendUdpMessage($"getuserorders|{u.UserID}");
                orders.AddRange(ParseOrders(or, u));
            }

            // fill product names from products list
            foreach (var o in orders)
            {
                var prod = vm.Products.FirstOrDefault(x => x.ProductID == o.ProductId);
                if (prod != null) o.ProductName = prod.ProductName;
            }

            vm.Orders = orders;

            return View(vm);
        }

        [HttpGet]
        public IActionResult Analytics()
        {
            var vm = new AdminAnalyticsViewModel();

            // fetch base data
            var prodResp = UdpClientHelper.SendUdpMessage("getproducts");
            var products = ParseProducts(prodResp);

            var storesResp = UdpClientHelper.SendUdpMessage("getallstores");
            var stores = ParseStores(storesResp);

            var usersResp = UdpClientHelper.SendUdpMessage("getusers");
            var users = ParseUsers(usersResp);

            var catsResp = UdpClientHelper.SendUdpMessage("getallcategories");
            var categories = ParseCategories(catsResp);

            // gather orders (aggregate per user)
            var orders = new List<OrderViewModel>();
            foreach (var u in users)
            {
                var or = UdpClientHelper.SendUdpMessage($"getuserorders|{u.UserID}");
                orders.AddRange(ParseOrders(or, u));
            }

            // attach product names to orders
            foreach (var o in orders)
            {
                var prod = products.FirstOrDefault(p => p.ProductID == o.ProductId);
                if (prod != null) o.ProductName = prod.ProductName;
            }

            // gather reviews per product
            var reviews = new List<ReviewViewModel>();
            foreach (var p in products)
            {
                var rresp = UdpClientHelper.SendUdpMessage($"getproductreviewsall|{p.ProductID}");
                reviews.AddRange(ParseReviews(rresp, p));
            }

            // Summary statistics
            vm.TotalProducts = products.Count;
            vm.TotalOrders = orders.Count;
            vm.TotalRevenue = orders.Sum(o => o.TotalAmount);
            vm.TotalTurnover = vm.TotalRevenue;
            vm.CancelledOrders = orders.Count(o => !string.IsNullOrEmpty(o.Status) && o.Status.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0);
            vm.CancelledOrdersPercentage = vm.TotalOrders == 0 ? 0m : (decimal)vm.CancelledOrders * 100m / vm.TotalOrders;

            // Store performance
            foreach (var s in stores)
            {
                var so = orders.Where(o => o.StoreId == s.StoreID).ToList();
                var totalSales = so.Sum(o => o.TotalAmount);
                var orderCount = so.Count;
                vm.StorePerformance.Add(new StorePerformanceItem
                {
                    StoreID = s.StoreID,
                    StoreName = s.StoreName,
                    TotalSales = totalSales,
                    OrderCount = orderCount,
                    AverageOrderValue = orderCount > 0 ? totalSales / orderCount : 0m,
                    RegistrationDate = s.RegistrationDate ?? string.Empty
                });
            }

            // Category analytics
            foreach (var c in categories)
            {
                var prodForCat = products.Where(p => string.Equals(p.CategoryName?.Trim(), c.CategoryName?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                var prodIds = prodForCat.Select(p => p.ProductID).ToHashSet();
                var ordersForCat = orders.Where(o => prodIds.Contains(o.ProductId)).ToList();
                vm.CategoryAnalytics.Add(new CategoryAnalyticsItem
                {
                    CategoryID = c.CategoryID,
                    CategoryName = c.CategoryName,
                    ProductCount = prodForCat.Count,
                    OrderCount = ordersForCat.Count,
                    Revenue = ordersForCat.Sum(o => o.TotalAmount)
                });
            }

            // Sales by month
            var salesGroups = orders.Where(o => !string.IsNullOrEmpty(o.CreatedAt)).Select(o => new { Order = o, Date = TryParseDate(o.CreatedAt) })
                .Where(x => x.Date.HasValue)
                .GroupBy(x => new { x.Date.Value.Year, x.Date.Value.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);
            foreach (var g in salesGroups)
            {
                vm.SalesByMonth.Add(new SalesPeriodData { Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("yyyy-MM"), OrderCount = g.Count(), TotalAmount = g.Sum(x => x.Order.TotalAmount) });
            }

            // Users analytics
            vm.TotalUsers = users.Count;
            vm.ActiveUsers = users.Count(u => u.IsActive);
            foreach (var u in users)
            {
                var usOrders = orders.Where(o => o.UserId == u.UserID).ToList();
                vm.UserAnalytics.Add(new UserAnalyticsItem
                {
                    UserID = u.UserID,
                    Login = u.Login,
                    RoleName = u.RoleName,
                    OrderCount = usOrders.Count,
                    TotalSpent = usOrders.Sum(o => o.TotalAmount),
                    IsActive = u.IsActive
                });
            }

            // Product performance
            var prodOrderGroups = orders.GroupBy(o => o.ProductId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var p in products)
            {
                prodOrderGroups.TryGetValue(p.ProductID, out var ords);
                var qSold = ords?.Count ?? 0; // assume one order = one qty
                var revenue = ords?.Sum(o => o.TotalAmount) ?? 0m;
                var prodReviews = reviews.Where(r => r.ProductId == p.ProductID).ToList();
                decimal avgRating = 0m;
                if (prodReviews.Any()) avgRating = prodReviews.Average(r => (decimal)r.Rating);
                vm.BestProducts.Add(new ProductPerformanceItem { ProductID = p.ProductID, ProductName = p.ProductName, QuantitySold = qSold, Revenue = revenue, AverageRating = avgRating });
            }
            // sort best and worst
            vm.BestProducts = vm.BestProducts.OrderByDescending(x => x.QuantitySold).ThenByDescending(x => x.Revenue).Take(10).ToList();
            vm.WorstProducts = vm.BestProducts.OrderBy(x => x.QuantitySold).ThenBy(x => x.Revenue).Take(10).ToList();

            // Review analytics
            var revGroups = reviews.GroupBy(r => r.ProductId);
            foreach (var g in revGroups)
            {
                vm.ReviewAnalytics.Add(new GlobalReviewAnalyticsItem { ProductID = g.Key, ProductName = products.FirstOrDefault(p => p.ProductID == g.Key)?.ProductName ?? string.Empty, ReviewCount = g.Count(), AverageRating = (decimal)g.Average(r => r.Rating) });
            }
            vm.GlobalAverageRating = reviews.Any() ? (decimal)reviews.Average(r => r.Rating) : 0m;

            // Price & discount analytics
            vm.AveragePrice = products.Any() ? products.Average(p => p.Price) : 0m;
            vm.AverageDiscount = products.Any() ? products.Average(p => p.Discount) : 0m;

            // price buckets
            var buckets = new[] { (0m, 100m, "0-100"), (100m, 500m, "100-500"), (500m, 1000m, "500-1000"), (1000m, decimal.MaxValue, "1000+") };
            foreach (var b in buckets)
            {
                var list = products.Where(p => p.Price >= b.Item1 && p.Price < b.Item2).ToList();
                vm.PriceAnalytics.Add(new PriceAnalyticsItem { PriceRange = b.Item3, ProductCount = list.Count, AverageDiscount = list.Any() ? list.Average(p => p.Discount) : 0m });
            }

            // total revenue already set
            vm.TotalRevenue = vm.TotalRevenue;

            return View("Analytics", vm);
        }

        private static DateTime? TryParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParse(s, out var dt)) return dt;
            // try common formats
            var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd.MM.yyyy", "dd.MM.yyyy HH:mm:ss" };
            foreach (var f in formats) if (DateTime.TryParseExact(s, f, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) return dt;
            return null;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetOrderStatus(int orderId, string status)
        {
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage($"setorderstatus|{orderId}|{status}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK") return Json(new { success = true });
                return Json(new { success = false, message = resp });
            }
            catch (System.Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetProductStatus(int productId, string status)
        {
            try
            {
                // accept '1'/'0', 'true'/'false' etc.
                bool statusBool = false;
                if (!string.IsNullOrEmpty(status) && (status == "1" || status.Equals("true", System.StringComparison.OrdinalIgnoreCase))) statusBool = true;

                var resp = Models.UdpClientHelper.SendUdpMessage($"setproductstatus|{productId}|{(statusBool ? 1 : 0)}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK")
                {
                    return Json(new { success = true });
                }
                return Json(new { success = false, message = resp });
            }
            catch (System.Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetStoreStatus(int storeId, string status)
        {
            try
            {
                bool statusBool = false;
                if (!string.IsNullOrEmpty(status) && (status == "1" || status.Equals("true", System.StringComparison.OrdinalIgnoreCase))) statusBool = true;

                var resp = Models.UdpClientHelper.SendUdpMessage($"setstorestatus|{storeId}|{(statusBool ? 1 : 0)}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK")
                {
                    return Json(new { success = true });
                }
                return Json(new { success = false, message = resp });
            }
            catch (System.Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetUserActive(int userId, string status)
        {
            try
            {
                bool statusBool = false;
                if (!string.IsNullOrEmpty(status) && (status == "1" || status.Equals("true", System.StringComparison.OrdinalIgnoreCase))) statusBool = true;

                var resp = Models.UdpClientHelper.SendUdpMessage($"setuseractive|{userId}|{(statusBool ? "true" : "false")}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK")
                {
                    return Json(new { success = true });
                }
                return Json(new { success = false, message = resp });
            }
            catch (System.Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddUser(string login, string password, int roleId, string phone)
        {
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password)) return Json(new { success = false, message = "Не указан логин или пароль" });
            try
            {
                var hash = ComputeSha256Hash(password);
                var ph = string.IsNullOrWhiteSpace(phone) ? string.Empty : phone.Trim();
                var resp = Models.UdpClientHelper.SendUdpMessage($"register|{login.Trim()}|{hash}|{roleId}|{ph}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK") return Json(new { success = true });
                return Json(new { success = false, message = resp });
            }
            catch (System.Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetReviewApproval(int reviewId, string approve)
        {
            try
            {
                bool approveBool = !string.IsNullOrEmpty(approve) && (approve == "1" || approve.Equals("true", System.StringComparison.OrdinalIgnoreCase));
                var resp = Models.UdpClientHelper.SendUdpMessage($"setreviewapproval|{reviewId}|{(approveBool ? "true" : "false")} ");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK") return Json(new { success = true });
                return Json(new { success = false, message = resp });
            }
            catch (System.Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // Category management endpoints
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return Json(new { success = false, message = "Name required" });
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage($"addcategory|{categoryName.Replace('|', ' ')}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK") return Json(new { success = true });
                return Json(new { success = false, message = resp });
            }
            catch (System.Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateCategory(int categoryId, string categoryName)
        {
            if (categoryId <= 0 || string.IsNullOrWhiteSpace(categoryName)) return Json(new { success = false, message = "Bad parameters" });
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage($"updatecategory|{categoryId}|{categoryName.Replace('|', ' ')}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK") return Json(new { success = true });
                return Json(new { success = false, message = resp });
            }
            catch (System.Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteCategory(int categoryId)
        {
            if (categoryId <= 0) return Json(new { success = false, message = "Bad id" });
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage($"deletecategory|{categoryId}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK") return Json(new { success = true });
                return Json(new { success = false, message = resp });
            }
            catch (System.Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddStore(string login, string password, string phone, string storeName, string address, string city, string legalPerson)
        {
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storeName))
                return Json(new { success = false, message = "Login, password and store name are required" });

            try
            {
                // 1) register seller account (roleId = 3)
                var hash = ComputeSha256Hash(password);
                var ph = string.IsNullOrWhiteSpace(phone) ? string.Empty : phone.Trim();
                var regCmd = $"register|{login.Trim().Replace('|',' ')}|{hash}|3|{ph}";
                var regResp = Models.UdpClientHelper.SendUdpMessage(regCmd);
                if (string.IsNullOrEmpty(regResp) || !regResp.Trim().Equals("OK", System.StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = regResp ?? "Register failed" });
                }

                // 2) add store (include login so server can link user to store)
                var sName = (storeName ?? string.Empty).Trim().Replace('|', ' ');
                var sAddr = (address ?? string.Empty).Trim().Replace('|', ' ');
                var sCity = (city ?? string.Empty).Trim().Replace('|', ' ');
                var sPhone = (phone ?? string.Empty).Trim().Replace('|', ' ');
                var sLegal = (legalPerson ?? string.Empty).Trim().Replace('|', ' ');

                var addStoreCmd = $"addstore|{login.Trim().Replace('|',' ')}|{sName}|{sAddr}|{sCity}|{sPhone}|{sLegal}";
                var storeResp = Models.UdpClientHelper.SendUdpMessage(addStoreCmd);
                if (string.IsNullOrEmpty(storeResp) || !storeResp.Trim().Equals("OK", System.StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = storeResp ?? "Add store failed" });
                }

                return Json(new { success = true });
            }
            catch (System.Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private static string ComputeSha256Hash(string rawData)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private List<ProductViewModel> ParseProducts(string resp)
        {
            var list = new List<ProductViewModel>();
            if (string.IsNullOrEmpty(resp)) return list;
            var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var p = l.Split('|');
                // now server returns 19 columns including Status at the end
                if (p.Length < 19) continue;
                if (!int.TryParse(p[0], out var id)) continue;
                var vm = new ProductViewModel
                {
                    ProductID = id,
                    ProductName = p[1],
                    CategoryName = p[2],
                    ManufacturerName = p[3],
                    Country = p[4],
                    Description = p[5],
                    IsActive = bool.TryParse(p[6], out var act) && act,
                    StoreName = p[9],
                    Address = p[10],
                    City = p[11],
                    Phone = p[12],
                    Price = decimal.TryParse(p[13], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pr) ? pr : 0m,
                    Discount = decimal.TryParse(p[14], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m,
                    Quantity = int.TryParse(p[15], out var q) ? q : 0,
                    ImageUrl = p[16],
                    StoreID = int.TryParse(p[17], out var sid) ? sid : 0,
                    Status = (p.Length > 18) && (p[18] == "1" || p[18].Equals("true", System.StringComparison.OrdinalIgnoreCase))
                };
                list.Add(vm);
            }
            return list;
        }

        private List<StoreViewModel> ParseStores(string resp)
        {
            var list = new List<StoreViewModel>();
            if (string.IsNullOrEmpty(resp)) return list;
            var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var p = l.Split('|');
                // expect: id|name|address|city|phone|legalPerson|status?
                if (p.Length < 6) continue;
                if (!int.TryParse(p[0], out var id)) continue;
                var store = new StoreViewModel { StoreID = id, StoreName = p[1], Address = p[2], City = p[3], Phone = p[4], LegalPerson = p[5] };
                if (p.Length > 6) store.Status = (p[6] == "1" || p[6].Equals("true", System.StringComparison.OrdinalIgnoreCase));
                list.Add(store);
            }
            return list;
        }

        private List<AdminUserViewModel> ParseUsers(string resp)
        {
            var list = new List<AdminUserViewModel>();
            if (string.IsNullOrEmpty(resp)) return list;
            var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var p = l.Split('|');
                if (p.Length < 6) continue;
                if (!int.TryParse(p[0], out var id)) continue;
                list.Add(new AdminUserViewModel { UserID = id, Login = p[1], RoleName = p[3], IsActive = bool.TryParse(p[4], out var a) && a, Phone = p[5] });
            }
            return list;
        }

        private List<ReviewViewModel> ParseReviews(string resp, ProductViewModel product)
        {
            var list = new List<ReviewViewModel>();
            if (string.IsNullOrEmpty(resp)) return list;
            var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var p = l.Split('|');
                if (p.Length < 8) continue;
                if (!int.TryParse(p[0], out var id)) continue;
                // p: ReviewID|UserID|Login|Title|ReviewText|Rating|CreatedAt|IsApproved
                list.Add(new ReviewViewModel { ReviewId = id, UserId = int.TryParse(p[1], out var uid) ? uid : 0, Login = p[2], Title = p[3], Text = p[4], Rating = int.TryParse(p[5], out var r) ? r : 0, CreatedAt = p[6], IsApproved = p.Length>7 && bool.TryParse(p[7], out var ap) && ap, ProductId = product.ProductID, ProductName = product.ProductName });
            }
            return list;
        }

        private List<CategoryViewModel> ParseCategories(string resp)
        {
            var list = new List<CategoryViewModel>();
            if (string.IsNullOrEmpty(resp)) return list;
            var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var p = l.Split('|');
                if (p.Length < 2) continue;
                if (!int.TryParse(p[0], out var id)) continue;
                list.Add(new CategoryViewModel { CategoryID = id, CategoryName = p[1].Trim(), ImageUrl = string.Empty, Count = 0 });
            }
            return list;
        }

        private List<ReviewViewModel> ParseReviews(string resp)
        {
            var list = new List<ReviewViewModel>();
            if (string.IsNullOrEmpty(resp)) return list;
            var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var p = l.Split('|');
                if (p.Length < 8) continue;
                if (!int.TryParse(p[0], out var id)) continue;
                list.Add(new ReviewViewModel { ReviewId = id, UserId = int.TryParse(p[1], out var uid) ? uid : 0, Login = p[2], Title = p[3], Text = p[4], Rating = int.TryParse(p[5], out var r) ? r : 0, CreatedAt = p[6], IsApproved = p.Length>7 && bool.TryParse(p[7], out var ap) && ap });
            }
            return list;
        }

        private List<OrderViewModel> ParseOrders(string resp, AdminUserViewModel user)
        {
            var list = new List<OrderViewModel>();
            if (string.IsNullOrEmpty(resp)) return list;
            var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var p = l.Split('|');
                if (p.Length < 8) continue;
                if (!int.TryParse(p[0], out var id)) continue;
                // orderId|productId|createdAt|status|totalAmount|payment|address|storeId
                var prodId = int.TryParse(p[1], out var pid) ? pid : 0;
                var prodName = "";
                // try to find product name from current product list
                // (will be filled by caller because admin controller has vm.Products)
                // to avoid circular reference, do a lookup by calling Udp getproducts? skip here, will fill later if needed
                list.Add(new OrderViewModel { OrderId = id, ProductId = prodId, ProductName = string.Empty, UserId = user.UserID, UserLogin = user.Login, CreatedAt = p[2], Status = p[3], TotalAmount = decimal.TryParse(p[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0m, PaymentMethod = p[5], DeliveryAddress = p[6], StoreId = int.TryParse(p[7], out var sid) ? sid : 0 });
            }
            return list;
        }
    }
}
