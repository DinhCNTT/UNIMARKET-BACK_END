namespace UniMarket.DTO
{
    public class VideoSearchResultDto
    {
        public int MaTinDang { get; set; }
        public string TieuDe { get; set; }
        public string VideoUrl { get; set; }
        public decimal Gia { get; set; }
        public string DiaChi { get; set; }
        public string TinhThanh { get; set; }
        public string QuanHuyen { get; set; }
        public int SoTym { get; set; }
        public int SoBinhLuan { get; set; }
        public UserSummaryDto NguoiDang { get; set; }
        public bool IsLiked { get; set; }
        public string ThoiGianHienThi { get; set; }
    }
    public class UserSummaryDto
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string AvatarUrl { get; set; }
    }
}
