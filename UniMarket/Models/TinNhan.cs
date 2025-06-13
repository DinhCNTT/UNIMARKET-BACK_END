using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public enum LoaiTinNhan
    {
        Text,
        Image,
        Video
    }

    public class TinNhan
    {
        [Key]
        public int MaTinNhan { get; set; }

        [Required]
        public string MaCuocTroChuyen { get; set; }

        [Required]
        public string MaNguoiGui { get; set; }

        [Required]
        public string NoiDung { get; set; }

        public DateTime ThoiGianGui { get; set; } = DateTime.UtcNow;

        public bool DaXem { get; set; } = false;

        public DateTime? ThoiGianXem { get; set; }

        // 🆕 Loại tin nhắn: Text / Image / Video
        public LoaiTinNhan Loai { get; set; } = LoaiTinNhan.Text;

        // 🆕 Nếu là ảnh hoặc video, lưu URL tại đây
        public string? MediaUrl { get; set; }
        // Trường mới để kiểm soát hiển thị tin nhắn
        public bool IsVisible { get; set; } = true;  // Mặc định là hiển thị

        [ForeignKey("MaCuocTroChuyen")]
        public CuocTroChuyen? CuocTroChuyen { get; set; }

        [ForeignKey("MaNguoiGui")]
        public ApplicationUser? NguoiGui { get; set; }
    }
}
