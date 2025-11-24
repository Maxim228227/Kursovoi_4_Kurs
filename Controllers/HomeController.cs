using Kursovoi.Models; // подключаем класс UdpClientHelper
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace Kursovoi.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet]
        [ActionName("Index")]
        public IActionResult IndexGet(string q = null, string[] cat = null, string[] brand = null, string min = null, string max = null, string instock = null, string sort = null)
        {
            // get the full, unfiltered product list (used to populate filter dropdowns)
            var allProducts = GetProductsFromServer();
            var list = allProducts.ToList();

            // expose the full list to the view so filter controls always show all parameters
            ViewBag.AllProducts = allProducts;

            // helper to compute effective price after discount
            decimal EffectivePrice(ProductViewModel p) => p.Price * (1 - (p.Discount / 100m));

            // normalize incoming string filters
            q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            if (cat != null && cat.Length == 0) cat = null;
            if (brand != null && brand.Length == 0) brand = null;
            min = string.IsNullOrWhiteSpace(min) ? null : min.Trim();
            max = string.IsNullOrWhiteSpace(max) ? null : max.Trim();
            instock = string.IsNullOrWhiteSpace(instock) ? null : instock.Trim().ToLowerInvariant();
            sort = string.IsNullOrWhiteSpace(sort) ? null : sort.Trim();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qq = q;
                list = list.Where(p =>
                    (p.ProductName ?? string.Empty).Contains(qq, StringComparison.OrdinalIgnoreCase)
                    || (p.Description ?? string.Empty).Contains(qq, StringComparison.OrdinalIgnoreCase)
                    || (p.CategoryName ?? string.Empty).Contains(qq, StringComparison.OrdinalIgnoreCase)
                    || (p.ManufacturerName ?? string.Empty).Contains(qq, StringComparison.OrdinalIgnoreCase)
                ).ToList();
                ViewBag.SearchQuery = q;
            }

            if (cat != null && cat.Length > 0)
            {
                var trimmed = cat.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                if (trimmed.Length > 0)
                {
                    list = list.Where(p => trimmed.Any(tc => string.Equals((p.CategoryName ?? string.Empty).Trim(), tc, StringComparison.OrdinalIgnoreCase))).ToList();
                }
            }

            if (brand != null && brand.Length > 0)
            {
                var trimmed = brand.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
                if (trimmed.Length > 0)
                {
                    list = list.Where(p => trimmed.Any(tb => string.Equals((p.ManufacturerName ?? string.Empty).Trim(), tb, StringComparison.OrdinalIgnoreCase))).ToList();
                }
            }

            // helper to parse decimals from various cultures/user input
            static bool TryParseDecimalFlexible(string s, out decimal result)
            {
                result = 0m;
                if (string.IsNullOrWhiteSpace(s)) return false;
                s = s.Trim();
                // try invariant first (expects dot)
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result)) return true;
                // try current culture (may accept comma)
                if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out result)) return true;
                // replace comma with dot and try invariant again
                var s2 = s.Replace(',', '.');
                if (decimal.TryParse(s2, NumberStyles.Any, CultureInfo.InvariantCulture, out result)) return true;
                // replace dot with comma and try current culture
                var s3 = s.Replace('.', ',');
                if (decimal.TryParse(s3, NumberStyles.Any, CultureInfo.CurrentCulture, out result)) return true;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(min) && TryParseDecimalFlexible(min, out var minVal))
            {
                list = list.Where(p => EffectivePrice(p) >= minVal).ToList();
            }

            if (!string.IsNullOrWhiteSpace(max) && TryParseDecimalFlexible(max, out var maxVal))
            {
                list = list.Where(p => EffectivePrice(p) <= maxVal).ToList();
            }

            if (!string.IsNullOrWhiteSpace(instock) && (instock == "on" || instock == "true" || instock == "1"))
            {
                list = list.Where(p => p.Quantity > 0).ToList();
            }

            if (!string.IsNullOrWhiteSpace(sort))
            {
                switch (sort)
                {
                    case "price_asc":
                        list = list.OrderBy(p => EffectivePrice(p)).ToList();
                        break;
                    case "price_desc":
                        list = list.OrderByDescending(p => EffectivePrice(p)).ToList();
                        break;
                    case "name_asc":
                        list = list.OrderBy(p => p.ProductName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "name_desc":
                        list = list.OrderByDescending(p => p.ProductName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "rating_desc":
                        // rating unavailable; fallback to quantity desc
                        list = list.OrderByDescending(p => p.Quantity).ToList();
                        break;
                }
            }

            return View(list);
        }

        [HttpPost]
        public IActionResult Index(string userMessage)
        {
            if (!string.IsNullOrEmpty(userMessage))
            {
                string response = UdpClientHelper.SendUdpMessage(userMessage);
                ViewBag.ServerResponse = response;
            }

            var list = GetProductsFromServer();
            return View(list);
        }

        public IActionResult Catalog()
        {
            var list = GetProductsFromServer();

            // build categories using first product image as category image
            var categories = list
                .GroupBy(p => p.CategoryName ?? string.Empty)
                .Select(g => new CategoryViewModel
                {
                    CategoryName = g.Key,
                    ImageUrl = g.FirstOrDefault(p => !string.IsNullOrEmpty(p.ImageUrl))?.ImageUrl ?? "/Images/placeholder.svg",
                    Count = g.Count()
                })
                .ToList();

            // take top 5 products by discount percent; effective price will be shown in the view
            var topDiscounted = list
                .Where(p => p.Discount > 0)
                .OrderByDescending(p => p.Discount)
                .ThenByDescending(p => p.Price)
                .Take(5)
                .ToList();

            var vm = new CatalogViewModel
            {
                Categories = categories,
                TopDiscounted = topDiscounted
            };

            return View(vm);
        }

        public IActionResult Details(int id)
        {
            var list = GetProductsFromServer();
            var product = list.FirstOrDefault(p => p.ProductID == id);
            if (product == null) return NotFound();

            // Load reviews for this product from server
            try
            {
                var resp = UdpClientHelper.SendUdpMessage($"getproductreviews|{id}");
                var reviews = new List<dynamic>();
                decimal avg = 0m; int cnt = 0;
                if (!string.IsNullOrEmpty(resp))
                {
                    var lines = resp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        // reviewId|userId|login|title|text|rating|createdAt|isApproved
                        if (parts.Length < 8) continue;
                        if (!int.TryParse(parts[5], out var rating)) rating = 0;
                        reviews.Add(new {
                            ReviewId = parts[0],
                            UserId = parts[1],
                            Login = parts[2],
                            Title = parts[3],
                            Text = parts[4],
                            Rating = rating,
                            CreatedAt = parts[6]
                        });
                        avg += rating; cnt++;
                    }
                }
                ViewBag.Reviews = reviews;
                ViewBag.ReviewCount = cnt;
                ViewBag.AverageRating = cnt > 0 ? (avg / cnt) : 0m;
            }
            catch
            {
                ViewBag.Reviews = new List<dynamic>();
                ViewBag.ReviewCount = 0;
                ViewBag.AverageRating = 0m;
            }

            return View(product);
        }

        [HttpGet]
        public IActionResult GetProductReviews(int productId)
        {
            try
            {
                var resp = UdpClientHelper.SendUdpMessage($"getproductreviews|{productId}");
                var list = new List<object>();
                if (!string.IsNullOrEmpty(resp))
                {
                    var lines = resp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var p = line.Split('|');
                        if (p.Length < 8) continue;
                        list.Add(new { reviewId = p[0], userId = p[1], login = p[2], title = p[3], text = p[4], rating = p[5], createdAt = p[6] });
                    }
                }
                return Json(list);
            }
            catch
            {
                return Json(new object[0]);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddReview(int productId, string title, string reviewText, int rating)
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = false, redirect = Url.Action("Login", "Account", new { returnUrl = Url.Action("Details", "Home", new { id = productId }) }) });
                return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Details", "Home", new { id = productId }) });
            }

            if (rating < 1 || rating > 5) ModelState.AddModelError(string.Empty, "Рейтинг должен быть от 1 до 5");
            if (string.IsNullOrWhiteSpace(title)) ModelState.AddModelError(string.Empty, "Введите заголовок");
            if (string.IsNullOrWhiteSpace(reviewText)) ModelState.AddModelError(string.Empty, "Введите текст отзыва");

            if (!ModelState.IsValid)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToArray() });
                return RedirectToAction("Details", new { id = productId });
            }

            int userId = GetCurrentUserId();
            if (userId <= 0) {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = false, redirect = Url.Action("Login", "Account", new { returnUrl = Url.Action("Details", "Home", new { id = productId }) }) });
                return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Details", "Home", new { id = productId }) });
            }

            var safeTitle = (title ?? string.Empty).Replace('|', ' ');
            var safeText = (reviewText ?? string.Empty).Replace('|', ' ');

            try
            {
                var cmd = $"addreview|{userId}|{productId}|{safeTitle}|{safeText}|{rating}";
                var resp = UdpClientHelper.SendUdpMessage(cmd);
                var ok = (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK");
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = ok });
                TempData["AuthMessage"] = ok ? "Отзыв отправлен" : "Не удалось добавить отзыв";
            }
            catch
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = false });
                TempData["AuthMessage"] = "Ошибка при отправке отзыва";
            }

            return RedirectToAction("Details", new { id = productId });
        }

        private int GetCurrentUserId()
        {
            var name = User?.Identity?.Name ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return 0;
            try
            {
                var resp = UdpClientHelper.SendUdpMessage("getusers");
                if (string.IsNullOrEmpty(resp)) return 0;
                var lines = resp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length < 2) continue;
                    if (int.TryParse(parts[0], out var id) && string.Equals(parts[1].Trim(), name, StringComparison.OrdinalIgnoreCase)) return id;
                }
            }
            catch { }
            return 0;
        }

        // New overloaded GetProductsFromServer that can include inactive products when requested (admin)
        private List<ProductViewModel> GetProductsFromServer(bool includeInactive)
        {
            var list = new List<ProductViewModel>();
            try
            {
                var response = UdpClientHelper.SendUdpMessage("getproducts");
                if (string.IsNullOrEmpty(response)) return list;

                var lines = response.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length < 18) continue;
                    try
                    {
                        // convert server image path to web URL
                        var rawImagePath = parts[16];
                        var fileName = Path.GetFileName(rawImagePath ?? string.Empty);
                        var physicalPath = Path.Combine(Directory.GetCurrentDirectory(), "Images", fileName ?? string.Empty);
                        string imageUrl;
                        if (!string.IsNullOrEmpty(fileName) && System.IO.File.Exists(physicalPath))
                        {
                            imageUrl = "/Images/" + fileName;
                        }
                        else
                        {
                            imageUrl = "/Images/placeholder.svg";
                        }

                        var vm = new ProductViewModel
                        {
                            ProductID = int.Parse(parts[0]),
                            ProductName = parts[1],
                            CategoryName = parts[2],
                            ManufacturerName = parts[3],
                            Country = parts[4],
                            Description = parts[5],
                            IsActive = bool.Parse(parts[6]),
                            CreatedAt = DateTime.TryParse(parts[7], CultureInfo.InvariantCulture, DateTimeStyles.None, out var cAt) ? cAt : DateTime.MinValue,
                            UpdatedAt = DateTime.TryParse(parts[8], CultureInfo.InvariantCulture, DateTimeStyles.None, out var uAt) ? uAt : DateTime.MinValue,
                            StoreName = parts[9],
                            Address = parts[10],
                            City = parts[11],
                            Phone = parts[12],
                            Price = decimal.TryParse(parts[13], NumberStyles.Any, CultureInfo.InvariantCulture, out var pr) ? pr : 0m,
                            Discount = decimal.TryParse(parts[14], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m,
                            Quantity = int.TryParse(parts[15], out var qn) ? qn : 0,
                            ImageUrl = imageUrl,
                            StoreID = int.TryParse(parts[17], out var sid) ? sid : 0,
                            // Status: if present (server may return an extra column), treat "1" or "true" as ACTIVE
                            Status = (parts.Length > 18) && (parts[18] == "1" || parts[18].Equals("true", System.StringComparison.OrdinalIgnoreCase))
                        };

                        // only expose ACTIVE products in public lists unless includeInactive is true
                        if (vm.Status || includeInactive)
                        {
                            list.Add(vm);
                        }
                    }
                    catch
                    {
                        // ignore malformed lines
                    }
                }
            }
            catch
            {
                // ignore communication errors
            }

            return list;
        }

        // existing parameterless wrapper keeps behavior for public views
        private List<ProductViewModel> GetProductsFromServer()
        {
            return GetProductsFromServer(false);
        }

        public IActionResult ProductsFromServer()
        {
            var list = GetProductsFromServer();
            return View(list);
        }

        [HttpGet("Home/CategoryProducts/{*category}")]
        public IActionResult CategoryProductsPath(string category)
        {
            return CategoryProducts(category);
        }

        public IActionResult CategoryProducts(string category)
        {
            // Admins should see inactive products too
            var includeInactive = User?.Identity?.IsAuthenticated == true && User.IsInRole("admin");
            var all = GetProductsFromServer(includeInactive);

            // normalize incoming category: handle url encoding and trim
            if (!string.IsNullOrEmpty(category))
            {
                try { category = WebUtility.UrlDecode(category).Trim(); } catch { category = category.Trim(); }
            }

            ViewBag.Category = category ?? string.Empty;

            // prepare diagnostics
            ViewBag.AllCount = all.Count;
            ViewBag.AllCategories = all.Select(p => (p.CategoryName ?? string.Empty).Trim()).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();

            if (string.IsNullOrWhiteSpace(category))
            {
                ViewBag.FilteredCount = 0;
                return View("CategoryProducts", new List<ProductViewModel>());
            }

            string Normalize(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                var t = s.Replace('\u00A0', ' ');
                t = Regex.Replace(t, "\\s+", " ").Trim();
                return t.ToLowerInvariant();
            }

            var catNorm = Normalize(category);

            // 1) try exact normalized equality
            var filtered = all.Where(p => Normalize(p.CategoryName) == catNorm).ToList();

            // 2) if none, try normalized contains
            if (filtered.Count == 0)
            {
                filtered = all.Where(p => Normalize(p.CategoryName).Contains(catNorm)).ToList();
            }

            // 3) if still none, try simple case-insensitive contains on original names
            if (filtered.Count == 0)
            {
                filtered = all.Where(p => !string.IsNullOrEmpty(p.CategoryName) && p.CategoryName.IndexOf(category, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            ViewBag.FilteredCount = filtered.Count;

            return View("CategoryProducts", filtered);
        }
    }
}
