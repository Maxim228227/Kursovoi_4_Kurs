using Kursovoi.Models; // подключаем класс UdpClientHelper
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Kursovoi.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet]
        [ActionName("Index")]
        public IActionResult IndexGet(string q = null, string cat = null, string brand = null, string min = null, string max = null, string instock = null, string sort = null)
        {
            var list = GetProductsFromServer();

            // helper to compute effective price after discount
            decimal EffectivePrice(ProductViewModel p) => p.Price * (1 - (p.Discount / 100m));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qq = q.Trim();
                list = list.Where(p =>
                    (p.ProductName ?? string.Empty).Contains(qq, StringComparison.OrdinalIgnoreCase)
                    || (p.Description ?? string.Empty).Contains(qq, StringComparison.OrdinalIgnoreCase)
                    || (p.CategoryName ?? string.Empty).Contains(qq, StringComparison.OrdinalIgnoreCase)
                    || (p.ManufacturerName ?? string.Empty).Contains(qq, StringComparison.OrdinalIgnoreCase)
                ).ToList();
                ViewBag.SearchQuery = q;
            }

            if (!string.IsNullOrWhiteSpace(cat))
            {
                list = list.Where(p => string.Equals(p.CategoryName ?? string.Empty, cat.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(brand))
            {
                list = list.Where(p => string.Equals(p.ManufacturerName ?? string.Empty, brand.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(min) && decimal.TryParse(min, NumberStyles.Any, CultureInfo.InvariantCulture, out var minVal))
            {
                list = list.Where(p => EffectivePrice(p) >= minVal).ToList();
            }

            if (!string.IsNullOrWhiteSpace(max) && decimal.TryParse(max, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxVal))
            {
                list = list.Where(p => EffectivePrice(p) <= maxVal).ToList();
            }

            if (!string.IsNullOrWhiteSpace(instock) && instock == "on")
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
                        list = list.OrderBy(p => p.ProductName).ToList();
                        break;
                    case "name_desc":
                        list = list.OrderByDescending(p => p.ProductName).ToList();
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

        private List<ProductViewModel> GetProductsFromServer()
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
                            StoreID = int.TryParse(parts[17], out var sid) ? sid : 0
                        };
                        list.Add(vm);
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

        public IActionResult ProductsFromServer()
        {
            var list = GetProductsFromServer();
            return View(list);
        }
    }
}
