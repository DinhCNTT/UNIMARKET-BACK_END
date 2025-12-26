namespace UniMarket.DTO
{
    public class PostFilterRequest
    {
        public int Page { get; set; } = 1;
        public int Limit { get; set; } = 10;
        public string? SearchTerm { get; set; }

        // 👇 THÊM DÒNG NÀY ĐỂ FIX LỖI CAROUSEL
        public int? CategoryId { get; set; }

        public string? CategoryGroup { get; set; } // Danh mục cha (Điện thoại, Laptop...)
        public string? SubCategory { get; set; }   // Danh mục con (iPhone, Samsung...)
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public int? ProvinceId { get; set; }
        public int? DistrictId { get; set; }
        public string? SortOrder { get; set; } = "newest";
        public string? ProvinceName { get; set; }
        public string? DistrictName { get; set; }

        // 🔥 Quan trọng: Chuỗi JSON chứa bộ lọc Mongo (VD: '{"MauSac": "Đỏ", "Ram": "8GB"}')
        public string? AdvancedFilters { get; set; }
        public bool? HasVideo { get; set; }
    }
}