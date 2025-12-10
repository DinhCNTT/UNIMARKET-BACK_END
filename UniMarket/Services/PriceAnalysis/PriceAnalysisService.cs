using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;
using MongoDB.Bson; // Cần thêm
using MongoDB.Driver; // Cần thêm

namespace UniMarket.Services.PriceAnalysis
{
    public class PriceAnalysisService
    {
        private readonly ApplicationDbContext _context;
        private readonly TinDangDetailService _mongoService; // ✅ Inject Service Mongo

        public PriceAnalysisService(ApplicationDbContext context, TinDangDetailService mongoService)
        {
            _context = context;
            _mongoService = mongoService;
        }

        public async Task<MarketAnalysisResult> AnalyzePriceAsync(int postId)
        {
            // =========================================================
            // 1. LẤY TIN GỐC TỪ SQL (Cơ bản)
            // =========================================================
            var currentPost = await _context.TinDangs
                .Include(p => p.DanhMuc)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.MaTinDang == postId);

            if (currentPost == null) return new MarketAnalysisResult { IsSuccess = false };

            // 🛑 LỚP BẢO VỆ: CHECK DANH MỤC
            string categoryName = currentPost.DanhMuc?.TenDanhMuc?.ToLower() ?? "";
            if (!categoryName.Contains("điện thoại") && !categoryName.Contains("phone") && !categoryName.Contains("smartphone"))
            {
                return new MarketAnalysisResult { IsSuccess = false };
            }

            // =========================================================
            // 2. LẤY CHI TIẾT TỪ MONGODB (Thay vì SQL)
            // =========================================================
            ProductSpecDTO currentSpecs = new ProductSpecDTO();
            try
            {
                var mongoDetail = await _mongoService.GetByMaTinDangAsync(postId);
                if (mongoDetail != null && mongoDetail.ChiTiet != null)
                {
                    // Chuyển BSON -> JSON String -> DTO
                    // Lưu ý: Cần config ToJsonWriterSettings để output ra JSON chuẩn
                    var json = mongoDetail.ChiTiet.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson });
                    currentSpecs = JsonConvert.DeserializeObject<ProductSpecDTO>(json) ?? new ProductSpecDTO();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI ERROR] Lỗi parse chi tiết tin gốc: {ex.Message}");
            }

            Console.WriteLine($"\n🔍 [AI HYBRID] Phân tích: {currentPost.TieuDe} (ID: {postId})");

            // =========================================================
            // 3. LẤY DANH SÁCH ỨNG VIÊN TỪ SQL (Bỏ chọn ThongTinChiTiet)
            // =========================================================
            var candidates = await _context.TinDangs
                .AsNoTracking()
                .Where(p => p.MaDanhMuc == currentPost.MaDanhMuc
                            && p.TrangThai == TrangThaiTinDang.DaDuyet
                            && p.Gia > 0
                            && p.TinhTrang == currentPost.TinhTrang)
                // ❌ QUAN TRỌNG: Không select p.ThongTinChiTiet nữa vì cột đã xóa
                .Select(p => new { p.MaTinDang, p.Gia, p.TieuDe, p.TinhTrang })
                .ToListAsync();

            Console.WriteLine($"   => Tìm thấy {candidates.Count} tin sơ bộ.");

            var validPrices = new List<decimal>();

            // Kiểm tra xem tin gốc có dữ liệu chuẩn không?
            bool hasStrictData = !string.IsNullOrEmpty(currentSpecs.Hang)
                              && !string.IsNullOrEmpty(currentSpecs.DongMay)
                              && currentSpecs.DongMay != "Khác"
                              && !string.IsNullOrEmpty(currentSpecs.DungLuong);

            // =========================================================
            // 4. SO SÁNH (MATCHING) - KẾT HỢP GỌI MONGO CHO TỪNG ỨNG VIÊN
            // =========================================================
            foreach (var post in candidates)
            {
                try
                {
                    // 🔥 Lấy chi tiết của ứng viên từ MongoDB
                    var candidateDetail = await _mongoService.GetByMaTinDangAsync(post.MaTinDang);

                    ProductSpecDTO targetSpecs = new ProductSpecDTO();
                    if (candidateDetail != null && candidateDetail.ChiTiet != null)
                    {
                        var json = candidateDetail.ChiTiet.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson });
                        targetSpecs = JsonConvert.DeserializeObject<ProductSpecDTO>(json) ?? new ProductSpecDTO();
                    }

                    bool isMatch = false;

                    // 🛡️ LỚP 2: SO SÁNH CHÍNH XÁC (STRICT MATCHING)
                    if (hasStrictData)
                    {
                        if (!IsStringMatch(currentSpecs.Hang, targetSpecs.Hang)) continue;
                        if (!IsStringMatch(currentSpecs.DongMay, targetSpecs.DongMay)) continue;
                        if (!IsStringMatch(currentSpecs.DungLuong, targetSpecs.DungLuong)) continue;
                        isMatch = true;
                    }
                    // 🛡️ LỚP 3: SO SÁNH TIÊU ĐỀ (FALLBACK)
                    else
                    {
                        // Vẫn bắt buộc cùng Hãng (nếu có thông tin bên Mongo)
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
                catch { }
            }

            // =========================================================
            // 5. TÍNH TOÁN THỐNG KÊ (GIỮ NGUYÊN LOGIC CŨ)
            // =========================================================
            if (validPrices.Count < 1)
            {
                Console.WriteLine("❌ KẾT QUẢ: Không tìm thấy dữ liệu thị trường.");
                return new MarketAnalysisResult { IsSuccess = false };
            }

            validPrices.Sort();
            int n = validPrices.Count;
            List<decimal> marketPrices;

            // IQR Filter
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

            decimal marketMin = marketPrices.Min();
            decimal marketMax = marketPrices.Max();
            decimal marketAvg = marketPrices.Average();

            if (marketMin == marketMax)
            {
                marketMin = marketMin * 0.9m;
                marketMax = marketMax * 1.1m;
            }

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
                AveragePrice = marketAvg,
                CurrentPrice = currentPost.Gia,
                Status = status,
                DifferencePercent = Math.Round(diffPercent, 1),
                SampleSize = marketPrices.Count
            };
        }

        // =================================================================================
        // CÁC HÀM BỔ TRỢ (HELPER FUNCTIONS) - GIỮ NGUYÊN 100%
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