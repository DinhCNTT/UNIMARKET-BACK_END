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
            var currentPost = await _context.TinDangs
                .Include(p => p.DanhMuc)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.MaTinDang == postId);

            if (currentPost == null) return new MarketAnalysisResult { IsSuccess = false };

            // --- DEBUG LOG ---
            Console.WriteLine($"\n🔍 [AI SUPER CLEANER] Đang xử lý: {currentPost.TieuDe}");

            // 1. Query Database (Lấy rộng)
            var candidates = await _context.TinDangs
                .AsNoTracking()
                .Where(p => p.MaTinDang != postId
                            && p.TrangThai == TrangThaiTinDang.DaDuyet
                            && p.Gia > 0)
                .Select(p => new { p.MaTinDang, p.Gia, p.ThongTinChiTiet, p.TieuDe })
                .ToListAsync();

            // 2. TẠO CHỮ KÝ MODEL (Dựa trên Tiêu đề đã làm sạch cực kỹ)
            var currentSig = ExtractModelSignature(currentPost.TieuDe);
            Console.WriteLine($"   => Signature Gốc: [{string.Join(",", currentSig.Numbers)}] + [{string.Join(",", currentSig.Keywords)}]");

            var validPrices = new List<decimal>();

            foreach (var post in candidates)
            {
                // BỎ QUA CHECK JSON -> CHỈ DÙNG TIÊU ĐỀ ĐỂ TEST
                var targetSig = ExtractModelSignature(post.TieuDe);

                if (IsSimilarModel(currentSig, targetSig))
                {
                    Console.WriteLine($"   ✅ CHẤP NHẬN: {post.TieuDe} ({post.Gia:N0})");
                    validPrices.Add(post.Gia);
                }
            }

            // CHỈ CẦN 1 TIN LÀ TÍNH
            if (validPrices.Count < 1) return new MarketAnalysisResult { IsSuccess = false };

            // 3. TÍNH TOÁN
            validPrices.Sort();
            int n = validPrices.Count;
            decimal q1 = validPrices[n / 4];
            decimal q3 = validPrices[n * 3 / 4];
            decimal iqr = q3 - q1;

            var finalPrices = validPrices.Where(p => p >= q1 - 1.5m * iqr && p <= q3 + 1.5m * iqr).ToList();
            if (!finalPrices.Any()) finalPrices = validPrices;

            decimal avg = finalPrices.Average();
            double diffPercent = 0;
            if (avg > 0) diffPercent = (double)((currentPost.Gia - avg) / avg) * 100;

            string status = "Giá hợp lý";
            if (diffPercent < -5) status = "Rẻ hơn thị trường";
            else if (diffPercent > 5) status = "Cao hơn thị trường";

            return new MarketAnalysisResult
            {
                IsSuccess = true,
                MinPrice = finalPrices.Min(),
                MaxPrice = finalPrices.Max(),
                AveragePrice = avg,
                CurrentPrice = currentPost.Gia,
                Status = status,
                DifferencePercent = Math.Round(diffPercent, 1),
                SampleSize = finalPrices.Count
            };
        }

        // =================================================================================
        // 🔥 BỘ LỌC TỪ KHÓA & DỌN RÁC (SUPER CLEANER)
        // =================================================================================

        private readonly string[] _modelKeywords = new[] {
            "pro", "max", "mini", "plus", "se", "ultra",
            "note", "fold", "flip", "gaming", "5g", "4g"
        };

        // Hàm xóa sạch rác trong tiêu đề
        private string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            string clean = title.ToLower();

            // 1. Xóa phần trăm độ mới/pin (99%, 9x%, 85%...)
            clean = Regex.Replace(clean, @"\d+[x]*%", " ");

            // 2. Xóa thời gian bảo hành (6 tháng, 12 th...)
            clean = Regex.Replace(clean, @"\d+\s*(tháng|th|thang|năm|ngày)", " ");

            // 3. Xóa thông tin Pin (5000mah, pin 8x...)
            clean = Regex.Replace(clean, @"pin\s*[\d%x]+", " ");

            // 4. Xóa thông số kỹ thuật (gb, tb, hz, w, sim...)
            clean = Regex.Replace(clean, @"\d+\s*(gb|tb|hz|w|wat|sim|mp)", " ");

            // 5. Xóa các từ rác phổ biến trong mua bán
            clean = Regex.Replace(clean, @"(chính hãng|xách tay|quốc tế|lock|vn/a|ll/a|fullbox|zin|keng|đẹp|cũ|mới|pass|bán|giá|rẻ)", " ");

            return clean;
        }

        private ModelSignature ExtractModelSignature(string title)
        {
            var sig = new ModelSignature();

            // BƯỚC 1: Dọn rác
            string cleanTitle = CleanTitle(title);

            // BƯỚC 2: Bắt số đời máy
            var numberMatches = Regex.Matches(cleanTitle, @"\d+");
            foreach (Match match in numberMatches)
            {
                if (int.TryParse(match.Value, out int num))
                {
                    // Lọc số rác còn sót lại:
                    // - Bỏ qua 32, 64, 128... (dung lượng nếu lỡ sót)
                    // - Bỏ qua 99, 98, 95 (độ mới phổ biến nếu lỡ sót)
                    // - Bỏ qua năm 20xx

                    bool isStorage = num == 32 || num == 64 || num == 128 || num == 256 || num == 512;
                    bool isCommonPercent = num == 99 || num == 98 || num == 95 || num == 90;
                    bool isYear = num > 2000;

                    // Lấy số nếu nó không phải rác VÀ (lớn hơn 3 hoặc là số nhỏ nhưng có chữ iphone/samsung...)
                    // Để an toàn cho case này, tui lấy num >= 3
                    if (!isStorage && !isCommonPercent && !isYear && num >= 3)
                    {
                        sig.Numbers.Add(match.Value);
                    }
                }
            }

            // BƯỚC 3: Bắt từ khóa Hậu tố (Pro, Max...)
            // Quét trên title gốc để tránh bị Clean mất từ khóa (dù CleanTitle tui ko xóa Pro/Max nhưng cứ chắc ăn)
            string normalizedOriginal = title.ToLower();
            foreach (var key in _modelKeywords)
            {
                if (Regex.IsMatch(normalizedOriginal, $@"\b{key}\b"))
                {
                    sig.Keywords.Add(key);
                }
            }

            return sig;
        }

        private bool IsSimilarModel(ModelSignature source, ModelSignature target)
        {
            // 1. Số phải khớp (13 == 13)
            if (source.Numbers.Count != target.Numbers.Count) return false;
            foreach (var num in source.Numbers) if (!target.Numbers.Contains(num)) return false;

            // 2. Từ khóa phải khớp (Pro == Pro, Max == Max)
            foreach (var key in source.Keywords) if (!target.Keywords.Contains(key)) return false;
            foreach (var key in target.Keywords) if (!source.Keywords.Contains(key)) return false;

            return true;
        }

        private class ModelSignature
        {
            public List<string> Numbers { get; set; } = new List<string>();
            public HashSet<string> Keywords { get; set; } = new HashSet<string>();
        }

        private bool IsMatch(string? s1, string? s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return false;
            return string.Equals(s1.Trim(), s2.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}