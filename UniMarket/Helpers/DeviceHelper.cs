using System.Security.Cryptography;
using System.Text;
namespace UniMarket.Helpers
{
    public static class DeviceHelper
    {
        /// <summary>
        /// Tạo dấu vân tay thiết bị từ IP + User-Agent bằng SHA256 (hex 64 ký tự, lowercase)
        /// </summary>
        public static string GenerateDeviceInfo(string ip, string userAgent)
        {
            var raw = $"{ip}_{userAgent ?? string.Empty}";
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(raw);
            var hash = sha256.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2")); // hex lowercase
            return sb.ToString(); // ví dụ: "a3f1c2... (64 ký tự)"
        }
    }
}
