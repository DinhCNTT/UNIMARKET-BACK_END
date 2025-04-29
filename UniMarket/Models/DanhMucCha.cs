using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace UniMarket.Models
{
    public class DanhMucCha
    {
        [Key]
        public int MaDanhMucCha { get; set; }

        [Required, StringLength(100)]
        [Display(Name = "Tên danh mục cha")]
        public string TenDanhMucCha { get; set; }

        [Display(Name = "Danh sách danh mục con")]
        public List<DanhMuc>? DanhMucs { get; set; } // ✅ Cho phép rỗng khi chưa có danh mục con

        [Display(Name = "Ảnh danh mục cha")]
        public string? AnhDanhMucCha { get; set; } // ✅ Lưu đường dẫn ảnh danh mục cha
        // Thêm thuộc tính Icon vào DanhMucCha
        [Display(Name = "Icon danh mục cha")]
        public string? Icon { get; set; } // Lưu đường dẫn icon

    }
}
