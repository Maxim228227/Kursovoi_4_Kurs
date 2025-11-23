using Kursovoi.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

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
                var resp = Models.UdpClientHelper.SendUdpMessage($"getuserorders|{userId}");
                if (string.IsNullOrEmpty(resp)) return Json(new object[0]);
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                var list = new List<object>();
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 5) continue;
                    list.Add(new { orderId = p[0], productId = p[1], createdAt = p[2], status = p[3], totalAmount = p[4] });
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
                var resp = Models.UdpClientHelper.SendUdpMessage($"getuserreviews|{userId}");
                if (string.IsNullOrEmpty(resp)) return Json(new object[0]);
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                var list = new List<object>();
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 6) continue;
                    list.Add(new { reviewId = p[0], productId = p[1], title = p[2], reviewText = p[3], rating = p[4], createdAt = p[5] });
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
                ModelState.AddModelError(string.Empty, "Сервер недоступен");
                return View(model);
            }

            if (!string.IsNullOrEmpty(response) && response.Trim().ToUpper() == "OK")
            {
                // sign in with cookie authentication
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, model.Login)
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

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

                TempData["AuthMessage"] = "Вы успешно вошли";
                if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
                return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError(string.Empty, "Неправильный логин или пароль");
            return View(model);
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
                ModelState.AddModelError(string.Empty, "Сервер недоступен");
                return View(model);
            }

            if (!string.IsNullOrEmpty(response) && response.Trim().ToUpper() == "OK")
            {
                TempData["AuthMessage"] = "Регистрация прошла успешно. Войдите в систему.";
                return RedirectToAction("Login");
            }

            ModelState.AddModelError(string.Empty, "Не удалось зарегистрироваться");
            return View(model);
        }

        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["AuthMessage"] = "Вы вышли из системы.";
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

        [HttpGet]
        public IActionResult GetPurchases()
        {
            if (User?.Identity?.IsAuthenticated != true) return Json(new object[0]);
            int userId = GetCurrentUserId();
            if (userId <= 0) return Json(new object[0]);

            try
            {
                var resp = Models.UdpClientHelper.SendUdpMessage($"getuserorders|{userId}");
                if (string.IsNullOrEmpty(resp)) return Json(new object[0]);
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                var list = new List<object>();
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 5) continue;
                    list.Add(new { orderId = p[0], productId = p[1], createdAt = p[2], status = p[3], totalAmount = p[4] });
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
                // reuse getuserorders and filter for cancelled statuses
                var resp = Models.UdpClientHelper.SendUdpMessage($"getuserorders|{userId}");
                if (string.IsNullOrEmpty(resp)) return Json(new object[0]);
                var lines = resp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                var list = new List<object>();
                foreach (var l in lines)
                {
                    var p = l.Split('|');
                    if (p.Length < 5) continue;
                    var status = p[3] ?? string.Empty;
                    if (status.IndexOf("отмен", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // orderId|productId|createdAt|status|totalAmount|payment|address|storeId
                        list.Add(new { returnId = p[0], orderId = p[0], productId = p[1], status = p[3], createdAt = p[2] });
                    }
                }
                return Json(list);
            }
            catch
            {
                return Json(new object[0]);
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
    }
}
