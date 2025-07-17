namespace UniMarket.DTO
{
    public class UserPostDto
    {
        public int MaTinDang { get; set; }
        public string TieuDe { get; set; }
        public decimal Gia { get; set; }
        public string? VideoUrl { get; set; }
        public string DiaChi { get; set; }
        public DateTime NgayDang { get; set; }
        public string TinhTrang { get; set; }
        public List<string> AnhDuongDans { get; set; } = new();
    }

}
