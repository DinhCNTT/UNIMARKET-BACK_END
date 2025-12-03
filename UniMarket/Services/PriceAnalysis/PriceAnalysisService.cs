using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;

namespace UniMarket.Services.PriceAnalysis
{
    public class PriceAnalysisService
    {
        private readonly ApplicationDbContext _context;

        public PriceAnalysisService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<MarketAnalysisResult> AnalyzePriceAsync(int postId)
        {
            // 1. LẤY TIN GỐC KÈM DANH MỤC
            var currentPost = await _context.TinDangs
                .Include(p => p.DanhMuc)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.MaTinDang == postId);

            if (currentPost == null) return new MarketAnalysisResult { IsSuccess = false };

            // 🛑 LỚP BẢO VỆ 1: CHECK DANH MỤC (CATEGORY GUARD)
            string categoryName = currentPost.DanhMuc?.TenDanhMuc?.ToLower() ?? "";
            if (!categoryName.Contains("điện thoại") && !categoryName.Contains("phone") && !categoryName.Contains("smartphone"))
            {
                return new MarketAnalysisResult { IsSuccess = false };
            }

            // 2. PARSE JSON THÔNG TIN CHI TIẾT
            ProductSpecDTO currentSpecs = new ProductSpecDTO();
            if (!string.IsNullOrEmpty(currentPost.ThongTinChiTiet))
            {
                try
                {
                    currentSpecs = JsonConvert.DeserializeObject<ProductSpecDTO>(currentPost.ThongTinChiTiet) ?? new ProductSpecDTO();
                }
                catch { }
            }

            Console.WriteLine($"\n🔍 [AI STABLE & STRICT] Phân tích: {currentPost.TieuDe} (ID: {postId})");

            // 3. LẤY DANH SÁCH ỨNG VIÊN (QUERY DATABASE)
            // 🔥 FIX: Lấy TOÀN BỘ tin cùng loại (BAO GỒM CẢ TIN HIỆN TẠI)
            // Để tin hiện tại cũng tham gia vào việc tính toán giá trung bình -> Giúp giá ổn định
            var candidates = await _context.TinDangs
                .AsNoTracking()
                .Where(p => p.MaDanhMuc == currentPost.MaDanhMuc
                            // ❌ ĐÃ XÓA DÒNG: && p.MaTinDang != postId
                            && p.TrangThai == TrangThaiTinDang.DaDuyet
                            && p.Gia > 0
                            // Chỉ so sánh cùng tình trạng (Cũ so với Cũ, Mới so với Mới)
                            && p.TinhTrang == currentPost.TinhTrang)
                .Select(p => new { p.MaTinDang, p.Gia, p.ThongTinChiTiet, p.TieuDe, p.TinhTrang })
                .ToListAsync();

            Console.WriteLine($"   => Tìm thấy {candidates.Count} tin sơ bộ (đã gộp tin hiện tại).");

            var validPrices = new List<decimal>();

            // --- CHIẾN THUẬT SO SÁNH (MATCHING STRATEGY - GIỮ NGUYÊN LOGIC CŨ) ---

            // Kiểm tra xem tin gốc có dữ liệu chuẩn không?
            bool hasStrictData = !string.IsNullOrEmpty(currentSpecs.Hang)
                              && !string.IsNullOrEmpty(currentSpecs.DongMay)
                              && currentSpecs.DongMay != "Khác"
                              && !string.IsNullOrEmpty(currentSpecs.DungLuong);

            foreach (var post in candidates)
            {
                try
                {
                    var targetSpecs = JsonConvert.DeserializeObject<ProductSpecDTO>(post.ThongTinChiTiet ?? "{}");
                    if (targetSpecs == null) continue;

                    bool isMatch = false;

                    // 🛡️ LỚP 2: SO SÁNH CHÍNH XÁC (STRICT MATCHING - Ưu tiên)
                    if (hasStrictData)
                    {
                        // 1. Cùng Hãng
                        if (!IsStringMatch(currentSpecs.Hang, targetSpecs.Hang)) continue;

                        // 2. Cùng Dòng Máy (Model)
                        if (!IsStringMatch(currentSpecs.DongMay, targetSpecs.DongMay)) continue;

                        // 3. Cùng Dung Lượng (Storage)
                        if (!IsStringMatch(currentSpecs.DungLuong, targetSpecs.DungLuong)) continue;

                        // Khớp hết -> Lấy
                        isMatch = true;
                    }
                    // 🛡️ LỚP 3: SO SÁNH TIÊU ĐỀ (FALLBACK - Dự phòng)
                    else
                    {
                        var currentSig = ExtractModelSignature(currentPost.TieuDe);
                        var targetSig = ExtractModelSignature(post.TieuDe);

                        // Vẫn bắt buộc cùng Hãng (nếu có thông tin)
                        if (!string.IsNullOrEmpty(currentSpecs.Hang) && !string.IsNullOrEmpty(targetSpecs.Hang))
                        {
                            if (!IsStringMatch(currentSpecs.Hang, targetSpecs.Hang)) continue;
                        }

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
                catch { }
            }

            // Test Mode: Chỉ cần 1 tin là tính (Chính là tin hiện tại nếu nó khớp chính mình)
            if (validPrices.Count < 1)
            {
                Console.WriteLine("❌ KẾT QUẢ: Không tìm thấy dữ liệu thị trường.");
                return new MarketAnalysisResult { IsSuccess = false };
            }

            // 4. THUẬT TOÁN IQR (LOẠI BỎ GIÁ ẢO)
            validPrices.Sort();
            int n = validPrices.Count;
            List<decimal> marketPrices;

            // Nếu dữ liệu đủ lớn (>=4), dùng IQR để lọc giá ảo (spam)
            if (n >= 4)
            {
                decimal q1 = validPrices[n / 4];
                decimal q3 = validPrices[n * 3 / 4];
                decimal iqr = q3 - q1;
                marketPrices = validPrices.Where(p => p >= q1 - 1.5m * iqr && p <= q3 + 1.5m * iqr).ToList();
            }
            else
            {
                marketPrices = validPrices;
            }

            if (!marketPrices.Any()) marketPrices = validPrices;

            // 5. TÍNH KẾT QUẢ (DỰA TRÊN TOÀN BỘ THỊ TRƯỜNG CỐ ĐỊNH)
            decimal marketMin = marketPrices.Min();
            decimal marketMax = marketPrices.Max();
            decimal marketAvg = marketPrices.Average();

            // Xử lý trường hợp Min = Max (chỉ có 1 mức giá hoặc 1 tin) -> Nới rộng ảo 1 chút để vẽ biểu đồ đẹp
            if (marketMin == marketMax)
            {
                marketMin = marketMin * 0.9m;
                marketMax = marketMax * 1.1m;
            }

            // So sánh giá tin hiện tại với giá trung bình thị trường
            double diffPercent = 0;
            if (marketAvg > 0)
                diffPercent = (double)((currentPost.Gia - marketAvg) / marketAvg) * 100;

            string status = "Giá hợp lý";
            if (diffPercent < -5) status = "Rẻ hơn thị trường";
            else if (diffPercent > 5) status = "Cao hơn thị trường";

            Console.WriteLine($"✅ SUCCESS: Thị trường [{marketMin:N0} - {marketMax:N0}], Phổ biến: {marketAvg:N0}");

            return new MarketAnalysisResult
            {
                IsSuccess = true,
                MinPrice = marketMin,
                MaxPrice = marketMax,
                AveragePrice = marketAvg, // Giá phổ biến này sẽ CỐ ĐỊNH cho mọi tin cùng loại
                CurrentPrice = currentPost.Gia,
                Status = status,
                DifferencePercent = Math.Round(diffPercent, 1),
                SampleSize = marketPrices.Count
            };
        }

        // =================================================================================
        // CÁC HÀM BỔ TRỢ (HELPER FUNCTIONS) - GIỮ NGUYÊN
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
    }
}