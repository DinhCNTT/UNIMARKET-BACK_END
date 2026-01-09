using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace UniMarket.Services.PriceAnalysis
{
    // DTO trả về cho tính năng Gợi ý giá khi Search
    public class SearchPriceSuggestion
    {
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal AveragePrice { get; set; }
        public int SampleSize { get; set; }
    }

    public class PriceAnalysisService
    {
        private readonly ApplicationDbContext _context;
        private readonly TinDangDetailService _mongoService;

        public PriceAnalysisService(ApplicationDbContext context, TinDangDetailService mongoService)
        {
            _context = context;
            _mongoService = mongoService;
        }

        // =================================================================================
        // 1. TÍNH NĂNG MỚI: GỢI Ý KHOẢNG GIÁ THEO TỪ KHÓA (Dùng cho API Search)
        // =================================================================================
        public async Task<SearchPriceSuggestion?> GetPriceSuggestionByKeywordAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return null;

            // Lấy mẫu giá từ SQL (Lấy 100 tin mới nhất khớp từ khóa để tính toán nhanh)
            // Không join MongoDB ở đây để tối ưu tốc độ cho Search API
            var rawPrices = await _context.TinDangs
                .AsNoTracking()
                .Where(p => p.TrangThai == TrangThaiTinDang.DaDuyet
                         && p.Gia > 0
                         && p.TieuDe.Contains(keyword))
                .OrderByDescending(p => p.NgayDang)
                .Take(100)
                .Select(p => p.Gia)
                .ToListAsync();

            if (rawPrices.Count < 3) return null; // Dữ liệu quá ít, không gợi ý

            // Gọi hàm tính toán chung
            return CalculateIQR(rawPrices);
        }

        // =================================================================================
        // 2. TÍNH NĂNG CŨ: PHÂN TÍCH GIÁ CHI TIẾT 1 BÀI ĐĂNG (Deep Analysis)
        // =================================================================================
        public async Task<MarketAnalysisResult> AnalyzePriceAsync(int postId)
        {
            // A. LẤY TIN GỐC TỪ SQL
            var currentPost = await _context.TinDangs
                .Include(p => p.DanhMuc)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.MaTinDang == postId);

            if (currentPost == null) return new MarketAnalysisResult { IsSuccess = false };

            // Check danh mục (Chỉ áp dụng cho điện thoại)
            string categoryName = currentPost.DanhMuc?.TenDanhMuc?.ToLower() ?? "";
            if (!categoryName.Contains("điện thoại") && !categoryName.Contains("phone") && !categoryName.Contains("smartphone"))
            {
                return new MarketAnalysisResult { IsSuccess = false };
            }

            // B. LẤY CHI TIẾT CẤU HÌNH TỪ MONGODB
            ProductSpecDTO currentSpecs = new ProductSpecDTO();
            try
            {
                var mongoDetail = await _mongoService.GetByMaTinDangAsync(postId);
                if (mongoDetail != null && mongoDetail.ChiTiet != null)
                {
                    var json = mongoDetail.ChiTiet.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson });
                    currentSpecs = JsonConvert.DeserializeObject<ProductSpecDTO>(json) ?? new ProductSpecDTO();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI ERROR] Lỗi parse chi tiết tin gốc: {ex.Message}");
            }

            Console.WriteLine($"\n🔍 [AI HYBRID] Phân tích: {currentPost.TieuDe} (ID: {postId})");

            // C. LẤY DANH SÁCH ỨNG VIÊN TỪ SQL (Sơ loại)
            var candidates = await _context.TinDangs
                .AsNoTracking()
                .Where(p => p.MaDanhMuc == currentPost.MaDanhMuc
                            && p.TrangThai == TrangThaiTinDang.DaDuyet
                            && p.Gia > 0
                            && p.TinhTrang == currentPost.TinhTrang
                            && p.MaTinDang != postId) // Loại trừ chính nó
                .Select(p => new { p.MaTinDang, p.Gia, p.TieuDe, p.TinhTrang })
                .ToListAsync();

            var validPrices = new List<decimal>();

            // Kiểm tra tin gốc có đủ dữ liệu để so sánh chính xác không
            bool hasStrictData = !string.IsNullOrEmpty(currentSpecs.Hang)
                              && !string.IsNullOrEmpty(currentSpecs.DongMay)
                              && currentSpecs.DongMay != "Khác"
                              && !string.IsNullOrEmpty(currentSpecs.DungLuong);

            // D. MATCHING LOOP (Kết hợp MongoDB cho từng ứng viên)
            foreach (var post in candidates)
            {
                try
                {
                    // Lấy chi tiết ứng viên từ Mongo
                    var candidateDetail = await _mongoService.GetByMaTinDangAsync(post.MaTinDang);
                    ProductSpecDTO targetSpecs = new ProductSpecDTO();

                    if (candidateDetail != null && candidateDetail.ChiTiet != null)
                    {
                        var json = candidateDetail.ChiTiet.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson });
                        targetSpecs = JsonConvert.DeserializeObject<ProductSpecDTO>(json) ?? new ProductSpecDTO();
                    }

                    bool isMatch = false;

                    // Layer 1: So sánh chính xác (Strict)
                    if (hasStrictData)
                    {
                        if (!IsStringMatch(currentSpecs.Hang, targetSpecs.Hang)) continue;
                        if (!IsStringMatch(currentSpecs.DongMay, targetSpecs.DongMay)) continue;
                        if (!IsStringMatch(currentSpecs.DungLuong, targetSpecs.DungLuong)) continue;
                        isMatch = true;
                    }
                    // Layer 2: So sánh tiêu đề (Fallback)
                    else
                    {
                        if (!string.IsNullOrEmpty(currentSpecs.Hang) && !string.IsNullOrEmpty(targetSpecs.Hang))
                        {
                            if (!IsStringMatch(currentSpecs.Hang, targetSpecs.Hang)) continue;
                        }

                        var currentSig = ExtractModelSignature(currentPost.TieuDe);
                        var targetSig = ExtractModelSignature(post.TieuDe);

                        if (IsSimilarModel(currentSig, targetSig))
                        {
                            isMatch = true;
                        }
                    }

                    if (isMatch)
                    {
                        validPrices.Add(post.Gia);
                    }
                }
                catch { /* Bỏ qua lỗi parsing lẻ tẻ */ }
            }
            validPrices.Add(currentPost.Gia);

            // E. TÍNH TOÁN KẾT QUẢ CUỐI CÙNG
            if (validPrices.Count < 1)
            {
                return new MarketAnalysisResult { IsSuccess = false };
            }

            // 🔥 SỬ DỤNG HÀM TÍNH TOÁN CHUNG (REUSABLE LOGIC)
            var stats = CalculateIQR(validPrices);

            // Tính độ lệch và trạng thái
            double diffPercent = 0;
            if (stats.AveragePrice > 0)
                diffPercent = (double)((currentPost.Gia - stats.AveragePrice) / stats.AveragePrice) * 100;

            string status = "Giá hợp lý";
            if (diffPercent < -5) status = "Rẻ hơn thị trường";
            else if (diffPercent > 5) status = "Cao hơn thị trường";
            // Tính toán dữ liệu biểu đồ cột (Chia làm 5 cột)
            var histogram = CalculateHistogram(validPrices, 5);

            Console.WriteLine($"✅ SUCCESS: Thị trường [{stats.MinPrice:N0} - {stats.MaxPrice:N0}], TB: {stats.AveragePrice:N0}");

            return new MarketAnalysisResult
            {
                IsSuccess = true,
                MinPrice = stats.MinPrice,
                MaxPrice = stats.MaxPrice,
                AveragePrice = stats.AveragePrice,
                CurrentPrice = currentPost.Gia,
                Status = status,
                DifferencePercent = Math.Round(diffPercent, 1),
                SampleSize = stats.SampleSize,
                HistogramData = histogram
            };
        }

        // =================================================================================
        // 3. CORE LOGIC: TÍNH TOÁN IQR & LỌC GIÁ ẢO (DÙNG CHUNG)
        // =================================================================================
        private SearchPriceSuggestion CalculateIQR(List<decimal> prices)
        {
            if (prices == null || !prices.Any())
                return new SearchPriceSuggestion();

            prices.Sort();
            int n = prices.Count;
            List<decimal> marketPrices;

            // Thuật toán IQR để loại bỏ ngoại lai (Outliers)
            if (n >= 4)
            {
                decimal q1 = prices[n / 4];
                decimal q3 = prices[n * 3 / 4];
                decimal iqr = q3 - q1;
                // Lọc các giá nằm trong khoảng [Q1 - 1.5*IQR, Q3 + 1.5*IQR]
                marketPrices = prices.Where(p => p >= q1 - 1.5m * iqr && p <= q3 + 1.5m * iqr).ToList();
            }
            else
            {
                marketPrices = prices;
            }

            // Fallback nếu lọc xong bị rỗng
            if (!marketPrices.Any()) marketPrices = prices;

            decimal min = marketPrices.Min();
            decimal max = marketPrices.Max();
            decimal avg = marketPrices.Average();

            // Mở rộng range nhẹ nếu min == max để biểu đồ đẹp hơn
            if (min == max)
            {
                min = min * 0.9m;
                max = max * 1.1m;
            }

            return new SearchPriceSuggestion
            {
                MinPrice = min,
                MaxPrice = max,
                AveragePrice = avg,
                SampleSize = marketPrices.Count
            };
        }

        // =================================================================================
        // 4. HELPERS (REGEX, CLEAN TEXT) - GIỮ NGUYÊN
        // =================================================================================
        private bool IsStringMatch(string? s1, string? s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return false;
            return string.Equals(s1.Trim(), s2.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private readonly string[] _modelKeywords = new[] {
            "pro", "max", "mini", "plus", "se", "ultra", "note", "fold", "flip", "fe", "5g", "4g"
        };

        private string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            string clean = title.ToLower();
            clean = Regex.Replace(clean, @"\d+[x]*%", " ");
            clean = Regex.Replace(clean, @"\d+\s*(tháng|th|thang|năm|ngày)", " ");
            clean = Regex.Replace(clean, @"pin\s*[\d%x]+", " ");
            clean = Regex.Replace(clean, @"\d+\s*(hz|w|wat|sim|mp|mah)", " ");
            clean = Regex.Replace(clean, @"\d+\s*(gb|tb)", " ");
            clean = Regex.Replace(clean, @"(chính hãng|xách tay|quốc tế|lock|vn/a|ll/a|fullbox|zin|keng|đẹp|cũ|mới|pass|bán|giá|rẻ)", " ");
            return clean;
        }

        private ModelSignature ExtractModelSignature(string title)
        {
            var sig = new ModelSignature();
            string cleanTitle = CleanTitle(title);

            var numberMatches = Regex.Matches(cleanTitle, @"\d+");
            foreach (Match match in numberMatches)
            {
                if (int.TryParse(match.Value, out int num))
                {
                    bool isStorage = num == 32 || num == 64 || num == 128 || num == 256 || num == 512;
                    bool isYear = num > 2000;
                    if (!isStorage && !isYear && num >= 3) sig.Numbers.Add(match.Value);
                }
            }
            foreach (var key in _modelKeywords)
            {
                if (Regex.IsMatch(cleanTitle, $@"\b{key}\b")) sig.Keywords.Add(key);
            }
            return sig;
        }

        private bool IsSimilarModel(ModelSignature source, ModelSignature target)
        {
            if (source.Numbers.Count != target.Numbers.Count) return false;
            foreach (var num in source.Numbers) if (!target.Numbers.Contains(num)) return false;

            foreach (var key in source.Keywords) if (!target.Keywords.Contains(key)) return false;
            foreach (var key in target.Keywords) if (!source.Keywords.Contains(key)) return false;

            return true;
        }

        private class ModelSignature
        {
            public List<string> Numbers { get; set; } = new List<string>();
            public HashSet<string> Keywords { get; set; } = new HashSet<string>();
        }
        // =================================================================================
        // 5. [NEW] LOGIC CHIA BIỂU ĐỒ CỘT (HISTOGRAM)
        // =================================================================================
        private List<PriceBucket> CalculateHistogram(List<decimal> prices, int bucketCount = 5)
        {
            if (prices == null || !prices.Any()) return new List<PriceBucket>();

            decimal min = prices.Min();
            decimal max = prices.Max();

            // Trường hợp đặc biệt: Chỉ có 1 giá hoặc tất cả giá bằng nhau
            if (min == max)
            {
                return new List<PriceBucket> {
                    new PriceBucket { Min = min, Max = max, Count = prices.Count }
                };
            }

            // Tính độ rộng của mỗi cột (Bucket Size)
            decimal range = max - min;
            decimal step = range / bucketCount;

            var buckets = new List<PriceBucket>();

            for (int i = 0; i < bucketCount; i++)
            {
                // Tính khoảng giá cho cột thứ i
                decimal bucketMin = min + (step * i);
                decimal bucketMax = min + (step * (i + 1));

                // Nếu là cột cuối cùng, mở rộng max ra một chút để chắc chắn bao gồm giá trị lớn nhất
                if (i == bucketCount - 1) bucketMax = max + 1;

                // Đếm số lượng tin đăng nằm trong khoảng giá này
                int count = prices.Count(p => p >= bucketMin && p < bucketMax);

                buckets.Add(new PriceBucket
                {
                    Min = Math.Round(bucketMin, 0),
                    Max = Math.Round(bucketMax, 0),
                    Count = count
                });
            }

            return buckets;
        }
    }
    public class MarketAnalysisResult
    {
        public bool IsSuccess { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public string? Status { get; set; } // "Rẻ hơn", "Cao hơn"...
        public double DifferencePercent { get; set; }
        public int SampleSize { get; set; }

        // 🔥 Trường mới thêm cho biểu đồ cột (Histogram)
        public List<PriceBucket> HistogramData { get; set; } = new List<PriceBucket>();
    }

    public class PriceBucket
    {
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public int Count { get; set; }
    }
}