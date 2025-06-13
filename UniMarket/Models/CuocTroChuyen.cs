using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class CuocTroChuyen
    {
        [Key]
        public string MaCuocTroChuyen { get; set; } = Guid.NewGuid().ToString();

        public DateTime ThoiGianTao { get; set; } = DateTime.UtcNow;

        public bool IsEmpty { get; set; } = true;
        public int MaTinDang { get; set; }  // lưu mã tin đăng

        public string? TieuDeTinDang { get; set; }  // lưu tạm tiêu đề

        public string? AnhDaiDienTinDang { get; set; } // lưu tạm ảnh đại diện

        public decimal GiaTinDang { get; set; }  // lưu tạm giá

        public ICollection<TinNhan>? TinNhans { get; set; }

        public ICollection<NguoiThamGia>? NguoiThamGias { get; set; }
        // 🆕 Trường mới để kiểm tra trạng thái chặn
        public bool IsBlocked { get; set; } = false;
    }
}

