namespace UniMarket.Services
{
    /// <summary>
    /// Cấu hình chiến lược fallback cho tìm kiếm sản phẩm
    /// Cho phép điều chỉnh hành vi fallback qua appsettings.json
    /// </summary>
    public class SearchFallbackConfig
    {
        /// <summary>Độ dài tối thiểu của keyword để sử dụng LIKE search (mặc định: 2)</summary>
        public int MinKeywordLength { get; set; } = 2;

        /// <summary>Bỏ qua keywords và tìm theo category khi không tìm thấy sản phẩm (mặc định: true)</summary>
        public bool EnableCategoryFallback { get; set; } = true;

        /// <summary>Sử dụng flexible search (OR logic) khi strict search (AND logic) không có kết quả (mặc định: true)</summary>
        public bool EnableFlexibleSearch { get; set; } = true;

        /// <summary>Độ dài tối thiểu của keyword trong flexible search (mặc định: 3)</summary>
        public int MinFlexibleKeywordLength { get; set; } = 3;

        /// <summary>Số sản phẩm tối thiểu được coi là "có kết quả" (mặc định: 1)</summary>
        public int MinResultThreshold { get; set; } = 1;

        /// <summary>Ưu tiên category search thay vì keyword search khi categoryId tồn tại (mặc định: false)</summary>
        public bool PreferCategoryOverKeywords { get; set; } = false;

        /// <summary>Cho phép bỏ qua keyword ngắn trong flexible search (mặc định: true)</summary>
        public bool SkipShortKeywordsInFlexible { get; set; } = true;
    }
}
