using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using Newtonsoft.Json;  // Đảm bảo bạn đã thêm thư viện này

namespace UniMarket.Models
{
    public class AnhTinDang
    {
        [Key]
        public int MaAnh { get; set; }

        [Required]
        [DisplayName("Mã tin đăng")]
        public int MaTinDang { get; set; }

        [Required(ErrorMessage = "Đường dẫn ảnh không được để trống.")]
        [StringLength(255, ErrorMessage = "Đường dẫn ảnh không được vượt quá 255 ký tự.")]
        [DisplayName("Đường dẫn ảnh")]
        public string DuongDan { get; set; }

        [DisplayName("Thứ tự ảnh")]
        public int Order { get; set; }

        [ForeignKey("MaTinDang")]
        [JsonIgnore]  // Bỏ qua thuộc tính này khi chuyển đổi đối tượng thành JSON để tránh vòng lặp
        public TinDang? TinDang { get; set; }
    }
}
