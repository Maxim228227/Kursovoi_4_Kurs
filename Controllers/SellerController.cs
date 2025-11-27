using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Kursovoi.Models;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Kursovoi.Controllers
{
    [Authorize(Roles = "seller")]
    public class SellerController : Controller
    {
        // GET: /Seller/Index
        public IActionResult Index()
        {
            var vm = new SellerPanelViewModel
            {
                Products = new List<ProductViewModel>(),
                Store = new StoreViewModel(),
                Stocks = new List<StockViewModel>()
            };

            try
            {
                // fetch all products from server
                var prodResp = Models.UdpClientHelper.SendUdpMessage("getproducts");
                vm.Products = ParseProducts(prodResp);

                // fetch all stores
                var storesResp = Models.UdpClientHelper.SendUdpMessage("getallstores");
                var stores = ParseStores(storesResp);

                // attempt to find current seller's store by matching user's login -> user's StoreID (if available in getusers)
                int sellerStoreId = 0;
                try
                {
                    var usersResp = Models.UdpClientHelper.SendUdpMessage("getusers");
                    if (!string.IsNullOrEmpty(usersResp))
                    {
                        var lines = usersResp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                        foreach (var l in lines)
                        {
                            var parts = l.Split('|');
                            if (parts.Length < 2) continue;
                            if (string.Equals(parts[1].Trim(), User?.Identity?.Name ?? string.Empty, System.StringComparison.OrdinalIgnoreCase))
                            {
                                // try to read StoreID if server returns it as 7th column
                                if (parts.Length >= 7 && int.TryParse(parts[6], out var sid)) sellerStoreId = sid;
                                break;
                            }
                        }
                    }
                }
                catch { }

                // if we didn't get store id from users list try to infer from products where Login may match store linkage - fallback: if seller has any product, take its StoreID
                if (sellerStoreId == 0 && vm.Products.Any())
                {
                    var first = vm.Products.FirstOrDefault();
                    if (first != null) sellerStoreId = first.StoreID;
                }

                if (sellerStoreId != 0)
                {
                    var store = stores.FirstOrDefault(s => s.StoreID == sellerStoreId);
                    if (store != null) vm.Store = store;
                }

                // build stocks list for this store: show product name, quantity and last update
                var stocks = new List<StockViewModel>();
                foreach (var p in vm.Products.Where(p => sellerStoreId == 0 || p.StoreID == sellerStoreId))
                {
                    stocks.Add(new StockViewModel { ProductID = p.ProductID, Quantity = p.Quantity, ProductName = p.ProductName, UpdatedAt = p.StockUpdatedAt });
                }
                vm.Stocks = stocks;

                // filter products shown to seller (if we have store id)
                if (sellerStoreId != 0)
                {
                    vm.Products = vm.Products.Where(p => p.StoreID == sellerStoreId).ToList();
                }
            }
            catch
            {
                // ignore network errors; return empty model
            }

            // Set top-right store name for layout (fallback to default)
            ViewData["TopRightStoreName"] = string.IsNullOrEmpty(vm.Store?.StoreName) ? "МаркетPRO" : vm.Store.StoreName;

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetStock(int productId, int quantity)
        {
            if (productId <= 0) return BadRequest();
            var resp = Models.UdpClientHelper.SendUdpMessage($"setstock|{productId}|{quantity}");
            bool ok = string.Equals(resp?.Trim(), "OK", System.StringComparison.OrdinalIgnoreCase);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = ok, response = resp });
            }
            return RedirectToAction("Index");
        }

        // NEW ACTIONS AND HELPERS FOR ADDING PRODUCT

        [HttpGet]
        public IActionResult AddProduct()
        {
            var vm = new AddProductViewModel();
            // fetch categories and manufacturers to ViewData
            ViewData["Categories"] = GetCategories();
            ViewData["Manufacturers"] = GetManufacturers();
            // pass seller's store id and name so client can include it in form
            ViewData["StoreId"] = GetSellerStoreId();
            ViewData["StoreName"] = GetSellerStoreName();
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddProduct(AddProductViewModel model, IFormFile imageFile)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Categories"] = GetCategories();
                ViewData["Manufacturers"] = GetManufacturers();
                ViewData["StoreId"] = GetSellerStoreId();
                ViewData["StoreName"] = GetSellerStoreName();
                return View(model);
            }

            // prefer StoreId posted from client, fallback to server lookup
            int storeId = 0;
            if (Request.HasFormContentType && int.TryParse(Request.Form["StoreId"], out var fid)) storeId = fid;
            if (storeId == 0) storeId = GetSellerStoreId();

            // Handle uploaded image file
            if (imageFile != null && imageFile.Length > 0)
            {
                try
                {
                    var uploads = Path.Combine(Directory.GetCurrentDirectory(), "Images");
                    if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);

                    string storeName = GetSellerStoreName() ?? "store";
                    string safeStore = MakeSafeFileName(storeName);
                    string safeProd = MakeSafeFileName(model.ProductName);
                    string destName = safeStore + "_" + safeProd + ".jpg";
                    string destPath = Path.Combine(uploads, destName);
                    using (var fs = System.IO.File.Create(destPath))
                    {
                        imageFile.OpenReadStream().CopyTo(fs);
                    }
                    model.ImageUrl = "/Images/" + destName;
                }
                catch (System.Exception ex)
                {
                    // report file save error and return so user can act
                    ModelState.AddModelError(string.Empty, "Ошибка при сохранении файла изображения: " + ex.Message);
                    ViewData["Categories"] = GetCategories();
                    ViewData["Manufacturers"] = GetManufacturers();
                    ViewData["StoreId"] = storeId;
                    ViewData["StoreName"] = GetSellerStoreName();
                    return View(model);
                }
            }

            var cmd = $"addproduct|{User?.Identity?.Name ?? string.Empty}|{EscapePipe(model.ProductName)}|{model.CategoryId}|{model.ManufacturerId}|{EscapePipe(model.Description)}|{model.Price.ToString(CultureInfo.InvariantCulture)}|{model.Discount.ToString(CultureInfo.InvariantCulture)}|{model.Quantity}|{EscapePipe(model.ImageUrl)}|{storeId}";
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage(cmd);
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK")
                {
                    TempData["AddProductMsg"] = "Товар добавлен";
                    return RedirectToAction("Index");
                }

                // show server response as error
                ModelState.AddModelError(string.Empty, "Сервер: " + (resp ?? "(пустой ответ)"));
            }
            catch (System.Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Ошибка при отправке на сервер: " + ex.Message);
            }

            ViewData["Categories"] = GetCategories();
            ViewData["Manufacturers"] = GetManufacturers();
            ViewData["StoreId"] = storeId;
            ViewData["StoreName"] = GetSellerStoreName();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddProductFromCsv(AddProductViewModel model)
        {
            if (model == null) return Json(new { success = false });
            int storeId = 0;
            if (Request.HasFormContentType && int.TryParse(Request.Form["StoreId"], out var fid)) storeId = fid;
            if (storeId == 0) storeId = GetSellerStoreId();
            try
            {
                var cmd = $"addproduct|{User?.Identity?.Name ?? string.Empty}|{EscapePipe(model.ProductName)}|{model.CategoryId}|{model.ManufacturerId}|{EscapePipe(model.Description)}|{model.Price.ToString(CultureInfo.InvariantCulture)}|{model.Discount.ToString(CultureInfo.InvariantCulture)}|{model.Quantity}|{EscapePipe(model.ImageUrl)}|{storeId}";
                var resp = Models.UdpClientHelper.SendUdpMessage(cmd);
                var ok = !string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK";
                return Json(new { success = ok, response = resp });
            }
            catch { return Json(new { success = false }); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddProductUpload(IFormFile csvFile, IFormFileCollection images)
        {
            if (csvFile == null || csvFile.Length == 0) return RedirectToAction("AddProduct");

            // Save uploaded CSV to temporary uploads for later reporting
            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Uploads");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            var originalName = Path.GetFileName(csvFile.FileName);
            var savedName = Guid.NewGuid().ToString("N") + "_" + originalName;
            var savedPath = Path.Combine(tempDir, savedName);
            using (var fsSave = System.IO.File.Create(savedPath))
            {
                csvFile.OpenReadStream().CopyTo(fsSave);
            }

            var uploads = Path.Combine(Directory.GetCurrentDirectory(), "Images");
            if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);

            int storeId = 0;
            if (Request.HasFormContentType && int.TryParse(Request.Form["StoreId"], out var fid2)) storeId = fid2;
            if (storeId == 0) storeId = GetSellerStoreId();
            string storeName = "store";
            try
            {
                var stores = Models.UdpClientHelper.SendUdpMessage("getallstores");
                if (!string.IsNullOrEmpty(stores))
                {
                    var sl = stores.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var s in sl)
                    {
                        var p = s.Split('|');
                        if (p.Length >= 2 && int.TryParse(p[0], out var sid) && sid == storeId) { storeName = p[1]; break; }
                    }
                }
            }
            catch { }

            var errors = new List<string>();
            int row = 0;
            using (var sr = new StreamReader(savedPath, System.Text.Encoding.UTF8))
            using (var csv = new CsvReader(sr, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture) { HasHeaderRecord = false, BadDataFound = null }))
            {
                while (true)
                {
                    row++;
                    try
                    {
                        if (!csv.Read()) break;
                        var fields = new List<string>();
                        for (int i = 0; csv.TryGetField<string>(i, out var f); i++) fields.Add(f ?? string.Empty);
                        if (fields.Count < 8) { errors.Add($"Строка {row}: Недостаточно столбцов"); continue; }

                        var model = new AddProductViewModel();
                        model.ProductName = fields[0].Trim();
                        if (!int.TryParse(fields[1].Trim(), out var cat)) { errors.Add($"Строка {row}: Неверный CategoryId"); continue; }
                        model.CategoryId = cat;
                        if (!int.TryParse(fields[2].Trim(), out var man)) { errors.Add($"Строка {row}: Неверный ManufacturerId"); continue; }
                        model.ManufacturerId = man;
                        model.Description = fields[3].Trim();
                        if (!decimal.TryParse(fields[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var price)) { errors.Add($"Строка {row}: Неверная цена"); continue; }
                        model.Price = price;
                        if (!decimal.TryParse(fields[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var disc)) disc = 0m;
                        model.Discount = disc;
                        if (!int.TryParse(fields[6].Trim(), out var qty)) { errors.Add($"Строка {row}: Неверное количество"); continue; }
                        model.Quantity = qty;
                        var imageField = fields[7].Trim();

                        // locate image among uploaded files by name
                        string imageUrl = string.Empty;
                        if (!string.IsNullOrEmpty(imageField))
                        {
                            var uploaded = images?.FirstOrDefault(i => string.Equals(Path.GetFileName(i.FileName), Path.GetFileName(imageField), System.StringComparison.OrdinalIgnoreCase));
                            if (uploaded != null && uploaded.Length > 0)
                            {
                                string safeStore = MakeSafeFileName(storeName);
                                string safeProd = MakeSafeFileName(model.ProductName);
                                string destName = safeStore + "_" + safeProd + ".jpg";
                                string destPath = Path.Combine(uploads, destName);
                                try
                                {
                                    using (var fs = System.IO.File.Create(destPath))
                                    {
                                        uploaded.OpenReadStream().CopyTo(fs);
                                    }
                                    imageUrl = "/Images/" + destName;
                                }
                                catch
                                {
                                    errors.Add($"Строка {row}: Не удалось сохранить изображение {uploaded.FileName}");
                                }
                            }
                            else
                            {
                                // if imageField is a local path on server, try copy
                                if (System.IO.File.Exists(imageField))
                                {
                                    string safeStore = MakeSafeFileName(storeName);
                                    string safeProd = MakeSafeFileName(model.ProductName);
                                    string destName = safeStore + "_" + safeProd + ".jpg";
                                    string destPath = Path.Combine(uploads, destName);
                                    try
                                    {
                                        System.IO.File.Copy(imageField, destPath, true);
                                        imageUrl = "/Images/" + destName;
                                    }
                                    catch
                                    {
                                        errors.Add($"Строка {row}: Не удалось скопировать изображение из {imageField}");
                                    }
                                }
                            }
                        }

                        model.ImageUrl = imageUrl;

                        // send to server
                        try
                        {
                            var cmd = $"addproduct|{User?.Identity?.Name ?? string.Empty}|{EscapePipe(model.ProductName)}|{model.CategoryId}|{model.ManufacturerId}|{EscapePipe(model.Description)}|{model.Price.ToString(CultureInfo.InvariantCulture)}|{model.Discount.ToString(CultureInfo.InvariantCulture)}|{model.Quantity}|{EscapePipe(model.ImageUrl)}|{storeId}";
                            var resp = Models.UdpClientHelper.SendUdpMessage(cmd);
                            if (string.IsNullOrEmpty(resp) || !resp.Trim().Equals("OK", System.StringComparison.OrdinalIgnoreCase))
                            {
                                errors.Add($"Строка {row}: Сервер вернул ошибку для товара '{model.ProductName}': {resp}");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            errors.Add($"Строка {row}: Ошибка отправки на сервер: {ex.Message}");
                        }
                    }
                    catch (CsvHelperException cex)
                    {
                        errors.Add($"Строка {row}: CSV ошибка - " + cex.Message);
                    }
                }
            }

            if (errors.Any()) TempData["AddProductCsvReport"] = string.Join("\n", errors);
            else TempData["AddProductCsvReport"] = "Импорт завершён без ошибок";
            // keep saved file name for report viewing
            TempData["CsvUploadFile"] = savedName;

            return RedirectToAction("CsvReport");
        }

        [HttpGet]
        public IActionResult CsvReport()
        {
            var savedName = TempData["CsvUploadFile"] as string;
            var report = TempData["AddProductCsvReport"] as string;

            var model = new CsvReportViewModel { ReportText = report ?? string.Empty, CsvFileName = savedName };
            if (!string.IsNullOrEmpty(savedName))
            {
                var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Uploads");
                var path = Path.Combine(tempDir, savedName);
                if (System.IO.File.Exists(path))
                {
                    model.CsvLines = System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8).ToList();
                }
            }

            return View(model);
        }

        private int GetSellerStoreId()
        {
            try
            {
                var users = Models.UdpClientHelper.SendUdpMessage("getusers");
                if (string.IsNullOrEmpty(users)) return 0;
                var ulines = users.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var u in ulines)
                {
                    var p = u.Split('|');
                    if (p.Length >= 2 && string.Equals(p[1].Trim(), User?.Identity?.Name ?? string.Empty, System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (p.Length >= 7 && int.TryParse(p[6], out var sid)) return sid;
                    }
                }
            }
            catch { }
            return 0;
        }

        private string GetSellerStoreName()
        {
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage("getallstores");
                if (string.IsNullOrEmpty(resp)) return null;
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                int storeId = 0;
                // try to get seller's store id from getusers
                var users = Models.UdpClientHelper.SendUdpMessage("getusers");
                if (!string.IsNullOrEmpty(users))
                {
                    var ulines = users.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var u in ulines)
                    {
                        var p = u.Split('|');
                        if (p.Length >= 2 && string.Equals(p[1].Trim(), User?.Identity?.Name ?? string.Empty, System.StringComparison.OrdinalIgnoreCase))
                        {
                            if (p.Length >= 7 && int.TryParse(p[6], out var sid)) { storeId = sid; break; }
                        }
                    }
                }

                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 2) continue;
                    if (storeId > 0 && int.TryParse(p[0], out var sid) && sid == storeId) return p[1];
                }
            }
            catch { }
            return null;
        }

        private List<KeyValuePair<int,string>> GetCategories()
        {
            var list = new List<KeyValuePair<int,string>>();
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage("getallcategories");
                if (string.IsNullOrEmpty(resp)) return list;
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 2) continue;
                    if (int.TryParse(p[0], out var id)) list.Add(new KeyValuePair<int,string>(id, p[1]));
                }
            }
            catch { }
            return list;
        }

        private List<KeyValuePair<int,string>> GetManufacturers()
        {
            var list = new List<KeyValuePair<int,string>>();
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage("getallmanufacturers");
                if (string.IsNullOrEmpty(resp)) return list;
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 2) continue;
                    if (int.TryParse(p[0], out var id)) list.Add(new KeyValuePair<int,string>(id, p[1]));
                }
            }
            catch { }
            return list;
        }

        private static string EscapePipe(string s) => (s ?? string.Empty).Replace("|", " ");

        private List<ProductViewModel> ParseProducts(string resp)
        {
            var list = new List<ProductViewModel>();
            if (string.IsNullOrEmpty(resp)) return list;
            var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var p = l.Split('|');
                if (p.Length < 18) continue;
                if (!int.TryParse(p[0], out var id)) continue;
                var prod = new ProductViewModel
                {
                    ProductID = id,
                    ProductName = p.Length > 1 ? p[1] : string.Empty,
                    CategoryName = p.Length > 2 ? p[2] : string.Empty,
                    ManufacturerName = p.Length > 3 ? p[3] : string.Empty,
                    Country = p.Length > 4 ? p[4] : string.Empty,
                    Description = p.Length > 5 ? p[5] : string.Empty,
                    IsActive = p.Length > 6 && bool.TryParse(p[6], out var act) && act,
                    StoreName = p.Length > 9 ? p[9] : string.Empty,
                    Address = p.Length > 10 ? p[10] : string.Empty,
                    City = p.Length > 11 ? p[11] : string.Empty,
                    Phone = p.Length > 12 ? p[12] : string.Empty,
                    Price = p.Length > 13 && decimal.TryParse(p[13], NumberStyles.Any, CultureInfo.InvariantCulture, out var pr) ? pr : 0m,
                    Discount = p.Length > 14 && decimal.TryParse(p[14], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m,
                    Quantity = p.Length > 15 && int.TryParse(p[15], out var q) ? q : 0,
                    ImageUrl = p.Length > 16 ? p[16] : string.Empty,
                    StoreID = p.Length > 17 && int.TryParse(p[17], out var sid) ? sid : 0
                };

                // parse stock last update if provided as last column
                if (p.Length > 18 && DateTime.TryParse(p[18], out var stockUpd)) prod.StockUpdatedAt = stockUpd;
                else if (p.Length > 8 && DateTime.TryParse(p[8], out var updatedAt)) prod.StockUpdatedAt = updatedAt;

                list.Add(prod);
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
                if (p.Length < 6) continue;
                if (!int.TryParse(p[0], out var id)) continue;
                var store = new StoreViewModel { StoreID = id, StoreName = p[1], Address = p[2], City = p[3], Phone = p[4], LegalPerson = p[5] };
                // server now returns RegistrationDate at p[6] and Status at p[7]
                if (p.Length > 6) store.RegistrationDate = p[6];
                if (p.Length > 7 && bool.TryParse(p[7], out var st)) store.Status = st;
                list.Add(store);
            }
            return list;
        }

        private static string MakeSafeFileName(string input)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var c in input)
            {
                if (invalid.Contains(c) || char.IsWhiteSpace(c)) sb.Append('_'); else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
