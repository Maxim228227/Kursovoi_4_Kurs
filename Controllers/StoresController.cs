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
                        if (parts.Length < 2) continue;

                        // status is expected to be the last column; handle both old (7 cols) and new (8 cols) formats
                        bool status = false;
                        var last = parts[parts.Length - 1];
                        if (string.Equals(last, "1", System.StringComparison.OrdinalIgnoreCase) || string.Equals(last, "true", System.StringComparison.OrdinalIgnoreCase)) status = true;
                        else if (string.Equals(last, "0", System.StringComparison.OrdinalIgnoreCase) || string.Equals(last, "false", System.StringComparison.OrdinalIgnoreCase)) status = false;
                        else bool.TryParse(last, out status);

                        // status == true means ACTIVE in DB; only show active stores to customers
                        if (!status) continue;

                        var vm = new StoreViewModel
                        {
                            StoreID = int.TryParse(parts[0], out var id) ? id : 0,
                            StoreName = parts.Length > 1 ? parts[1] : string.Empty,
                            Address = parts.Length > 2 ? parts[2] : string.Empty,
                            City = parts.Length > 3 ? parts[3] : string.Empty,
                            Phone = parts.Length > 4 ? parts[4] : string.Empty,
                            LegalPerson = parts.Length > 5 ? parts[5] : string.Empty,
                            Status = status
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
            // find store name by id and ensure store is active
            string storeName = null;
            int storeId = id;
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
                            bool status = true;
                            if (parts.Length > 6) bool.TryParse(parts[6], out status);
                            if (status) { storeName = null; break; } // store is frozen
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

            // fetch all products and filter by store ID or store name and only active products
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
                        // expect at least 19 parts including product status
                        if (parts.Length < 19) continue;
                        try
                        {
                            var isProductActive = true;
                            bool.TryParse(parts[18], out isProductActive);
                            if (!isProductActive) continue; // skip frozen products

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

                            // StoreID: если есть 20 столбцов, берем из 19-го, иначе ищем по имени магазина
                            int productStoreId = 0;
                            if (parts.Length > 19)
                                productStoreId = int.TryParse(parts[19], out var psid) ? psid : 0;

                            var vm = new ProductViewModel
                            {
                                ProductID = int.Parse(parts[0]),
                                ProductName = parts[1],
                                CategoryName = parts[2],
                                ManufacturerName = parts[3],
                                Country = parts[4],
                                Description = parts[5],
                                IsActive = bool.TryParse(parts[6], out var ia) && ia,
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

                            // Если StoreID совпадает или имя магазина совпадает
                            if ((productStoreId == storeId) || (string.Equals(vm.StoreName ?? string.Empty, storeName, System.StringComparison.OrdinalIgnoreCase)))
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
