using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace UniMarket.Models
{
    public class QuanHuyen
    {
        [Key]
        public int MaQuanHuyen { get; set; }

        [Required(ErrorMessage = "Tên quận/huyện không được để trống.")]
        [StringLength(100, ErrorMessage = "Tên quận/huyện không được vượt quá 100 ký tự.")]
        [DisplayName("Tên quận/huyện")]
        public string TenQuanHuyen { get; set; }

        [Required]
        [DisplayName("Mã tỉnh/thành")]
        public int MaTinhThanh { get; set; }

        [ForeignKey("MaTinhThanh")]
        public TinhThanh? TinhThanh { get; set; }
    }
}
