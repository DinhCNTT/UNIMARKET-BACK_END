using UAParser;

namespace UniMarket.Helpers
{
    public static class UserAgentHelper
    {
        private static readonly Parser _parser = Parser.GetDefault();

        public static string GetDeviceName(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return "Unknown Device";

            var clientInfo = _parser.Parse(userAgent);

            // Hệ điều hành
            var os = clientInfo.OS.ToString(); // ví dụ: Windows 10, iOS 17, Android 12
            if (string.IsNullOrEmpty(os)) os = "Unknown OS";

            // Trình duyệt
            var browser = clientInfo.UA.Family; // Chrome, Safari, Firefox...
            var browserVersion = clientInfo.UA.Major; // chỉ lấy số major (139 thay vì 139.0.0.0)

            if (string.IsNullOrEmpty(browser)) browser = "Unknown Browser";

            var browserDisplay = string.IsNullOrEmpty(browserVersion)
                ? browser
                : $"{browser} {browserVersion}";

            // Thiết bị
            var device = clientInfo.Device.Family; // iPhone, Other, Samsung...
            if (string.IsNullOrEmpty(device) || device == "Other")
                device = "PC"; // fallback khi không phải mobile

            // Trả về format: "Windows 10 - Chrome 139"
            return $"{os} - {browserDisplay}";
        }
    }
}
