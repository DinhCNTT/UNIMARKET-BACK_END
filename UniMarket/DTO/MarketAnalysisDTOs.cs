namespace UniMarket.DTO
{
    public class ProductSpecDTO
    {
        public string? Hang { get; set; }       // Apple, Samsung...
        public string? DongMay { get; set; }    // ✅ THÊM MỚI: iPhone 13 Pro Max, Galaxy S23...
        public string? MauSac { get; set; }
        public string? DungLuong { get; set; }  // 128GB, 256GB...
        public string? BaoHanh { get; set; }
        public string? XuatXu { get; set; }
    }

    public class MarketAnalysisResult
    {
        public bool IsSuccess { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public string Status { get; set; }
        public double DifferencePercent { get; set; }
        public int SampleSize { get; set; }
    }
}