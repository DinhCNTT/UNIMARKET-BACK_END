using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace UniMarket.Models
{
    public enum TrangThaiTinDang
    {
        ChoDuyet = 0,  // Chờ Duyệt
        DaDuyet = 1,   // Đã Duyệt
        TuChoi = 2     // Từ Chối
    }
    public class TinDang
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int MaTinDang { get; set; }

        [Required]
        [DisplayName("Mã người bán")]
        public string MaNguoiBan { get; set; }

        [Required]
        [DisplayName("Mã danh mục")]
        public int MaDanhMuc { get; set; }

        [Required(ErrorMessage = "Tiêu đề không được để trống.")]
        [StringLength(200, ErrorMessage = "Tiêu đề không được vượt quá 200 ký tự.")]
        [DisplayName("Tiêu đề")]
        public string TieuDe { get; set; }

        [DisplayName("Mô tả")]
        public string? MoTa { get; set; }

        [Required(ErrorMessage = "Giá sản phẩm không được để trống.")]
        [Range(0, double.MaxValue, ErrorMessage = "Giá sản phẩm phải lớn hơn hoặc bằng 0.")]
        [DisplayName("Giá bán")]
        public decimal Gia { get; set; }

        [DisplayName("Có thể thương lượng")]
        public bool CoTheThoaThuan { get; set; } = false;

        [Required(ErrorMessage = "Tình trạng sản phẩm không được để trống.")]
        [RegularExpression("Moi|DaSuDung", ErrorMessage = "Tình trạng chỉ nhận giá trị 'Moi' hoặc 'DaSuDung'.")]
        [DisplayName("Tình trạng")]
        public string TinhTrang { get; set; }

        [Required(ErrorMessage = "Địa chỉ không được để trống.")]
        [StringLength(255, ErrorMessage = "Địa chỉ không được vượt quá 255 ký tự.")]
        [DisplayName("Địa chỉ liên hệ")]
        public string DiaChi { get; set; }

        [DisplayName("Mã tỉnh/thành")]
        public int? MaTinhThanh { get; set; }

        [DisplayName("Mã quận/huyện")]
        public int? MaQuanHuyen { get; set; }

        [DisplayName("Ngày đăng")]
        public DateTime NgayDang { get; set; } = DateTime.Now;

        [DisplayName("Ngày cập nhật")]
        public DateTime? NgayCapNhat { get; set; }

        [Required]
        [DisplayName("Trạng thái tin")]
        public TrangThaiTinDang TrangThai { get; set; } = TrangThaiTinDang.ChoDuyet; // Mặc định là Chờ duyệt

        [ForeignKey("MaDanhMuc")]
        public DanhMuc? DanhMuc { get; set; }

        [ForeignKey("MaNguoiBan")]
        public ApplicationUser? NguoiBan { get; set; }

        [ForeignKey("MaTinhThanh")]
        public TinhThanh? TinhThanh { get; set; }

        [ForeignKey("MaQuanHuyen")]
        public QuanHuyen? QuanHuyen { get; set; }

        public ICollection<AnhTinDang>? AnhTinDangs { get; set; }
    }


}
