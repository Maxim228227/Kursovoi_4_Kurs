using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Kursovoi.Models;

namespace Kursovoi.Extensions
{
    public static class HttpContextExtensions
    {
        // Try get StoreId from session -> claim -> (optional) UDP server fallback
        public static int GetStoreId(this HttpContext? ctx, bool dbFallback = false)
        {
            if (ctx == null) return 0;

            try
            {
                var sess = ctx.Session?.GetInt32("StoreId");
                if (sess.HasValue && sess.Value > 0) return sess.Value;

                var user = ctx.User;
                if (user?.Identity?.IsAuthenticated == true)
                {
                    var claim = user.FindFirst("StoreId")?.Value;
                    if (int.TryParse(claim, out var id) && id > 0) return id;

                    if (dbFallback)
                    {
                        var name = user.Identity?.Name ?? string.Empty;
                        if (!string.IsNullOrEmpty(name))
                        {
                            try
                            {
                                // Try to get storeId from UDP server 'getusers' list
                                var usersResp = UdpClientHelper.SendUdpMessage("getusers");
                                if (!string.IsNullOrEmpty(usersResp) && !usersResp.StartsWith("ERROR|"))
                                {
                                    var lines = usersResp.Split(new[] {'\n'}, System.StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var l in lines)
                                    {
                                        var parts = l.Split('|');
                                        if (parts.Length < 2) continue;
                                        if (string.Equals(parts[1].Trim(), name, System.StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (parts.Length >= 7 && int.TryParse(parts[6], out var sid) && sid > 0)
                                            {
                                                // cache in session
                                                try { ctx.Session?.SetInt32("StoreId", sid); } catch { }
                                                return sid;
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                            catch

                            {
                                // ignore UDP errors and continue
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore and return 0
            }

            return 0;
        }

        // Convenience to get claim directly from ClaimsPrincipal in views/controllers
        public static int GetStoreId(this ClaimsPrincipal? user)
        {
            if (user == null) return 0;
            var claim = user.FindFirst("StoreId")?.Value;
            if (int.TryParse(claim, out var id) && id > 0) return id;
            return 0;
        }
    }
}
