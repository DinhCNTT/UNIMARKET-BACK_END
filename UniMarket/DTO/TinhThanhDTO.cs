namespace UniMarket.DTO
{
    public class TinhThanhDTO
    {
        public int MaTinhThanh { get; set; }
        public string TenTinhThanh { get; set; }
        public List<QuanHuyenDTO>? QuanHuyens { get; set; }
    }
}
