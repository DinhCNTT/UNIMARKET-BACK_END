using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
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

        [ForeignKey("MaCuocTroChuyen")]
        public CuocTroChuyen? CuocTroChuyen { get; set; }

        [ForeignKey("MaNguoiGui")]
        public ApplicationUser? NguoiGui { get; set; }
    }
}
