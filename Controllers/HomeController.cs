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
                list = list.Where(p => p.Price >= minVal).ToList();
            }

            if (!string.IsNullOrWhiteSpace(max) && decimal.TryParse(max, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxVal))
            {
                list = list.Where(p => p.Price <= maxVal).ToList();
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
                        list = list.OrderBy(p => p.Price).ToList();
                        break;
                    case "price_desc":
                        list = list.OrderByDescending(p => p.Price).ToList();
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
                    if (parts.Length < 17) continue;
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
                            ImageUrl = imageUrl
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
