using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class CuocTroChuyen
    {
        [Key]
        public string MaCuocTroChuyen { get; set; } = Guid.NewGuid().ToString();

        public DateTime ThoiGianTao { get; set; } = DateTime.UtcNow;

        public bool IsEmpty { get; set; } = true; // ✅ THÊM DÒNG NÀY

        public ICollection<TinNhan>? TinNhans { get; set; }

        public ICollection<NguoiThamGia>? NguoiThamGias { get; set; }
    }
}

