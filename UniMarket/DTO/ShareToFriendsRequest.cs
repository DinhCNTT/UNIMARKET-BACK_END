using UniMarket.Models;

namespace UniMarket.DTO
{
    public class ShareToFriendsRequest
    {
        public List<string> TargetUserIds { get; set; } = new();
        public int? TinDangId { get; set; }
        public string? PreviewTitle { get; set; }
        public string? PreviewImage { get; set; }
        public string? PreviewVideo { get; set; }
        public string? ExtraText { get; set; }

        // 🔹 Bắt buộc thêm để backend phân biệt
        public ChatType ChatType { get; set; } = ChatType.Social;

        // 🔹 Tuỳ chọn: dùng để biết hiển thị dạng Card / Inline / FullPreview
        public ShareDisplayMode DisplayMode { get; set; } = ShareDisplayMode.TinDang;

    }
}
