namespace UniMarket.DTO
{
    // 1. Khuôn mẫu hứng dữ liệu JSON từ cột ThongTinChiTiet
    public class ProductSpecDTO
    {
        // Các trường này khớp chính xác với JSON bạn lưu trong DB
        public string? Hang { get; set; }       // Apple, Samsung...
        public string? MauSac { get; set; }     // Đen, Trắng...
        public string? DungLuong { get; set; }  // 128GB, 256GB...
        public string? BaoHanh { get; set; }    // Hết bảo hành, Còn bảo hành...
    }

    // 2. Kết quả trả về cho Frontend (Giữ nguyên)
    public class MarketAnalysisResult
    {
        public bool IsSuccess { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public string Status { get; set; }          // "Rẻ hơn", "Cao hơn", "Hợp lý"
        public double DifferencePercent { get; set; }
        public int SampleSize { get; set; }
    }
}