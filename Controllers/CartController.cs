using Kursovoi.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Globalization;
using System.IO;

namespace Kursovoi.Controllers
{
    public class CartController : Controller
    {
        private const string SessionCartKey = "cart";
        private const string SessionCartCountKey = "cartCount";

        [HttpGet]
        public IActionResult Index()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                // server-backed cart
                int userId = GetCurrentUserId();
                var products = new List<ProductViewModel>();
                if (userId > 0)
                {
                    try
                    {
                        var basketResp = UdpClientHelper.SendUdpMessage($"getbasket|{userId}");
                        var basket = new Dictionary<int,int>();
                        if (!string.IsNullOrEmpty(basketResp))
                        {
                            var lines = basketResp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                var parts = line.Split('|');
                                if (parts.Length < 2) continue;
                                if (int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var qty))
                                {
                                    basket[pid] = qty;
                                }
                            }
                        }

                        if (basket.Count > 0)
                        {
                            var response = UdpClientHelper.SendUdpMessage("getproducts");
                            if (!string.IsNullOrEmpty(response))
                            {
                                var lines = response.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var line in lines)
                                {
                                    var parts = line.Split('|');
                                    if (parts.Length < 18) continue;
                                    try
                                    {
                                        var productId = int.Parse(parts[0]);
                                        if (!basket.ContainsKey(productId)) continue;

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
                                            ProductID = productId,
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
                                            Quantity = basket[productId],
                                            ImageUrl = imageUrl,
                                            StoreID = int.TryParse(parts[17], out var sid) ? sid : 0
                                        };

                                        products.Add(vm);
                                    }
                                    catch
                                    {
                                        // ignore malformed
                                    }
                                }
                            }
                        }

                        // set session cart count so layout shows it
                        var totalCount = basket.Values.Sum();
                        HttpContext.Session.SetInt32(SessionCartCountKey, totalCount);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                ViewBag.CartItems = products.ToDictionary(p => p.ProductID, p => p.Quantity);
                return View(products);
            }

            // fallback to session-based cart for anonymous users
            var cart = GetCartFromSession();
            var sessProducts = new List<ProductViewModel>();

            if (cart.Count > 0)
            {
                try
                {
                    var response = UdpClientHelper.SendUdpMessage("getproducts");
                    if (!string.IsNullOrEmpty(response))
                    {
                        var lines = response.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var parts = line.Split('|');
                            if (parts.Length < 18) continue;
                            try
                            {
                                var productId = int.Parse(parts[0]);
                                if (!cart.ContainsKey(productId)) continue;

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
                                    ProductID = productId,
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
                                    Quantity = cart[productId],
                                    ImageUrl = imageUrl,
                                    StoreID = int.TryParse(parts[17], out var sid) ? sid : 0
                                };

                                sessProducts.Add(vm);
                            }
                            catch
                            {
                                // ignore malformed
                            }
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            ViewBag.CartItems = cart; // productId -> qty
            return View(sessProducts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Add(int productId, int quantity = 1)
        {
            // Determine if user is logged in and has server-side basket
            var userId = GetCurrentUserId();
            if (userId > 0)
            {
                // server-backed cart
                UdpClientHelper.SendUdpMessage($"addtobasket|{userId}|{productId}|{quantity}");

                // update session cartCount after server update
                var resp = UdpClientHelper.SendUdpMessage($"getbasket|{userId}");
                var cnt = 0;
                if (!string.IsNullOrEmpty(resp))
                {
                    var lines = resp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach(var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length>=2 && int.TryParse(parts[1], out var q)) cnt += q;
                    }
                }
                HttpContext.Session.SetInt32(SessionCartCountKey, cnt);

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true, cartCount = cnt });
                }

                return Redirect(Request.Headers["Referer"].ToString() ?? "/");
            }

            // anonymous: use session-based cart
            var cart = GetCartFromSession();
            if (cart.ContainsKey(productId)) cart[productId] += quantity;
            else cart[productId] = quantity;
            SaveCartToSession(cart);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var cntSess = HttpContext.Session.GetInt32(SessionCartCountKey) ?? 0;
                return Json(new { success = true, cartCount = cntSess });
            }

            return Redirect(Request.Headers["Referer"].ToString() ?? "/");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Remove(int productId)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userId = GetCurrentUserId();
                if (userId > 0)
                {
                    UdpClientHelper.SendUdpMessage($"removefrombasket|{userId}|{productId}");

                    // update session cartCount
                    var resp = UdpClientHelper.SendUdpMessage($"getbasket|{userId}");
                    var cnt = 0;
                    if (!string.IsNullOrEmpty(resp))
                    {
                        var lines = resp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                        foreach(var line in lines)
                        {
                            var parts = line.Split('|');
                            if (parts.Length>=2 && int.TryParse(parts[1], out var q)) cnt += q;
                        }
                    }
                    HttpContext.Session.SetInt32(SessionCartCountKey, cnt);

                    return RedirectToAction("Index");
                }
            }

            var cart = GetCartFromSession();
            if (cart.ContainsKey(productId)) cart.Remove(productId);
            SaveCartToSession(cart);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Update(int productId, int quantity)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userId = GetCurrentUserId();
                if (userId > 0)
                {
                    UdpClientHelper.SendUdpMessage($"setbasket|{userId}|{productId}|{quantity}");

                    // update session cartCount
                    var resp = UdpClientHelper.SendUdpMessage($"getbasket|{userId}");
                    var cnt = 0;
                    if (!string.IsNullOrEmpty(resp))
                    {
                        var lines = resp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                        foreach(var line in lines)
                        {
                            var parts = line.Split('|');
                            if (parts.Length>=2 && int.TryParse(parts[1], out var q)) cnt += q;
                        }
                    }
                    HttpContext.Session.SetInt32(SessionCartCountKey, cnt);

                    return RedirectToAction("Index");
                }
            }

            var cart = GetCartFromSession();
            if (quantity <= 0)
            {
                if (cart.ContainsKey(productId)) cart.Remove(productId);
            }
            else
            {
                cart[productId] = quantity;
            }
            SaveCartToSession(cart);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Clear()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userId = GetCurrentUserId();
                if (userId > 0)
                {
                    UdpClientHelper.SendUdpMessage($"clearbasket|{userId}");

                    HttpContext.Session.SetInt32(SessionCartCountKey, 0);
                    return RedirectToAction("Index");
                }
            }

            var cart = new Dictionary<int,int>();
            SaveCartToSession(cart);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Checkout(string productIds)
        {
            if (string.IsNullOrWhiteSpace(productIds)) return RedirectToAction("Index");

            var ids = productIds.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v>0).ToList();
            if (!ids.Any()) return RedirectToAction("Index");

            // build product list from server
            var all = new List<ProductViewModel>();
            try
            {
                var response = UdpClientHelper.SendUdpMessage("getproducts");
                if (!string.IsNullOrEmpty(response))
                {
                    var lines = response.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 18) continue;
                        try
                        {
                            var productId = int.Parse(parts[0]);
                            var rawImagePath = parts[16];
                            var fileName = Path.GetFileName(rawImagePath ?? string.Empty);
                            var physicalPath = Path.Combine(Directory.GetCurrentDirectory(), "Images", fileName ?? string.Empty);
                            string imageUrl = (!string.IsNullOrEmpty(fileName) && System.IO.File.Exists(physicalPath)) ? "/Images/" + fileName : "/Images/placeholder.svg";

                            all.Add(new ProductViewModel {
                                ProductID = productId,
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
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }

            var selected = all.Where(a => ids.Contains(a.ProductID)).ToList();
            if (!selected.Any()) return RedirectToAction("Index");

            // determine quantities from session or server
            var quantities = new Dictionary<int,int>();
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userId = GetCurrentUserId();
                if (userId > 0)
                {
                    var basketResp = UdpClientHelper.SendUdpMessage($"getbasket|{userId}");
                    if (!string.IsNullOrEmpty(basketResp))
                    {
                        var bl = basketResp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var l in bl)
                        {
                            var ps = l.Split('|');
                            if (ps.Length>=2 && int.TryParse(ps[0], out var pid) && int.TryParse(ps[1], out var q)) quantities[pid] = q;
                        }
                    }
                }
            }
            else
            {
                var cart = GetCartFromSession();
                foreach (var kv in cart) quantities[kv.Key] = kv.Value;
            }

            foreach (var p in selected)
            {
                p.Quantity = quantities.ContainsKey(p.ProductID) ? quantities[p.ProductID] : 1;
            }

            return View("Checkout", selected);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ConfirmCheckout(List<int> productIds, string paymentMethod, string deliveryAddress)
        {
            if (productIds == null || !productIds.Any()) return RedirectToAction("Index");

            // validate inputs
            if (string.IsNullOrWhiteSpace(paymentMethod))
            {
                ModelState.AddModelError(string.Empty, "Выберите способ оплаты");
            }
            if (string.IsNullOrWhiteSpace(deliveryAddress))
            {
                ModelState.AddModelError(string.Empty, "Укажите адрес доставки");
            }
            if (!ModelState.IsValid)
            {
                // Rebuild selected products to redisplay checkout
                var all = new List<ProductViewModel>();
                try
                {
                    var response = UdpClientHelper.SendUdpMessage("getproducts");
                    if (!string.IsNullOrEmpty(response))
                    {
                        var lines = response.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var parts = line.Split('|');
                            if (parts.Length < 18) continue;
                            try
                            {
                                var productId = int.Parse(parts[0]);
                                var rawImagePath = parts[16];
                                var fileName = Path.GetFileName(rawImagePath ?? string.Empty);
                                var physicalPath = Path.Combine(Directory.GetCurrentDirectory(), "Images", fileName ?? string.Empty);
                                string imageUrl = (!string.IsNullOrEmpty(fileName) && System.IO.File.Exists(physicalPath)) ? "/Images/" + fileName : "/Images/placeholder.svg";

                                all.Add(new ProductViewModel {
                                    ProductID = productId,
                                    ProductName = parts[1],
                                    Price = decimal.TryParse(parts[13], NumberStyles.Any, CultureInfo.InvariantCulture, out var pr) ? pr : 0m,
                                    Discount = decimal.TryParse(parts[14], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m,
                                    Quantity = int.TryParse(parts[15], out var qn) ? qn : 0,
                                    ImageUrl = imageUrl,
                                    StoreID = int.TryParse(parts[17], out var sid2) ? sid2 : 0
                                });
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                var selected = all.Where(a => productIds.Contains(a.ProductID)).ToList();
                foreach (var p in selected)
                {
                    // set quantity from basket/session
                    var qty = 1;
                    var basketResp = string.Empty;
                    if (User?.Identity?.IsAuthenticated == true)
                    {
                        int uid = GetCurrentUserId();
                        if (uid>0) basketResp = UdpClientHelper.SendUdpMessage($"getbasket|{uid}");
                    }
                    else
                    {
                        var cart = GetCartFromSession();
                        if (cart.ContainsKey(p.ProductID)) qty = cart[p.ProductID];
                    }
                    if (!string.IsNullOrEmpty(basketResp))
                    {
                        var bl = basketResp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var l in bl)
                        {
                            var ps = l.Split('|');
                            if (ps.Length>=2 && int.TryParse(ps[0], out var pid) && int.TryParse(ps[1], out var q) && pid==p.ProductID) qty = q;
                        }
                    }
                    p.Quantity = qty;
                }

                return View("Checkout", selected);
            }

            // require authentication
            if (User?.Identity?.IsAuthenticated != true)
            {
                var returnUrl = Url.Action("Index", "Cart");
                return RedirectToAction("Login", "Account", new { returnUrl = returnUrl });
            }

            var currentUserId = GetCurrentUserId();
            if (currentUserId <= 0) return RedirectToAction("Index");

            // get current basket to determine quantities
            var quantities = new Dictionary<int,int>();
            var resp = UdpClientHelper.SendUdpMessage($"getbasket|{currentUserId}");
            if (!string.IsNullOrEmpty(resp))
            {
                var lines = resp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length>=2 && int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var q)) quantities[pid] = q;
                }
            }

            // fetch product prices
            var products = new Dictionary<int, ProductViewModel>();
            try
            {
                var response = UdpClientHelper.SendUdpMessage("getproducts");
                if (!string.IsNullOrEmpty(response))
                {
                    var lines = response.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 18) continue;
                        try
                        {
                            var productId = int.Parse(parts[0]);
                            var price = decimal.TryParse(parts[13], NumberStyles.Any, CultureInfo.InvariantCulture, out var pr) ? pr : 0m;
                            var discount = decimal.TryParse(parts[14], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
                            products[productId] = new ProductViewModel { ProductID = productId, Price = price, Discount = discount, StoreID = int.TryParse(parts[17], out var sid2) ? sid2 : 0 };
                        }
                        catch { }
                    }
                }
            }
            catch { }

            decimal grandTotal = 0m;
            foreach (var pid in productIds)
            {
                var qty = quantities.ContainsKey(pid) ? quantities[pid] : 1;
                if (!products.ContainsKey(pid)) continue;
                var p = products[pid];
                var effective = p.Price * (1 - (p.Discount/100m));
                var lineTotal = effective * qty;
                grandTotal += lineTotal;

                // create order per product (could be adjusted to create single order with multiple items)
                try
                {
                    var storeId = p.StoreID;
                    var cmd = $"createorder|{currentUserId}|{pid}|{qty}|{lineTotal.ToString(CultureInfo.InvariantCulture)}|{paymentMethod}|{deliveryAddress}|{storeId}";
                    UdpClientHelper.SendUdpMessage(cmd);
                    // remove ordered item from basket
                    UdpClientHelper.SendUdpMessage($"removefrombasket|{currentUserId}|{pid}");
                }
                catch { }
            }

            // update session cart count
            try
            {
                var resp2 = UdpClientHelper.SendUdpMessage($"getbasket|{currentUserId}");
                var cnt = 0;
                if (!string.IsNullOrEmpty(resp2))
                {
                    var lines = resp2.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length>=2 && int.TryParse(parts[1], out var q)) cnt += q;
                    }
                }
                HttpContext.Session.SetInt32(SessionCartCountKey, cnt);
            }
            catch { }

            // don't store decimal in TempData to avoid serializer error
            TempData["OrderPlaced"] = "ok";
            TempData["OrderTotalStr"] = (grandTotal % 1 == 0) ? $"{grandTotal:0} Br" : $"{grandTotal:0.##} Br";
            return RedirectToAction("Index", "Home");
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
                    if (int.TryParse(parts[0], out var id) && string.Equals(parts[1].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return id;
                    }
                }
            }
            catch
            {
                // ignore
            }
            return 0;
        }

        private Dictionary<int,int> GetCartFromSession()
        {
            var json = HttpContext.Session.GetString(SessionCartKey);
            if (string.IsNullOrEmpty(json)) return new Dictionary<int,int>();
            try
            {
                return JsonSerializer.Deserialize<Dictionary<int,int>>(json) ?? new Dictionary<int,int>();
            }
            catch
            {
                return new Dictionary<int,int>();
            }
        }

        private void SaveCartToSession(Dictionary<int,int> cart)
        {
            var json = JsonSerializer.Serialize(cart);
            HttpContext.Session.SetString(SessionCartKey, json);
            int count = cart.Values.Sum();
            HttpContext.Session.SetInt32(SessionCartCountKey, count);
        }
    }
}
