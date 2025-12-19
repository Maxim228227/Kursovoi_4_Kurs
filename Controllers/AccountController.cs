using Kursovoi.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Collections.Generic;

namespace Kursovoi.Controllers
{
    public class AccountController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            if (User?.Identity?.IsAuthenticated != true) return RedirectToAction("Login");
            return View();
        }

        [HttpGet]
        public IActionResult GetOrders()
        {
            if (User?.Identity?.IsAuthenticated != true) return Json(new object[0]);
            int userId = GetCurrentUserId();
            if (userId <= 0) return Json(new object[0]);

            try
            {
                var prodMap = BuildProductNameMap();
                var resp = Models.UdpClientHelper.SendUdpMessage($"getuserorders|{userId}");
                if (string.IsNullOrEmpty(resp)) return Json(new object[0]);
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                var list = new List<object>();
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 5) continue;
                    var pid = p[1];
                    var name = prodMap.TryGetValue(int.TryParse(pid, out var ip) ? ip : 0, out var nm) ? nm : null;
                    list.Add(new { orderId = p[0], productId = p[1], productName = name, createdAt = p[2], status = p[3], totalAmount = p[4] });
                }
                return Json(list);
            }
            catch
            {
                return Json(new object[0]);
            }
        }

        [HttpGet]
        public IActionResult GetPurchases()
        {
            if (User?.Identity?.IsAuthenticated != true) return Json(new object[0]);
            int userId = GetCurrentUserId();
            if (userId <= 0) return Json(new object[0]);

            try
            {
                var prodMap = BuildProductNameMap();
                var resp = Models.UdpClientHelper.SendUdpMessage($"getuserorders|{userId}");
                if (string.IsNullOrEmpty(resp)) return Json(new object[0]);
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                var list = new List<object>();
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 5) continue;
                    var pid = p[1];
                    var name = prodMap.TryGetValue(int.TryParse(pid, out var ip) ? ip : 0, out var nm) ? nm : null;
                    list.Add(new { orderId = p[0], productId = p[1], productName = name, createdAt = p[2], status = p[3], totalAmount = p[4] });
                }
                return Json(list);
            }
            catch
            {
                return Json(new object[0]);
            }
        }

        [HttpGet]
        public IActionResult GetReturns()
        {
            if (User?.Identity?.IsAuthenticated != true) return Json(new object[0]);
            int userId = GetCurrentUserId();
            if (userId <= 0) return Json(new object[0]);

            try
            {
                var prodMap = BuildProductNameMap();
                // reuse getuserorders and filter for cancelled statuses
                var resp = Models.UdpClientHelper.SendUdpMessage($"getuserorders|{userId}");
                if (string.IsNullOrEmpty(resp)) return Json(new object[0]);
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                var list = new List<object>();
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 5) continue;
                    var status = (p[3] ?? string.Empty).ToLowerInvariant();
                    // consider any status that contains 'отмен' (covers Отменён/Отменен/Отмена)
                    if (status.Contains("отмен"))
                    {
                        var pid = p[1];
                        var name = prodMap.TryGetValue(int.TryParse(pid, out var ip) ? ip : 0, out var nm) ? nm : null;
                        // orderId|productId|createdAt|status|totalAmount|payment|address|storeId
                        list.Add(new { returnId = p[0], orderId = p[0], productId = p[1], productName = name, status = p[3], createdAt = p[2] });
                    }
                }
                return Json(list);
            }
            catch
            {
                return Json(new object[0]);
            }
        }

        [HttpGet]
        public IActionResult GetReviews()
        {
            if (User?.Identity?.IsAuthenticated != true) return Json(new object[0]);
            int userId = GetCurrentUserId();
            if (userId <= 0) return Json(new object[0]);

            try
            {
                var prodMap = BuildProductNameMap();
                var resp = Models.UdpClientHelper.SendUdpMessage($"getuserreviews|{userId}");
                if (string.IsNullOrEmpty(resp)) return Json(new object[0]);
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                var list = new List<object>();
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 6) continue;
                    var pid = p[1];
                    var name = prodMap.TryGetValue(int.TryParse(pid, out var ip) ? ip : 0, out var nm) ? nm : null;
                    list.Add(new { reviewId = p[0], productId = p[1], productName = name, title = p[2], reviewText = p[3], rating = p[4], createdAt = p[5] });
                }
                return Json(list);
            }
            catch
            {
                return Json(new object[0]);
            }
        }

        [HttpGet]
        public IActionResult Login(string returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async System.Threading.Tasks.Task<IActionResult> Login(LoginViewModel model, string returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var hash = ComputeSha256Hash(model.Password);
            var message = $"authorize|{model.Login}|{hash}";
            string response;
            try
            {
                response = Models.UdpClientHelper.SendUdpMessage(message);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "Ошибка соединения");
                return View(model);
            }

            if (string.IsNullOrEmpty(response))
            {
                ModelState.AddModelError(string.Empty, "Ошибка авторизации");
                return View(model);
            }

            response = response.Trim();

            // If server returned an ERROR|... string, display message
            if (response.StartsWith("ERROR|", System.StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, response.Substring(6));
                return View(model);
            }

            // If server returned FAIL => invalid credentials
            if (string.Equals(response, "FAIL", System.StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Неверные имя или пароль");
                return View(model);
            }

            // Otherwise treat response as role name (e.g. "admin" or "user" or "seller")
            var roleName = response; // not null/empty here

            // sign in with cookie authentication and include role claim
            var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, model.Login),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, roleName)
            };

            // Attempt to get StoreID from UDP server users list
            int? storeIdForClaim = null;
            try
            {
                var usersResp = Models.UdpClientHelper.SendUdpMessage("getusers");
                if (!string.IsNullOrEmpty(usersResp) && !usersResp.StartsWith("ERROR|"))
                {
                    var ulines = usersResp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var l in ulines)
                    {
                        var parts = l.Split('|');
                        if (parts.Length < 2) continue;
                        if (string.Equals(parts[1].Trim(), model.Login, StringComparison.OrdinalIgnoreCase))
                        {
                            if (parts.Length >= 7 && int.TryParse(parts[6], out var sid) && sid > 0)
                            {
                                storeIdForClaim = sid;
                                claims.Add(new System.Security.Claims.Claim("StoreId", sid.ToString()));
                            }
                            break;
                        }
                    }
                }
            }
            catch { }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // store StoreId in session for quick lookup (if found)
            try
            {
                if (storeIdForClaim.HasValue && storeIdForClaim.Value > 0)
                {
                    HttpContext.Session.SetInt32("StoreId", storeIdForClaim.Value);
                }
                else
                {
                    // ensure session key cleared
                    HttpContext.Session.Remove("StoreId");
                }
            }
            catch { }

            // After sign-in, fetch basket count and store in session
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage("getusers");
                int userId = 0;
                if (!string.IsNullOrEmpty(resp))
                {
                    var lines = resp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 2) continue;
                        if (string.Equals(parts[1].Trim(), model.Login, StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[0], out var id))
                        {
                            userId = id; break;
                        }
                    }
                }

                if (userId > 0)
                {
                    var basketResp = Models.UdpClientHelper.SendUdpMessage($"getbasket|{userId}");
                    var cnt = 0;
                    if (!string.IsNullOrEmpty(basketResp))
                    {
                        var bl = basketResp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var l in bl)
                        {
                            var ps = l.Split('|');
                            if (ps.Length>=2 && int.TryParse(ps[1], out var q)) cnt += q;
                        }
                    }
                    HttpContext.Session.SetInt32("cartCount", cnt);
                }
            }
            catch
            {
                // ignore
            }

            // If admin, redirect to admin panel and request tab reset
            if (string.Equals(roleName, "admin", System.StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Admin", new { fromLogin = 1 });
            }

            // If seller, redirect to seller panel
            if (string.Equals(roleName, "seller", System.StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Seller");
            }

            // don't set TempData message on successful login
            if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var hash = ComputeSha256Hash(model.Password);
            // default RoleID = 2, include phone
            var phone = string.IsNullOrWhiteSpace(model.Phone) ? "" : model.Phone.Trim();
            var message = $"register|{model.Login}|{hash}|2|{phone}";

            string response;
            try
            {
                response = Models.UdpClientHelper.SendUdpMessage(message);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "Ошибка соединения");
                return View(model);
            }

            if (!string.IsNullOrEmpty(response) && response.Trim().ToUpper() == "OK")
            {
                TempData["AuthMessage"] = "Пользователь успешно создан. Войдите в систему.";
                return RedirectToAction("Login");
            }

            ModelState.AddModelError(string.Empty, "Не удалось зарегистрировать");
            return View(model);
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["AuthMessage"] = "Выход выполнен.";
            HttpContext.Session.SetInt32("cartCount", 0);
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async System.Threading.Tasks.Task<IActionResult> DeleteAccount()
        {
            if (User?.Identity?.IsAuthenticated != true) return Json(new { success = false });
            int userId = GetCurrentUserId();
            if (userId <= 0) return Json(new { success = false });

            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage($"deleteuser|{userId}");
                if (!string.IsNullOrEmpty(resp) && resp.Trim().ToUpper() == "OK")
                {
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    HttpContext.Session.SetInt32("cartCount", 0);
                    return Json(new { success = true });
                }
                return Json(new { success = false });
            }
            catch
            {
                return Json(new { success = false });
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

        private int GetCurrentUserId()
        {
            var name = User?.Identity?.Name ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return 0;
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage("getusers");
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

        private Dictionary<int, string> BuildProductNameMap()
        {
            var map = new Dictionary<int, string>();
            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage("getproducts");
                if (string.IsNullOrEmpty(resp)) return map;
                var lines = resp.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 2) continue;
                    if (int.TryParse(p[0], out var id)) map[id] = p[1];
                }
            }
            catch { }
            return map;
        }
    }
}
