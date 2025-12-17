using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Models.DTOs;

namespace UniMarket.Services.Recommendation
{
    public class UserRecommendationService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserBehaviorService _behaviorService;
        private readonly IMemoryCache _cache; // ✅ Thêm Cache

        public UserRecommendationService(
            ApplicationDbContext context,
            UserBehaviorService behaviorService,
            IMemoryCache cache)
        {
            _context = context;
            _behaviorService = behaviorService;
            _cache = cache;
        }

        public async Task<List<UserRecommendationDto>> GetSuggestedUsersAsync(string currentUserId, int take = 20)
        {
            // 1. Lấy chân dung user (Sở thích)
            var userProfile = await _behaviorService.AnalyzeUserProfileAsync(currentUserId);
            var preferredCategories = userProfile.PreferredCategoryIds;

            // ====================================================================================
            // BƯỚC 2: LẤY BLACKLIST (CACHE) - Tối ưu hiệu năng từ Code 1
            // ====================================================================================
            var cacheKey = $"FollowedIds_{currentUserId}";
            if (!_cache.TryGetValue(cacheKey, out List<string> followingIds))
            {
                followingIds = await _context.Follows
                    .AsNoTracking()
                    .Where(f => f.FollowerId == currentUserId)
                    .Select(f => f.FollowingId)
                    .ToListAsync();

                // Thêm chính mình vào blacklist
                followingIds.Add(currentUserId);

                // Cache trong 5 phút
                _cache.Set(cacheKey, followingIds, TimeSpan.FromMinutes(5));
            }

            // ====================================================================================
            // BƯỚC 3: TÌM ỨNG VIÊN (CANDIDATES) - Logic Code 2
            // ====================================================================================

            // --- LOGIC A: Social Graph (Bạn chung) ---
            var friendCandidates = new List<(string Id, int Count)>();
            if (followingIds.Count < 1000) // Chỉ chạy nếu follow list không quá khủng
            {
                var rawFriends = await _context.Follows
                    .AsNoTracking()
                    .Where(f => followingIds.Contains(f.FollowerId) && !followingIds.Contains(f.FollowingId))
                    .GroupBy(f => f.FollowingId)
                    .Select(g => new { UserId = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(50)
                    .ToListAsync();

                friendCandidates = rawFriends.Select(x => (Id: x.UserId, Count: x.Count)).ToList();
            }

            // --- LOGIC B: Content-Based (Người bán cùng gu) ---
            var nicheSellerCandidates = new List<string>();
            if (preferredCategories.Any())
            {
                nicheSellerCandidates = await _context.TinDangs
                    .AsNoTracking()
                    .Where(t => preferredCategories.Contains(t.MaDanhMuc) && !followingIds.Contains(t.MaNguoiBan))
                    .GroupBy(t => t.MaNguoiBan)
                    .OrderByDescending(g => g.Count()) // Ưu tiên người đăng nhiều
                    .Select(g => g.Key)
                    .Take(50)
                    .ToListAsync();
            }

            // --- LOGIC C: Interaction-Based (Like video - Logic TikTok) ---
            var engagedCandidates = new List<(string Id, int Count)>();
            var rawEngaged = await _context.VideoLikes
                .AsNoTracking()
                .Where(l => l.UserId == currentUserId)
                .Select(l => l.TinDang.MaNguoiBan)
                .Where(authorId => !followingIds.Contains(authorId))
                .GroupBy(id => id)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(50)
                .ToListAsync();

            engagedCandidates = rawEngaged.Select(x => (Id: x.UserId, Count: x.Count)).ToList();

            // ====================================================================================
            // BƯỚC 4: FALLBACK - TRENDING USERS (Từ Code 1)
            // Nếu tổng số ứng viên quá ít (User mới), lấp đầy bằng người nổi tiếng
            // ====================================================================================
            var trendingCandidates = new List<string>();
            int currentCount = friendCandidates.Count + nicheSellerCandidates.Count + engagedCandidates.Count;

            if (currentCount < take)
            {
                // Lấy danh sách Hot User từ Cache (Cache toàn cục 1 giờ)
                var popularUsers = await _cache.GetOrCreateAsync("GlobalTrendingUsers", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    // Giả định người nổi tiếng là người có nhiều follower nhất
                    return await _context.Follows
                        .GroupBy(f => f.FollowingId)
                        .OrderByDescending(g => g.Count())
                        .Take(50)
                        .Select(g => g.Key)
                        .ToListAsync();
                });

                // Lọc những người chưa follow
                trendingCandidates = popularUsers
                    .Where(id => !followingIds.Contains(id))
                    .Take(take - currentCount + 5) // Lấy dư ra một chút
                    .ToList();
            }

            // ====================================================================================
            // BƯỚC 5: TỔNG HỢP & QUERY DB
            // ====================================================================================
            var allCandidateIds = friendCandidates.Select(x => x.Id)
                .Concat(nicheSellerCandidates)
                .Concat(engagedCandidates.Select(x => x.Id))
                .Concat(trendingCandidates) // Gộp thêm Trending
                .Distinct()
                .ToList();

            // Query thông tin User + EmailConfirmed (để map ra Tích xanh)
            var usersInfo = await _context.Users
                .AsNoTracking()
                .Where(u => allCandidateIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.UserName,
                    u.AvatarUrl,
                    u.EmailConfirmed // Dùng trường này làm cờ Tích Xanh
                })
                .ToListAsync();

            var finalResult = new List<UserRecommendationDto>();
            var random = new Random();

            foreach (var user in usersInfo)
            {
                double score = 0;
                string reason = "Gợi ý cho bạn";

                // --- 1. Check Logic C (Interaction - Mạnh nhất) ---
                var engagedMatch = engagedCandidates.FirstOrDefault(x => x.Id == user.Id);
                if (engagedMatch != default)
                {
                    score += engagedMatch.Count * 5;
                    reason = score > 10 ? "Bạn thường tương tác" : "Dựa trên video bạn thích";
                }

                // --- 2. Check Logic A (Social) ---
                var friendMatch = friendCandidates.FirstOrDefault(x => x.Id == user.Id);
                if (friendMatch != default)
                {
                    score += friendMatch.Count * 3;
                    if (score <= 5) reason = $"{friendMatch.Count} bạn chung"; // Chỉ ghi đè nếu chưa có lý do xịn hơn
                }

                // --- 3. Check Logic B (Niche) ---
                if (nicheSellerCandidates.Contains(user.Id))
                {
                    score += 5;
                    if (score <= 5) reason = "Phù hợp sở thích của bạn";
                }

                // --- 4. Check Logic D (Trending) ---
                if (trendingCandidates.Contains(user.Id) && score == 0)
                {
                    score += 2; // Điểm thấp để xếp dưới cùng
                    reason = "Gợi ý phổ biến";
                }

                // Shuffle nhẹ
                score += random.NextDouble() * 2;

                finalResult.Add(new UserRecommendationDto
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    UserName = user.UserName,
                    AvatarUrl = user.AvatarUrl,
                    IsTickBlue = user.EmailConfirmed, // ✅ Map Logic Code 1
                    Reason = reason,
                    Score = score
                });
            }

            return finalResult
                .OrderByDescending(x => x.Score)
                .Take(take)
                .ToList();
        }
    }
}