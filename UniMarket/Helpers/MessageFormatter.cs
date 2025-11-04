using System.Text.RegularExpressions;

namespace YourNamespace.Helpers
{
    public static class MessageFormatter
    {
        public static string Format(string? noiDung, string? senderName)
        {
            if (string.IsNullOrEmpty(noiDung)) return "";

            // Regex match [ShareId:xxx(:video|:image)?]
            var match = Regex.Match(noiDung, @"\[ShareId:(\d+)(?::(\w+))?\]");
            if (match.Success)
            {
                var typeHint = match.Groups[2].Value.ToLowerInvariant();
                if (typeHint == "video")
                    return $"{senderName} đã gửi 1 video";
                if (typeHint == "image")
                    return $"{senderName} đã gửi 1 ảnh";
                return $"{senderName} đã gửi 1 nội dung được chia sẻ";
            }

            return noiDung; // Nếu k có ShareId thì trả raw text
        }
    }
}
