namespace UniMarket.Models
{
    public class TinDangYeuThich
    {
        public int MaYeuThich { get; set; } // Khóa chính
        public int MaTinDang { get; set; } // Mã tin đăng
        public string MaNguoiDung { get; set; } // Mã người dùng (Foreign Key từ bảng Users)
        public DateTime NgayTao { get; set; } // Ngày tạo tin yêu thích

        // Navigation Properties
        public virtual TinDang TinDang { get; set; } // Mối quan hệ với TinDang
        public virtual ApplicationUser NguoiDung { get; set; } // Mối quan hệ với người dùng (ApplicationUser)
    }
}
