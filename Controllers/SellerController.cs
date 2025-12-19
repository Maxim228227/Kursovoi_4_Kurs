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
        private readonly DbHelperClient _db;
        public SellerController(DbHelperClient db)
        {
            _db = db;
        }

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

            // pass seller's store options (may be zero, one or many)
            var storeIds = _db.GetStoreIdsForUser(User?.Identity?.Name ?? string.Empty);
            var storeOptions = new List<KeyValuePair<int,string>>();
            // get store names from UDP and map
            var storesResp = Models.UdpClientHelper.SendUdpMessage("getallstores");
            var storeMap = ParseStores(storesResp).ToDictionary(s => s.StoreID, s => s.StoreName);
            foreach (var sid in storeIds)
            {
                var name = storeMap.ContainsKey(sid) ? storeMap[sid] : sid.ToString();
                storeOptions.Add(new KeyValuePair<int,string>(sid, name));
            }
            ViewData["StoreOptions"] = storeOptions;

            // for compatibility, set StoreId/StoreName for single-store users
            ViewData["StoreId"] = storeIds.Count == 1 ? storeIds[0] : 0;
            ViewData["StoreName"] = storeIds.Count == 1 && storeOptions.Count>0 ? storeOptions[0].Value : string.Empty;
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
                // restore store options
                var storeIds = _db.GetStoreIdsForUser(User?.Identity?.Name ?? string.Empty);
                var storeOptions = BuildStoreOptions(storeIds);
                ViewData["StoreOptions"] = storeOptions;
                ViewData["StoreId"] = storeIds.Count == 1 ? storeIds[0] : 0;
                ViewData["StoreName"] = storeOptions.FirstOrDefault().Value ?? string.Empty;

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    var errors = ModelState.Where(kv => kv.Value.Errors.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.Errors.Select(e => e.ErrorMessage).ToArray());
                    return Json(new { success = false, errors = errors });
                }

                return View(model);
            }

            // get seller's associated stores
            var sellerStoreIds = _db.GetStoreIdsForUser(User?.Identity?.Name ?? string.Empty);

            // prefer StoreId posted from client
            int storeId = 0;
            if (Request.HasFormContentType && int.TryParse(Request.Form["StoreId"], out var fid)) storeId = fid;

            // Validation: determine effective storeId according to rules
            if (sellerStoreIds.Count == 0)
            {
                // user has no stores
                ModelState.AddModelError(string.Empty, "У вас не привязан ни один магазин. Обратитесь к администратору или укажите StoreID явно.");

                ViewData["Categories"] = GetCategories();
                ViewData["Manufacturers"] = GetManufacturers();
                ViewData["StoreOptions"] = BuildStoreOptions(sellerStoreIds);
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = false, errors = ModelState });
                return View(model);
            }
            else if (sellerStoreIds.Count == 1)
            {
                // single store: use it regardless of posted value
                storeId = sellerStoreIds[0];
            }
            else
            {
                // multiple stores: require explicit storeId and verify access
                if (storeId <= 0 || !sellerStoreIds.Contains(storeId))
                {
                    ModelState.AddModelError(string.Empty, "У вас несколько магазинов. Укажите StoreID, к которому вы хотите добавить товар.");

                    ViewData["Categories"] = GetCategories();
                    ViewData["Manufacturers"] = GetManufacturers();
                    ViewData["StoreOptions"] = BuildStoreOptions(sellerStoreIds);
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = false, errors = ModelState });
                    return View(model);
                }
            }

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
                    ViewData["StoreOptions"] = BuildStoreOptions(sellerStoreIds);
                    ViewData["StoreId"] = storeId;
                    ViewData["StoreName"] = GetSellerStoreName();

                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new { success = false, error = ex.Message });
                    }

                    return View(model);
                }
            }

            try
            {
                // Try to add directly via DB from web app (preferred)
                bool addedViaDb = false;
                try
                {
                    addedViaDb = _db.AddProductToDb(model.ProductName, model.CategoryId, model.ManufacturerId, model.Description, model.Price, model.Discount, model.Quantity, model.ImageUrl, storeId);
                }
                catch { addedViaDb = false; }

                if (addedViaDb)
                {
                    TempData["AddProductMsg"] = "Товар добавлен (DB).";
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = true });
                    return RedirectToAction("Index");
                }

                // fallback to UDP
                var resp = Models.UdpClientHelper.SendUdpMessage($"addproduct|{User?.Identity?.Name ?? string.Empty}|{EscapePipe(model.ProductName)}|{model.CategoryId}|{model.ManufacturerId}|{EscapePipe(model.Description)}|{model.Price.ToString(CultureInfo.InvariantCulture)}|{model.Discount.ToString(CultureInfo.InvariantCulture)}|{model.Quantity}|{EscapePipe(model.ImageUrl)}|{storeId}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK")
                {
                    TempData["AddProductMsg"] = "Товар добавлен (UDP).";
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Json(new { success = true });
                    return RedirectToAction("Index");
                }

                var err = !string.IsNullOrEmpty(_db?.LastError) ? _db.LastError : (resp ?? "(неизвестный ответ)");
                ModelState.AddModelError(string.Empty, "Ошибка при добавлении: " + err);
            }
            catch (System.Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Ошибка при добавлении товара: " + ex.Message);
            }

            ViewData["Categories"] = GetCategories();
            ViewData["Manufacturers"] = GetManufacturers();
            ViewData["StoreOptions"] = BuildStoreOptions(_db.GetStoreIdsForUser(User?.Identity?.Name ?? string.Empty));
            ViewData["StoreId"] = storeId;
            ViewData["StoreName"] = GetSellerStoreName();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var errors = ModelState.Where(kv => kv.Value.Errors.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.Errors.Select(e => e.ErrorMessage).ToArray());
                return Json(new { success = false, errors = errors });
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddProductFromCsv(AddProductViewModel model)
        {
            if (model == null) return Json(new { success = false });
            // determine store similar to AddProduct
            var sellerStoreIds = _db.GetStoreIdsForUser(User?.Identity?.Name ?? string.Empty);
            int storeId = 0;
            if (Request.HasFormContentType && int.TryParse(Request.Form["StoreId"], out var fid)) storeId = fid;
            if (sellerStoreIds.Count == 0) return Json(new { success = false, error = "User has no associated stores" });
            if (sellerStoreIds.Count == 1) storeId = sellerStoreIds[0];
            if (sellerStoreIds.Count > 1 && !sellerStoreIds.Contains(storeId)) return Json(new { success = false, error = "Multiple stores - specify StoreId" });

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
            var sellerStoreIds = _db.GetStoreIdsForUser(User?.Identity?.Name ?? string.Empty);
            if (sellerStoreIds.Count == 1) storeId = sellerStoreIds[0];

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
                        if (fields.Count < 8) { errors.Add($"Строка {row}: недостаточно полей"); continue; }

                        var model = new AddProductViewModel();
                        model.ProductName = fields[0].Trim();
                        if (!int.TryParse(fields[1].Trim(), out var cat)) { errors.Add($"Строка {row}: неверный CategoryId"); continue; }
                        model.CategoryId = cat;
                        if (!int.TryParse(fields[2].Trim(), out var man)) { errors.Add($"Строка {row}: неверный ManufacturerId"); continue; }
                        model.ManufacturerId = man;
                        model.Description = fields[3].Trim();
                        if (!decimal.TryParse(fields[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var price)) { errors.Add($"Строка {row}: неверная цена"); continue; }
                        model.Price = price;
                        if (!decimal.TryParse(fields[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var disc)) disc = 0m;
                        model.Discount = disc;
                        if (!int.TryParse(fields[6].Trim(), out var qty)) { errors.Add($"Строка {row}: неверное количество"); continue; }
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
                                    errors.Add($"Строка {row}: не удалось сохранить изображение {uploaded.FileName}");
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
                                        errors.Add($"Строка {row}: не удалось скопировать изображение из {imageField}");
                                    }
                                }
                            }
                        }

                        model.ImageUrl = imageUrl;

                        // send to server
                        try
                        {
                            var cmd = $"addproduct|{User?.Identity?.Name ?? string.Empty}|{EscapePipe(model.ProductName)}|{model.CategoryId}|{model.ManufacturerId}|{EscapePipe(model.Description)}|{model.Price.ToString(CultureInfo.InvariantCulture)}|{model.Discount.ToString(CultureInfo.InvariantCulture)}|{model.Quantity}|{EscapePipe(model.ImageUrl)}|{storeId}";
                            // Try to add directly via DB from web app (preferred)
                            bool addedViaDb = false;
                            try
                            {
                                addedViaDb = _db.AddProductToDb(model.ProductName, model.CategoryId, model.ManufacturerId, model.Description, model.Price, model.Discount, model.Quantity, model.ImageUrl, storeId);
                            }
                            catch { addedViaDb = false; }

                            if (addedViaDb)
                            {
                                errors.Add($"Строка {row}: товар добавлен (DB)");
                            }

                            // if DB insert failed, try UDP as a fallback
                            var resp = Models.UdpClientHelper.SendUdpMessage(cmd);
                            if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK")
                            {
                                errors.Add($"Строка {row}: товар добавлен (UDP)");
                            }
                            else
                            {
                                // show error from DB helper if available, otherwise server response
                                var err = !string.IsNullOrEmpty(_db?.LastError) ? _db.LastError : (resp ?? "(неизвестный ответ)");
                                errors.Add($"Строка {row}: ошибка при добавлении: " + err);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            errors.Add($"Строка {row}: ошибка при добавлении: " + ex.Message);
                        }
                    }
                    catch (CsvHelperException cex)
                    {
                        errors.Add($"Строка {row}: CSV ошибка - " + cex.Message);
                    }
                }
            }

            if (errors.Any()) TempData["AddProductCsvReport"] = string.Join("\n", errors);
            else TempData["AddProductCsvReport"] = "Все записи успешно добавлены";
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

        private List<KeyValuePair<int,string>> BuildStoreOptions(List<int> storeIds)
        {
            var opts = new List<KeyValuePair<int,string>>();
            try
            {
                var storesResp = Models.UdpClientHelper.SendUdpMessage("getallstores");
                var stores = ParseStores(storesResp).ToDictionary(s => s.StoreID, s => s.StoreName);
                foreach (var sid in storeIds)
                {
                    var name = stores.ContainsKey(sid) ? stores[sid] : sid.ToString();
                    opts.Add(new KeyValuePair<int, string>(sid, name));
                }
            }
            catch { }
            return opts;
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

        [HttpGet]
        public IActionResult Analytics()
        {
            var model = new SellerAnalyticsViewModel();
            // set categories for filter dropdown
            ViewBag.Categories = GetCategories();

            // populate basic store id
            model.StoreId = GetSellerStoreId();

            // default date range: last 30 days
            var end = System.DateTime.UtcNow.Date;
            var start = end.AddDays(-30);

            try
            {
                // reuse GetSellerAnalytics logic to fill model
                var json = GetSellerAnalyticsInternal(start, end, 0);
                if (json != null)
                {
                    model = json;
                    model.StoreId = GetSellerStoreId();
                }
            }
            catch { }

            return View(model);
        }

        [HttpGet]
        public IActionResult GetSellerAnalytics(string startDate = null, string endDate = null, int categoryId = 0)
        {
            DateTime start, end;
            if (!DateTime.TryParse(startDate, out start)) start = DateTime.UtcNow.Date.AddDays(-30);
            if (!DateTime.TryParse(endDate, out end)) end = DateTime.UtcNow.Date;

            try
            {
                var model = GetSellerAnalyticsInternal(start, end, categoryId);
                return Json(model);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // internal builder used by both actions
        private SellerAnalyticsViewModel GetSellerAnalyticsInternal(DateTime start, DateTime end, int categoryId)
        {
            var vm = new SellerAnalyticsViewModel();
            int storeId = GetSellerStoreId();
            vm.StoreId = storeId;

            // fetch products and filter by store/category
            var prodResp = Models.UdpClientHelper.SendUdpMessage("getproducts");
            var products = ParseProducts(prodResp);
            if (storeId > 0) products = products.Where(p => p.StoreID == storeId).ToList();
            if (categoryId > 0)
            {
                try
                {
                    var cats = GetCategories();
                    var catName = cats.FirstOrDefault(kv => kv.Key == categoryId).Value;
                    if (!string.IsNullOrEmpty(catName)) products = products.Where(p => string.Equals(p.CategoryName?.Trim(), catName.Trim(), System.StringComparison.OrdinalIgnoreCase)).ToList();
                }
                catch { }
            }
            // Note: categoryId mapping by name not available here; skip strong filter unless category names/ids mapping provided

            // Running out / out of stock
            foreach (var p in products)
            {
                if (p.Quantity <= 0)
                {
                    vm.OutOfStockProducts.Add(new StockAnalyticsItem { ProductID = p.ProductID, ProductName = p.ProductName, Quantity = p.Quantity, LastUpdate = p.StockUpdatedAt == System.DateTime.MinValue ? System.DateTime.MinValue : p.StockUpdatedAt });
                }
                else if (p.Quantity <= 10)
                {
                    vm.RunningOutProducts.Add(new StockAnalyticsItem { ProductID = p.ProductID, ProductName = p.ProductName, Quantity = p.Quantity, LastUpdate = p.StockUpdatedAt == System.DateTime.MinValue ? System.DateTime.MinValue : p.StockUpdatedAt });
                }
            }

            // Orders by date range (use server helper)
            var startStr = start.ToString("yyyy-MM-dd");
            var endStr = end.ToString("yyyy-MM-dd");
            var ordersResp = Models.UdpClientHelper.SendUdpMessage($"getordersbydaterange|{startStr}|{endStr}|{storeId}");
            var orders = new List<(int OrderId, int ProductId, DateTime CreatedAt, string Status, decimal Total)>();
            if (!string.IsNullOrEmpty(ordersResp))
            {
                var lines = ordersResp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in lines)
                {
                    var parts = l.Split('|');
                    if (parts.Length < 5) continue;
                    if (!int.TryParse(parts[0], out var oid)) continue;
                    int pid = int.TryParse(parts[1], out var tpid) ? tpid : 0;
                    DateTime created = DateTime.TryParse(parts[2], out var dt) ? dt : DateTime.MinValue;
                    string status = parts[3];
                    decimal total = decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) ? dec : 0m;
                    orders.Add((oid, pid, created, status, total));
                }
            }

            // group orders by day
            var ordersByDay = orders.GroupBy(o => o.CreatedAt.Date).OrderBy(g => g.Key);
            foreach (var g in ordersByDay)
            {
                vm.OrdersByDay.Add(new OrderPeriodData { Period = g.Key.ToString("yyyy-MM-dd"), OrderCount = g.Count(), TotalAmount = g.Sum(x => x.Total) });
            }

            vm.AverageOrderAmount = orders.Any() ? orders.Average(o => o.Total) : 0m;

            // Top selling products
            var topResp = Models.UdpClientHelper.SendUdpMessage($"gettopsellingproducts|{storeId}|{startStr}|{endStr}");
            if (!string.IsNullOrEmpty(topResp))
            {
                var lines = topResp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 4) continue;
                    if (!int.TryParse(p[0], out var prodId)) continue;
                    var name = p[1];
                    int qty = int.TryParse(p[2], out var qv) ? qv : 0;
                    decimal rev = decimal.TryParse(p[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var rv) ? rv : 0m;
                    vm.TopSellingProducts.Add(new TopProductItem { ProductID = prodId, ProductName = name, QuantitySold = qty, Revenue = rev });
                }
            }

            // Abandoned baskets
            var abandonedResp = Models.UdpClientHelper.SendUdpMessage($"getabandonedbaskets|{storeId}|7");
            if (!string.IsNullOrEmpty(abandonedResp))
            {
                var lines = abandonedResp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 4) continue;
                    int uid = int.TryParse(p[0], out var u) ? u : 0;
                    string login = p[1];
                    int prodCount = int.TryParse(p[2], out var pc) ? pc : 0;
                    decimal total = decimal.TryParse(p[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var tt) ? tt : 0m;
                    vm.AbandonedBaskets.Add(new BasketAnalyticsItem { UserID = uid, UserLogin = login, ProductCount = prodCount, TotalAmount = total, LastActivity = DateTime.MinValue });
                }
                vm.TotalAbandonedBaskets = vm.AbandonedBaskets.Count;
            }

            // Reviews for products (compute average rating)
            var allReviews = new List<ReviewAnalyticsItem>();
            foreach (var prod in products)
            {
                var rresp = Models.UdpClientHelper.SendUdpMessage($"getproductreviews|{prod.ProductID}");
                if (string.IsNullOrEmpty(rresp)) continue;
                var lines = rresp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                int rc = 0; decimal sum = 0m;
                foreach (var l in lines)
                {
                    var parts = l.Split('|');
                    if (parts.Length < 6) continue;
                    int rating = int.TryParse(parts[5], out var rv) ? rv : 0;
                    rc++; sum += rating;
                }
                if (rc > 0)
                {
                    allReviews.Add(new ReviewAnalyticsItem { ProductID = prod.ProductID, ProductName = prod.ProductName, ReviewCount = rc, AverageRating = sum / rc });
                }
            }
            vm.ReviewsData = allReviews;
            vm.TotalReviews = allReviews.Sum(r => r.ReviewCount);
            vm.AverageRating = allReviews.Any() ? allReviews.Average(r => r.AverageRating) : 0m;

            return vm;
        }

        [HttpGet]
        public IActionResult EditProduct(int id)
        {
            if (id <= 0) return BadRequest();
            ProductViewModel? model = null;
            // try DB first
            try { model = _db.GetProductById(id); } catch { model = null; }
            if (model == null)
            {
                // fallback to UDP
                var resp = Models.UdpClientHelper.SendUdpMessage($"getproductbyid|{id}");
                if (!string.IsNullOrEmpty(resp))
                {
                    var parts = resp.Split('|');
                    if (parts.Length >= 9 && int.TryParse(parts[0], out var pid))
                    {
                        model = new ProductViewModel {
                            ProductID = pid,
                            ProductName = parts.Length>1?parts[1]:string.Empty,
                            CategoryId = parts.Length>2 && int.TryParse(parts[2], out var c)?c:0,
                            ManufacturerId = parts.Length>3 && int.TryParse(parts[3], out var m)?m:0,
                            Description = parts.Length>4?parts[4]:string.Empty,
                            Price = parts.Length>5 && decimal.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var pr)?pr:0m,
                            Discount = parts.Length>6 && decimal.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out var di)?di:0m,
                            Quantity = parts.Length>7 && int.TryParse(parts[7], out var q)?q:0,
                            ImageUrl = parts.Length>8?parts[8]:string.Empty
                        };
                    }
                }
            }
            if (model==null) return NotFound();
            ViewData["Categories"] = GetCategories();
            ViewData["Manufacturers"] = GetManufacturers();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditProduct(ProductViewModel model)
        {
            if (model==null || model.ProductID<=0) return BadRequest();
            if (!ModelState.IsValid)
            {
                ViewData["Categories"] = GetCategories();
                ViewData["Manufacturers"] = GetManufacturers();
                return View(model);
            }

            bool ok = false;
            // try direct DB update first
            try
            {
                ok = _db.UpdateProduct(model.ProductID, model.ProductName, model.CategoryId, model.ManufacturerId, model.Description, model.Price, model.Discount, model.Quantity, model.ImageUrl, model.StoreID);
            }
            catch { ok = false; }

            string usedPath = "db";
            if (!ok)
            {
                // fallback to UDP command
                usedPath = "udp";
                var cmd = $"updateproduct|{User?.Identity?.Name ?? string.Empty}|{model.ProductID}|{EscapePipe(model.ProductName)}|{model.CategoryId}|{model.ManufacturerId}|{EscapePipe(model.Description)}|{model.Price.ToString(CultureInfo.InvariantCulture)}|{model.Discount.ToString(CultureInfo.InvariantCulture)}|{model.Quantity}|{EscapePipe(model.ImageUrl)}|{model.StoreID}";
                try
                {
                    var resp = Models.UdpClientHelper.SendUdpMessage(cmd);
                    ok = !string.IsNullOrEmpty(resp) && resp.Trim().ToUpper()=="OK";
                }
                catch { ok=false; }
            }

            if (ok)
            {
                TempData["Message"] = $"Изменено ({usedPath}).";
                // redirect back to edit so user can see updated values
                return RedirectToAction("EditProduct", new { id = model.ProductID });
            }
            var detail = string.Empty;
            try { if (!string.IsNullOrEmpty(_db?.LastError)) detail = _db.LastError; } catch { }
            TempData["ErrorDetail"] = detail;
            ModelState.AddModelError(string.Empty, "Не удалось сохранить изменения");
            ViewData["Categories"] = GetCategories();
            ViewData["Manufacturers"] = GetManufacturers();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BlockProduct(int productId)
        {
            if (productId<=0) return BadRequest();
            bool ok = false;
            string used = "db";
            try { ok = _db.UpdateProductStatus(productId, true); } catch { ok=false; }
            string udpResp = null;
            if (!ok)
            {
                used = "udp";
                try { udpResp = Models.UdpClientHelper.SendUdpMessage($"setproductstatus|{productId}|1"); ok = udpResp!=null && udpResp.Trim().ToUpper()=="OK"; } catch { }
            }
            var message = ok ? $"Товар {productId} заблокирован ({used})." : $"Не удалось заблокировать товар {productId}.";
            string detail = null;
            if (!ok)
            {
                try { if (!string.IsNullOrEmpty(_db?.LastError)) detail = _db.LastError; else if (!string.IsNullOrEmpty(udpResp)) detail = udpResp; } catch { }
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = ok, message = message, status = ok ? true : (bool?)null, error = detail });
            }

            TempData["Message"] = message;
            if (!string.IsNullOrEmpty(detail)) TempData["ErrorDetail"] = detail;
            return RedirectToAction("Index");
        }

        public IActionResult UnblockProduct(int productId)
        {
            if (productId<=0) return BadRequest();
            bool ok = false;
            string used = "db";
            try { ok = _db.UpdateProductStatus(productId, false); } catch { ok=false; }
            string udpResp2 = null;
            if (!ok)
            {
                used = "udp";
                try { udpResp2 = Models.UdpClientHelper.SendUdpMessage($"setproductstatus|{productId}|0"); ok = udpResp2!=null && udpResp2.Trim().ToUpper()=="OK"; } catch { }
            }
            var message = ok ? $"Товар {productId} разблокирован ({used})." : $"Не удалось разблокировать товар {productId}.";
            string detail = null;
            if (!ok)
            {
                try { if (!string.IsNullOrEmpty(_db?.LastError)) detail = _db.LastError; else if (!string.IsNullOrEmpty(udpResp2)) detail = udpResp2; } catch { }
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = ok, message = message, status = ok ? false : (bool?)null, error = detail });
            }

            TempData["Message"] = message;
            if (!string.IsNullOrEmpty(detail)) TempData["ErrorDetail"] = detail;
            return RedirectToAction("Index");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult TestDb(int productId = 0)
        {
            try
            {
                if (productId > 0)
                {
                    var prod = _db.GetProductById(productId);
                    return Json(new { ok = prod != null, product = prod, error = _db.LastError });
                }
                else
                {
                    // try a harmless query: get store id for current user if available
                    var login = User?.Identity?.Name ?? string.Empty;
                    var sid = _db.GetStoreIdForUser(login);
                    return Json(new { ok = true, storeId = sid, error = _db.LastError });
                }
            }
            catch (System.Exception ex)
            {
                return Json(new { ok = false, error = ex.Message });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult TestUdp()
        {
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage("getproducts");
                return Json(new { ok = true, response = resp });
            }
            catch (System.Exception ex)
            {
                return Json(new { ok = false, error = ex.Message });
            }
        }
    }
}
