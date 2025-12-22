using MongoDB.Driver;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UniMarket.DataAccess;
using UniMarket.Models.Mongo;
using UniMarket.Models;
using UniMarket.Services.Interfaces;
using UniMarket.Services.Recommendation;

namespace UniMarket.Services.Implementations
{
    public class SearchService : ISearchService
    {
        private readonly MongoDbContext _mongoContext;
        private readonly ApplicationDbContext _sqlContext;
        private readonly IMemoryCache _cache;
        private readonly UserBehaviorService _userBehaviorService;

        public SearchService(
            MongoDbContext mongoContext,
            ApplicationDbContext sqlContext,
            IMemoryCache cache,
            UserBehaviorService userBehaviorService)
        {
            _mongoContext = mongoContext;
            _sqlContext = sqlContext;
            _cache = cache;
            _userBehaviorService = userBehaviorService;
        }

        // ==========================================
        // 1. GHI LOG
        // ==========================================
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
                        ResultCount = resultCount
                    };
                    await _mongoContext.SearchLogs.InsertOneAsync(log);
                }
                catch { }
            });

            await Task.CompletedTask;
        }

        // ==========================================
        // 2. LẤY TRENDING (FIXED ERROR)
        // ==========================================
        public async Task<List<string>> GetTrendingKeywordsAsync(string? userId)
        {
            string cacheKey = string.IsNullOrEmpty(userId) ? "Trend_Global" : $"Trend_User_{userId}";
            if (_cache.TryGetValue(cacheKey, out List<string> cachedTrends))
                return cachedTrends;

            var finalResult = new List<string>();
            var filterTime = DateTime.UtcNow.AddHours(-24);
            int targetCount = Random.Shared.Next(10, 16); // Random 10-15

            // ---------------------------------------------------------
            // A. LẤY GLOBAL TREND (FIXED: Return List<string> directly)
            // ---------------------------------------------------------
            // Sửa lỗi: Chỉ định rõ Task<List<string>> để tránh lỗi compiler
            var globalTrendsTask = Task.Run<List<string>>(async () =>
            {
                try
                {
                    // Query Mongo lấy data ẩn danh trước
                    var rawData = await _mongoContext.SearchLogs
                        .Aggregate()
                        .Match(x => x.CreatedAt >= filterTime && !string.IsNullOrEmpty(x.Keyword))
                        .Group(x => new { x.NormalizedKeyword, UserId = x.UserId ?? "guest" },
                               g => new { Key = g.Key, OriginalKeyword = g.First().Keyword })
                        .Group(x => x.Key.NormalizedKeyword,
                               g => new { Keyword = g.First().OriginalKeyword, UniqueUserCount = g.Count() })
                        .SortByDescending(x => x.UniqueUserCount)
                        .Limit(30)
                        .ToListAsync();

                    // Convert sang List<string> ngay tại đây để đồng bộ kiểu trả về
                    return rawData.Select(x => x.Keyword).ToList();
                }
                catch
                {
                    return new List<string>(); // Catch cũng trả về List<string> -> Hết lỗi
                }
            });

            // ---------------------------------------------------------
            // B. LẤY PERSONAL TREND
            // ---------------------------------------------------------
            var personalTrendsTask = Task.Run<List<string>>(async () =>
            {
                if (string.IsNullOrEmpty(userId)) return new List<string>();
                try
                {
                    var userProfile = await _userBehaviorService.AnalyzeUserProfileAsync(userId);
                    if (userProfile != null && userProfile.PreferredCategoryIds.Any())
                    {
                        return await _sqlContext.TinDangs
                            .AsNoTracking()
                            .Where(t => t.TrangThai == TrangThaiTinDang.DaDuyet)
                            .Where(t => userProfile.PreferredCategoryIds.Contains(t.MaDanhMuc))
                            .OrderByDescending(t => t.SoLuotXem)
                            .Select(t => t.TieuDe)
                            .Take(10)
                            .ToListAsync();
                    }
                }
                catch { }
                return new List<string>();
            });

            await Task.WhenAll(globalTrendsTask, personalTrendsTask);

            var globalKeywords = await globalTrendsTask;
            var personalData = await personalTrendsTask;

            // ---------------------------------------------------------
            // C. THUẬT TOÁN TRỘN (MIXING)
            // ---------------------------------------------------------
            var usedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (personalData.Any())
            {
                int pIndex = 0, gIndex = 0;
                while (finalResult.Count < targetCount && (pIndex < personalData.Count || gIndex < globalKeywords.Count))
                {
                    if (pIndex < personalData.Count)
                    {
                        string kw = personalData[pIndex++];
                        if (usedKeywords.Add(kw)) finalResult.Add(kw);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        if (gIndex < globalKeywords.Count && finalResult.Count < targetCount)
                        {
                            string kw = globalKeywords[gIndex++];
                            if (usedKeywords.Add(kw)) finalResult.Add(kw);
                        }
                    }
                }
            }
            else
            {
                foreach (var kw in globalKeywords)
                {
                    if (finalResult.Count >= targetCount) break;
                    if (usedKeywords.Add(kw)) finalResult.Add(kw);
                }
            }

            // ---------------------------------------------------------
            // D. FILL UP (LẤP ĐẦY)
            // ---------------------------------------------------------
            if (finalResult.Count < targetCount)
            {
                try
                {
                    var fallback = await _sqlContext.TinDangs
                       .AsNoTracking()
                       .Where(t => t.TrangThai == TrangThaiTinDang.DaDuyet)
                       .OrderByDescending(t => t.SoLuotXem)
                       .Select(t => t.TieuDe)
                       .Take(20)
                       .ToListAsync();

                    foreach (var item in fallback)
                    {
                        if (finalResult.Count >= targetCount) break;
                        if (usedKeywords.Add(item)) finalResult.Add(item);
                    }
                }
                catch { }
            }

            var cacheTime = string.IsNullOrEmpty(userId) ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(2);
            _cache.Set(cacheKey, finalResult, cacheTime);

            return finalResult;
        }

        // ==========================================
        // 3. GỢI Ý THÔNG MINH
        // ==========================================
        public async Task<List<string>> GetSmartSuggestionsAsync(string keyword, string? userId)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<string>();

            keyword = keyword.Trim().ToLower();
            string cacheKey = $"suggest_{userId}_{keyword}";
            if (_cache.TryGetValue(cacheKey, out List<string> cached)) return cached;

            var suggestions = new List<string>();

            // A. Cá nhân
            if (!string.IsNullOrEmpty(userId))
            {
                try
                {
                    var personalHistory = await _mongoContext.SearchLogs
                        .Find(x => x.UserId == userId && x.NormalizedKeyword.Contains(keyword))
                        .SortByDescending(x => x.CreatedAt)
                        .Limit(3)
                        .Project(x => x.Keyword)
                        .ToListAsync();
                    suggestions.AddRange(personalHistory);
                }
                catch { }
            }

            // B. Cộng đồng
            try
            {
                var globalSuggestions = await _mongoContext.SearchLogs
                    .Aggregate()
                    .Match(x => x.NormalizedKeyword.Contains(keyword))
                    .Group(x => x.NormalizedKeyword, g => new { Key = g.Key, Original = g.First().Keyword, Count = g.Count() })
                    .SortByDescending(x => x.Count)
                    .Limit(5)
                    .ToListAsync();

                var globalKeywords = globalSuggestions.Select(x => x.Original).ToList();
                suggestions.AddRange(globalKeywords.Where(k => !suggestions.Contains(k)));
            }
            catch { }

            // C. Sản phẩm SQL
            if (suggestions.Count < 10)
            {
                try
                {
                    var productNames = await _sqlContext.TinDangs
                        .AsNoTracking()
                        .Where(t => t.TrangThai == TrangThaiTinDang.DaDuyet && t.TieuDe.Contains(keyword))
                        .OrderByDescending(t => t.SoLuotXem)
                        .Select(t => t.TieuDe)
                        .Take(5)
                        .ToListAsync();

                    suggestions.AddRange(productNames.Where(k => !suggestions.Contains(k)));
                }
                catch { }
            }

            var finalResult = suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            _cache.Set(cacheKey, finalResult, TimeSpan.FromSeconds(30));
            return finalResult;
        }

        // ==========================================
        // 4. RELATED SEARCH
        // ==========================================
        public async Task<List<string>> GetRelatedKeywordsAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<string>();
            string cacheKey = $"related_{keyword.Trim().ToLower()}";
            if (_cache.TryGetValue(cacheKey, out List<string> cached)) return cached;

            try
            {
                var userIds = await _mongoContext.SearchLogs
                    .Find(x => x.NormalizedKeyword == keyword.ToLower() && x.UserId != null)
                    .SortByDescending(x => x.CreatedAt)
                    .Limit(50)
                    .Project(x => x.UserId)
                    .ToListAsync();

                if (!userIds.Any()) return new List<string>();

                var related = await _mongoContext.SearchLogs
                    .Aggregate()
                    .Match(x => userIds.Contains(x.UserId) && x.NormalizedKeyword != keyword.ToLower())
                    .Group(x => x.Keyword, g => new { Keyword = g.Key, Count = g.Count() })
                    .SortByDescending(x => x.Count)
                    .Limit(8)
                    .ToListAsync();

                var result = related.Select(x => x.Keyword).ToList();
                _cache.Set(cacheKey, result, TimeSpan.FromHours(1));
                return result;
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}