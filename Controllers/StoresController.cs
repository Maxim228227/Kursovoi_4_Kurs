using Kursovoi.Models;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Kursovoi.Controllers
{
    public class StoresController : Controller
    {
        public IActionResult Index(string q = null)
        {
            var list = new List<StoreViewModel>();
            try
            {
                var response = UdpClientHelper.SendUdpMessage("getallstores");
                if (!string.IsNullOrEmpty(response))
                {
                    var lines = response.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 6) continue;

                        var vm = new StoreViewModel
                        {
                            StoreID = int.TryParse(parts[0], out var id) ? id : 0,
                            StoreName = parts[1],
                            Address = parts[2],
                            City = parts[3],
                            Phone = parts[4],
                            LegalPerson = parts[5]
                        };
                        list.Add(vm);
                    }
                }
            }
            catch
            {
                // ignore
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qq = q.Trim();
                list = list.Where(s => (s.StoreName ?? string.Empty).Contains(qq, System.StringComparison.OrdinalIgnoreCase)
                    || (s.Address ?? string.Empty).Contains(qq, System.StringComparison.OrdinalIgnoreCase)
                    || (s.City ?? string.Empty).Contains(qq, System.StringComparison.OrdinalIgnoreCase)).ToList();
                ViewBag.SearchQuery = q;
            }

            return View(list);
        }

        public IActionResult Details(int id)
        {
            // find store name by id
            string storeName = null;
            try
            {
                var resp = UdpClientHelper.SendUdpMessage("getallstores");
                if (!string.IsNullOrEmpty(resp))
                {
                    var lines = resp.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 2) continue;
                        if (int.TryParse(parts[0], out var sid) && sid == id)
                        {
                            storeName = parts[1];
                            break;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            if (string.IsNullOrEmpty(storeName))
            {
                return NotFound();
            }

            // fetch all products and filter by store name
            var products = new List<ProductViewModel>();
            try
            {
                var response = UdpClientHelper.SendUdpMessage("getproducts");
                if (!string.IsNullOrEmpty(response))
                {
                    var lines = response.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 17) continue;
                        try
                        {
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

                            if (string.Equals(vm.StoreName ?? string.Empty, storeName, System.StringComparison.OrdinalIgnoreCase))
                            {
                                products.Add(vm);
                            }
                        }
                        catch
                        {
                            // ignore malformed lines
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            ViewBag.StoreName = storeName;
            return View(products);
        }
    }
}
