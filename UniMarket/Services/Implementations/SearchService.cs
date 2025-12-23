using MongoDB.Driver;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Models.Mongo;
using UniMarket.Services.Interfaces;
using UniMarket.Services.Recommendation;

namespace UniMarket.Services.Implementations
{
    public class SearchService : ISearchService
    {
        private readonly MongoDbContext _mongoContext;
        private readonly ApplicationDbContext _sqlContext;
        private readonly IMemoryCache _cache;

        // Cấu hình Cache và Giới hạn
        private readonly TimeSpan _shortCache = TimeSpan.FromMinutes(2);  // Cache gợi ý (gõ phím)
        private readonly TimeSpan _longCache = TimeSpan.FromMinutes(30);  // Cache trend (ít thay đổi)
        private const int MAX_SUGGESTIONS = 10;

        public SearchService(
            MongoDbContext mongoContext,
            ApplicationDbContext sqlContext,
            IMemoryCache cache)
        {
            _mongoContext = mongoContext;
            _sqlContext = sqlContext;
            _cache = cache;
        }

        // ==========================================================
        // 1. SMART SUGGESTION (Gợi ý thông minh & Tốc độ cao - Autocomplete)
        // ==========================================================
        public async Task<List<string>> GetSmartSuggestionsAsync(string keyword, string? userId)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<string>();

            var normalizedKey = keyword.Trim().ToLower();
            string cacheKey = $"Suggest_v4_{userId}_{normalizedKey}";

            // 1. Kiểm tra Cache RAM (Trả về ngay lập tức - < 1ms)
            if (_cache.TryGetValue(cacheKey, out List<string> cachedResult))
                return cachedResult;

            // 2. CHẠY SONG SONG (Parallel Execution)
            // Task A: Lịch sử tìm kiếm CỦA CHÍNH USER (Ưu tiên cao nhất)
            var personalTask = GetPersonalHistoryAsync(userId, normalizedKey);

            // Task B: Sản phẩm có thật trong DB (Ưu tiên nhì - dùng Index SQL)
            var productTask = GetMatchingProductsAsync(normalizedKey);

            // Task C: Từ khóa hot của cộng đồng (Dùng để lấp đầy nếu thiếu)
            var communityTask = GetCommunityKeywordsAsync(normalizedKey);

            await Task.WhenAll(personalTask, productTask, communityTask);

            // 3. THUẬT TOÁN TRỘN & XẾP HẠNG (RANKING)
            var finalResult = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Layer 1: Lịch sử cá nhân
            foreach (var item in personalTask.Result) finalResult.Add(item);

            // Layer 2: Sản phẩm khớp trực tiếp
            foreach (var item in productTask.Result)
            {
                if (finalResult.Count >= MAX_SUGGESTIONS) break;
                finalResult.Add(item);
            }

            // Layer 3: Cộng đồng
            foreach (var item in communityTask.Result)
            {
                if (finalResult.Count >= MAX_SUGGESTIONS) break;
                finalResult.Add(item);
            }

            var resultList = finalResult.ToList();
            _cache.Set(cacheKey, resultList, _shortCache);

            return resultList;
        }

        // ==========================================================
        // 2. TRENDING SYSTEM (Đề xuất xu hướng khi chưa gõ gì)
        // ==========================================================
        public async Task<List<string>> GetTrendingKeywordsAsync(string? userId)
        {
            // A. Lấy Global Trend (Cache 30 phút)
            // SỬA: Gọi hàm ComputeGlobalTrendsAsync với limit 20 cho trang chủ
            var globalTrend = await _cache.GetOrCreateAsync("Trend_Global_Top20", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _longCache;
                return await ComputeGlobalTrendsAsync(20);
            });

            // Nếu là khách vãng lai -> Trả về Global
            if (string.IsNullOrEmpty(userId)) return globalTrend;

            // B. Lấy Personal Interest (Dựa trên hành vi user)
            var personalInterests = await GetUserInterestsAsync(userId);

            // C. Trộn thông minh (Interleave Mixing)
            var mixedResults = new List<string>();
            int p = 0, g = 0;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (mixedResults.Count < 10 && (p < personalInterests.Count || g < globalTrend.Count))
            {
                // Ưu tiên sở thích
                if (p < personalInterests.Count)
                {
                    var k = personalInterests[p++];
                    if (used.Add(k)) mixedResults.Add(k);
                }
                // Chêm trend vào
                if (g < globalTrend.Count)
                {
                    var k = globalTrend[g++];
                    if (used.Add(k)) mixedResults.Add(k);
                }
            }

            return mixedResults;
        }

        // ==========================================================
        // 3. LOGGING (Fire-and-Forget - Siêu tốc)
        // ==========================================================
        public async Task LogSearchAsync(string keyword, string? userId, string? sessionId, int resultCount)
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length > 100) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var log = new SearchLog
                    {
                        Keyword = keyword.Trim(),
                        NormalizedKeyword = keyword.Trim().ToLower(),
                        UserId = userId,
                        SessionId = sessionId,
                        CreatedAt = DateTime.UtcNow,
                        ResultCount = resultCount,
                        Platform = "Web"
                    };
                    await _mongoContext.SearchLogs.InsertOneAsync(log);
                }
                catch { /* Silent fail */ }
            });

            await Task.CompletedTask;
        }

        // ==========================================================
        // 4. RELATED KEYWORDS (Tìm kiếm liên quan - Logic giống TikTok)
        // ==========================================================

        public async Task<List<string>> GetRelatedKeywordsAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<string>();

            string normalizedKey = keyword.Trim().ToLower();
            string cacheKey = $"Related_Hybrid_{normalizedKey}";

            // Cache 10 phút để đỡ query DB liên tục, vì related keyword không thay đổi quá nhanh
            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                var finalResult = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // ----------------------------------------------------------
                // LAYER 1: BEHAVIORAL (Hành vi người dùng)
                // Ý nghĩa: "Những người tìm A cũng tìm B"
                // ----------------------------------------------------------
                try
                {
                    // 1. Tìm những user đã từng search từ khóa này
                    var userIds = await _mongoContext.SearchLogs
                        .Find(x => x.NormalizedKeyword == normalizedKey && x.UserId != null)
                        .SortByDescending(x => x.CreatedAt)
                        .Limit(50) // Lấy mẫu 50 người gần nhất
                        .Project(x => x.UserId)
                        .ToListAsync();

                    if (userIds.Any())
                    {
                        // 2. Xem họ tìm gì khác
                        var behaviorKeywords = await _mongoContext.SearchLogs
                            .Aggregate()
                            .Match(x => userIds.Contains(x.UserId) && x.NormalizedKeyword != normalizedKey)
                            .Group(x => x.Keyword, g => new { Keyword = g.Key, Count = g.Count() })
                            .SortByDescending(x => x.Count)
                            .Limit(6)
                            .ToListAsync();

                        foreach (var item in behaviorKeywords) finalResult.Add(item.Keyword);
                    }
                }
                catch { /* Bỏ qua lỗi Layer 1 để chạy tiếp */ }

                // ----------------------------------------------------------
                // LAYER 2: SEMANTIC (Ngữ nghĩa/Chứa từ khóa)
                // Ý nghĩa: Tìm "điện thoại" -> Gợi ý "ốp điện thoại", "giá đỡ điện thoại"
                // ----------------------------------------------------------
                if (finalResult.Count < 10)
                {
                    try
                    {
                        var semanticKeywords = await _mongoContext.SearchLogs
                            .Aggregate()
                            .Match(x => x.NormalizedKeyword.Contains(normalizedKey) && x.NormalizedKeyword != normalizedKey)
                            .Group(x => x.Keyword, g => new { Keyword = g.Key, Count = g.Count() })
                            .SortByDescending(x => x.Count)
                            .Limit(10)
                            .ToListAsync();

                        foreach (var item in semanticKeywords)
                        {
                            if (finalResult.Count >= 10) break;
                            finalResult.Add(item.Keyword);
                        }
                    }
                    catch { /* Bỏ qua lỗi Layer 2 */ }
                }

                // ----------------------------------------------------------
                // LAYER 3: RANDOM TRENDING (Lấp đầy chỗ trống)
                // Ý nghĩa: Nếu từ khóa lạ, chưa ai tìm, lấy Trend bù vào cho đỡ trống
                // ----------------------------------------------------------
                if (finalResult.Count < 5)
                {
                    // Tái sử dụng hàm ComputeGlobalTrendsAsync với limit 10
                    var trending = await ComputeGlobalTrendsAsync(10);

                    // Random hóa danh sách trend để mỗi lần F5 thấy hơi khác
                    var random = new Random();
                    var shuffledTrend = trending.OrderBy(x => random.Next()).ToList();

                    foreach (var item in shuffledTrend)
                    {
                        if (finalResult.Count >= 10) break;
                        finalResult.Add(item);
                    }
                }

                return finalResult.ToList();
            });
        }

        // ==========================================================
        // PRIVATE HELPERS
        // ==========================================================

        private async Task<List<string>> GetPersonalHistoryAsync(string? userId, string keyword)
        {
            if (string.IsNullOrEmpty(userId)) return new List<string>();
            try
            {
                return await _mongoContext.SearchLogs
                    .Find(x => x.UserId == userId && x.NormalizedKeyword.Contains(keyword))
                    .SortByDescending(x => x.CreatedAt)
                    .Limit(3)
                    .Project(x => x.Keyword)
                    .ToListAsync();
            }
            catch { return new List<string>(); }
        }

        private async Task<List<string>> GetMatchingProductsAsync(string keyword)
        {
            try
            {
                return await _sqlContext.TinDangs
                    .AsNoTracking()
                    .Where(t => t.TrangThai == TrangThaiTinDang.DaDuyet && t.TieuDe.StartsWith(keyword))
                    .OrderByDescending(t => t.SoLuotXem)
                    .Select(t => t.TieuDe)
                    .Take(5)
                    .ToListAsync();
            }
            catch { return new List<string>(); }
        }

        private async Task<List<string>> GetCommunityKeywordsAsync(string keyword)
        {
            try
            {
                return await _mongoContext.SearchLogs
                    .AsQueryable()
                    .Where(x => x.NormalizedKeyword.Contains(keyword))
                    .GroupBy(x => x.NormalizedKeyword)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.First().Keyword)
                    .Take(5)
                    .ToListAsync();
            }
            catch { return new List<string>(); }
        }

        // SỬA: Thêm tham số limit để tái sử dụng cho cả Trending Page và Related Search fallback
        private async Task<List<string>> ComputeGlobalTrendsAsync(int limit = 20)
        {
            try
            {
                var filterTime = DateTime.UtcNow.AddHours(-48); // Lấy trend 48h (tăng lên để có nhiều dữ liệu hơn)

                var result = await _mongoContext.SearchLogs
                    .Aggregate()
                    .Match(x => x.CreatedAt >= filterTime)
                    .Group(x => x.NormalizedKeyword, g => new { Keyword = g.First().Keyword, Count = g.Count() })
                    .SortByDescending(x => x.Count)
                    .Limit(limit)
                    .ToListAsync();

                return result.Select(x => x.Keyword).ToList();
            }
            catch { return new List<string>(); }
        }

        private async Task<List<string>> GetUserInterestsAsync(string userId)
        {
            string key = $"User_Interest_{userId}";
            if (_cache.TryGetValue(key, out List<string> cached)) return cached;

            try
            {
                var topCategories = await _sqlContext.VideoLikes
                    .AsNoTracking()
                    .Where(l => l.UserId == userId)
                    .GroupBy(l => l.TinDang.MaDanhMuc)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .Take(3)
                    .ToListAsync();

                if (!topCategories.Any()) return new List<string>();

                var suggestions = await _sqlContext.TinDangs
                    .AsNoTracking()
                    .Where(t => topCategories.Contains(t.MaDanhMuc) && t.TrangThai == TrangThaiTinDang.DaDuyet)
                    .OrderByDescending(t => t.SoLuotXem)
                    .Select(t => t.TieuDe)
                    .Take(10)
                    .ToListAsync();

                _cache.Set(key, suggestions, TimeSpan.FromMinutes(5));
                return suggestions;
            }
            catch { return new List<string>(); }
        }
    }
}