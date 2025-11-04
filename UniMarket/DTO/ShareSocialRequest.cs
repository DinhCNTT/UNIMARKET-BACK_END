namespace UniMarket.DTO
{
    using System.ComponentModel.DataAnnotations;
    using UniMarket.Models; // để dùng ShareDisplayMode

    public class ShareSocialRequest
    {
        [Required]
        public int TinDangId { get; set; }

        [Required, StringLength(100)]
        public string Platform { get; set; } = null!;  // "Facebook", "Zalo", "Twitter"...

        [Required]
        public ShareDisplayMode DisplayMode { get; set; } // TinDang = 1, Video = 2
        public int? Index { get; set; }  // ✅ mới thêm
    }
}
