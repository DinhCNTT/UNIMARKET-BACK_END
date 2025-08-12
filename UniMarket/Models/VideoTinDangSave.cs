using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel;

namespace UniMarket.Models
{
    public class VideoTinDangSave
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [DisplayName("Mã lưu video")]
        public int MaVideoSave { get; set; }

        [Required]
        [DisplayName("Mã tin đăng")]
        public int MaTinDang { get; set; }

        [Required]
        [DisplayName("Mã người lưu")]
        public string MaNguoiDung { get; set; }

        [DisplayName("Ngày lưu")]
        public DateTime NgayLuu { get; set; } = DateTime.Now;

        // Khóa ngoại tới TinDang
        [ForeignKey("MaTinDang")]
        public TinDang? TinDang { get; set; }

        // Khóa ngoại tới User
        [ForeignKey("MaNguoiDung")]
        public ApplicationUser? NguoiDung { get; set; }
    }
}
