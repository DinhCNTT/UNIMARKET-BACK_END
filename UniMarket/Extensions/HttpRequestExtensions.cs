using Microsoft.AspNetCore.Http;
using System.Linq;

namespace UniMarket.Extensions
{
    public static class HttpRequestExtensions
    {
        public static string GetClientIp(this HttpRequest request)
        {
            // Ưu tiên header do proxy/CDN set
            string[] headerKeys = new[]
            {
                "CF-Connecting-IP",       // Cloudflare
                "X-Real-IP",              // Nginx
                "X-Forwarded-For"         // Chuẩn chung
            };

            foreach (var key in headerKeys)
            {
                if (request.Headers.TryGetValue(key, out var value))
                {
                    var ip = value.ToString();
                    // X-Forwarded-For có thể là "client, proxy1, proxy2"
                    if (key == "X-Forwarded-For" && ip.Contains(','))
                        ip = ip.Split(',').Select(s => s.Trim()).FirstOrDefault() ?? ip;
                    if (!string.IsNullOrWhiteSpace(ip)) return ip;
                }
            }

            var remoteIp = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

            // ✅ Chuyển IPv6 loopback (::1) thành IPv4 loopback (127.0.0.1) cho dễ đọc
            if (remoteIp == "::1")
                return "127.0.0.1";

            return remoteIp;
        }
    }
}
