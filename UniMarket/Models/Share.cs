using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public enum ShareDisplayMode
    {
        TinDang = 1, // hiển thị chi tiết tin đăng (ảnh + mô tả + video nếu có)
        Video = 2    // hiển thị video feed (TikTok style)
    }

    public class Share
    {
        [Key]
        public int ShareId { get; set; }

        [Required, StringLength(450)]
        public string UserId { get; set; }

        [Required]
        public ShareType ShareType { get; set; }   // Chat / SocialMedia

        [Required]
        public ShareTargetType TargetType { get; set; } // TinDang / Video

        [Required]
        public ShareDisplayMode DisplayMode { get; set; }

        public int? TinDangId { get; set; }
        public string? MaCuocTroChuyen { get; set; }

        [StringLength(500)]
        public string? ShareLink { get; set; }

        [StringLength(100)]
        public string? Platform { get; set; }

        public DateTime SharedAt { get; set; } = DateTime.UtcNow;

        // 🆕 Metadata preview
        [StringLength(200)]
        public string? PreviewTitle { get; set; }

        [StringLength(500)]
        public string? PreviewImage { get; set; }

        [StringLength(500)]
        public string? PreviewVideo { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [ForeignKey("TinDangId")]
        public TinDang? TinDang { get; set; }

        [ForeignKey("MaCuocTroChuyen")]
        public CuocTroChuyenSocial? CuocTroChuyenSocial { get; set; }

    }
}
