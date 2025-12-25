using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UniMarket.DataAccess;
using UniMarket.Models; // Chứa enum ShareType, ReportTargetType...
using UniMarket.Models.ML;
using UniMarket.Models.Mongo;

namespace UniMarket.Services.Recommendation
{
    // ============================================================
    // DTO: CHÂN DUNG KHÁCH HÀNG (User Profiling)
    // ============================================================
    public class UserProfileDto
    {
        public List<string> RecentSearchKeywords { get; set; } = new();
        public decimal PreferredMinPrice { get; set; } = 0;
        public decimal PreferredMaxPrice { get; set; } = 0;
        public int? PreferredLocationId { get; set; }
        public List<int> PreferredCategoryIds { get; set; } = new();

        public bool HasData => RecentSearchKeywords.Any() || PreferredMaxPrice > 0 || PreferredCategoryIds.Any();
    }

    public class UserBehaviorService
    {
        private readonly ApplicationDbContext _context;
        // Khai báo 2 collection MongoDB riêng biệt
        private readonly IMongoCollection<ViewHistory> _viewHistoryCollection;
        private readonly IMongoCollection<VideoViewLog> _videoLogCollection; // <--- MỚI THÊM

        // ============================================================
        // 1. CẤU HÌNH ĐIỂM SỐ (WEIGHTS & SCORING RULES)
        // ============================================================

        // --- A. Hành vi Đọc Tin Đăng (MongoDB - ViewHistory) ---
        private const float SCORE_POST_CLICK_ONLY = 1.0f;
        private const float SCORE_POST_GLANCE = 0.5f;
        private const float SCORE_POST_READING = 2.0f;
        private const float SCORE_POST_DEEP_READ = 5.0f;
        private const float SCORE_POST_REVISIT_BONUS = 2.0f;

        // --- B. Hành vi Xem Video Feed (MongoDB - VideoViewLog) ---
        private const float SCORE_VIDEO_SKIP = -2.0f;        // < 3s
        private const float SCORE_VIDEO_SHORT = 0.5f;        // 3s - 10s
        private const float SCORE_VIDEO_MEDIUM = 2.0f;       // > 10s
        private const float SCORE_VIDEO_COMPLETED = 4.0f;    // Xem hết
        private const float SCORE_VIDEO_REWATCH = 3.0f;      // Xem lại

        // --- C. Tương tác Tích cực ---
        private const float SCORE_LIKE = 5.0f;
        private const float SCORE_COMMENT = 8.0f;
        private const float SCORE_SAVE_VIDEO = 10.0f;
        private const float SCORE_FAVORITE_ITEM = 15.0f;

        // --- D. Hành vi Lan tỏa ---
        private const float SCORE_SHARE_INTERNAL = 10.0f;
        private const float SCORE_SHARE_SOCIAL = 15.0f;

        // --- E. Hành vi Tiêu cực ---
        private const float SCORE_REPORT = -50.0f;

        public UserBehaviorService(ApplicationDbContext context, IMongoDatabase mongoDatabase)
        {
            _context = context;
            // Map với các collection trong MongoDB
            _viewHistoryCollection = mongoDatabase.GetCollection<ViewHistory>("ViewHistory");
            _videoLogCollection = mongoDatabase.GetCollection<VideoViewLog>("VideoViewLog"); // <--- KHỞI TẠO COLLECTION MỚI
        }

        // ============================================================
        // 2. PHÂN TÍCH CHÂN DUNG NGƯỜI DÙNG (USER PROFILING)
        // ============================================================
        public async Task<UserProfileDto> AnalyzeUserProfileAsync(string userId)
        {
            var profile = new UserProfileDto();

            // --- BƯỚC 1: Học từ Lịch Sử Tìm Kiếm (SQL) ---
            profile.RecentSearchKeywords = await _context.SearchHistories
                .AsNoTracking()
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.CreatedAt)
                .Take(10)
                .Select(h => h.Keyword.ToLower())
                .ToListAsync();

            // --- BƯỚC 2: Tổng hợp ID Tin Đăng quan tâm ---
            var interestingIds = new List<int>();

            // a. Từ SQL (Like, Favorite, Save)
            var likedIds = await _context.VideoLikes.AsNoTracking().Where(l => l.UserId == userId).Select(l => l.MaTinDang).ToListAsync();
            var favIds = await _context.TinDangYeuThichs.AsNoTracking().Where(f => f.MaNguoiDung == userId).Select(f => f.MaTinDang).ToListAsync();
            var savedIds = await _context.VideoTinDangSaves.AsNoTracking().Where(s => s.MaNguoiDung == userId).Select(s => s.MaTinDang).ToListAsync();

            interestingIds.AddRange(likedIds);
            interestingIds.AddRange(favIds);
            interestingIds.AddRange(savedIds);

            // b. Từ MongoDB (ViewHistory - Đọc tin)
            var rawMongoViews = await _viewHistoryCollection
                .Find(x => x.UserId == userId)
                .SortByDescending(x => x.LastViewedAt)
                .Limit(100)
                .ToListAsync();

            var validMongoIds = rawMongoViews
                .GroupBy(x => x.MaTinDang)
                .Select(g => new
                {
                    MaTinDang = g.Key,
                    TotalSeconds = g.Sum(x => x.WatchedSeconds),
                    VisitCount = g.Count()
                })
                .Where(x => x.TotalSeconds > 10 || x.VisitCount >= 2)
                .Select(x => x.MaTinDang)
                .ToList();

            interestingIds.AddRange(validMongoIds);

            // c. Từ MongoDB (VideoViewLog - Xem Video) -> THÊM PHẦN NÀY ĐỂ HỌC TỪ VIDEO LOG MỚI
            var rawVideoLogs = await _videoLogCollection
                .Find(x => x.UserId == userId)
                .SortByDescending(x => x.StartedAt)
                .Limit(50)
                .ToListAsync();

            var validVideoIds = rawVideoLogs
                .Where(x => x.IsCompleted || x.WatchedSeconds > 10) // Chỉ lấy video xem lâu
                .Select(x => x.MaTinDang)
                .ToList();

            interestingIds.AddRange(validVideoIds);


            // Loại bỏ trùng lặp
            interestingIds = interestingIds.Distinct().ToList();

            // --- BƯỚC 3: Tính toán Gu (Affinity) ---
            if (interestingIds.Any())
            {
                var interactiveItems = await _context.TinDangs
                    .AsNoTracking()
                    .Where(t => interestingIds.Contains(t.MaTinDang))
                    .Select(t => new { t.Gia, t.MaTinhThanh, t.MaDanhMuc })
                    .ToListAsync();

                if (interactiveItems.Any())
                {
                    var avgPrice = interactiveItems.Average(x => x.Gia);
                    profile.PreferredMinPrice = avgPrice * 0.7m;
                    profile.PreferredMaxPrice = avgPrice * 1.3m;

                    profile.PreferredLocationId = interactiveItems
                        .GroupBy(x => x.MaTinhThanh)
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key)
                        .FirstOrDefault();

                    profile.PreferredCategoryIds = interactiveItems
                        .GroupBy(x => x.MaDanhMuc)
                        .OrderByDescending(g => g.Count())
                        .Take(5)
                        .Select(g => g.Key)
                        .ToList();
                }
            }

            return profile;
        }

        // ============================================================
        // 3. LẤY DỮ LIỆU HUẤN LUYỆN (TRAINING DATA GENERATION)
        // ============================================================
        public async Task<List<VideoRating>> GetTrainingDataAsync()
        {
            var tempScores = new Dictionary<(string userId, int videoId), float>();

            // Chỉ học dữ liệu trong 90 ngày gần nhất
            var cutOffDate = DateTime.UtcNow.AddDays(-90);

            // ---------------------------------------------------------
            // A. XỬ LÝ MONGODB (ViewHistory - Đọc bài viết)
            // ---------------------------------------------------------
            var viewHistoryFilter = Builders<ViewHistory>.Filter.Gte(x => x.LastViewedAt, cutOffDate);
            var rawViewHistory = await _viewHistoryCollection.Find(viewHistoryFilter).ToListAsync();

            var groupedViewHistory = rawViewHistory
                .GroupBy(x => new { x.UserId, x.MaTinDang })
                .Select(g => new
                {
                    UserId = g.Key.UserId,
                    MaTinDang = g.Key.MaTinDang,
                    TotalSeconds = g.Sum(x => x.WatchedSeconds),
                    MaxRewatch = g.Max(x => x.RewatchCount),
                    VisitCount = g.Count(),
                    LastViewed = g.Max(x => x.LastViewedAt),
                    IsCompleted = g.Any(x => x.IsCompleted)
                });

            foreach (var view in groupedViewHistory)
            {
                float score = 0;
                if (view.TotalSeconds == 0) score = SCORE_POST_CLICK_ONLY;
                else if (view.TotalSeconds < 5) score = SCORE_POST_GLANCE;
                else if (view.TotalSeconds < 30) score = SCORE_POST_READING;
                else score = SCORE_POST_DEEP_READ;

                if (view.VisitCount > 1) score += Math.Min((view.VisitCount - 1) * SCORE_POST_REVISIT_BONUS, 6.0f);
                if (view.MaxRewatch > 0) score += SCORE_POST_REVISIT_BONUS;
                if (view.IsCompleted) score += 1.0f;

                AddScoreWithDecay(tempScores, view.UserId, view.MaTinDang, score, view.LastViewed);
            }

            // ---------------------------------------------------------
            // B. XỬ LÝ MONGODB (VideoViewLog - Xem Video Feed) -> ĐÃ SỬA
            // ---------------------------------------------------------
            // Logic cũ dùng SQL (_context.VideoViews) gây lỗi, nay đổi sang Mongo

            var videoLogFilter = Builders<VideoViewLog>.Filter.Gte(x => x.StartedAt, cutOffDate);
            var rawVideoLogs = await _videoLogCollection.Find(videoLogFilter).ToListAsync();

            // Không cần GroupBy phức tạp vì log video thường lưu theo session xem
            foreach (var v in rawVideoLogs)
            {
                // Bỏ qua nếu không có UserId (khách vãng lai)
                if (string.IsNullOrEmpty(v.UserId)) continue;

                float score = 0;

                // Logic tính điểm Video Feed
                if (v.WatchedSeconds < 3)
                {
                    score = SCORE_VIDEO_SKIP; // Lướt qua
                }
                else if (!v.IsCompleted)
                {
                    score = (v.WatchedSeconds >= 10) ? SCORE_VIDEO_MEDIUM : SCORE_VIDEO_SHORT;
                }
                else
                {
                    score = SCORE_VIDEO_COMPLETED;
                }

                // Điểm thưởng xem lại
                if (v.RewatchCount > 0)
                {
                    score += Math.Min(v.RewatchCount, 3) * SCORE_VIDEO_REWATCH;
                }

                AddScoreWithDecay(tempScores, v.UserId, v.MaTinDang, score, v.StartedAt);
            }

            // ---------------------------------------------------------
            // C. XỬ LÝ TƯƠNG TÁC RÕ RÀNG (Explicit Feedback - Vẫn dùng SQL)
            // ---------------------------------------------------------

            // 1. Likes
            var likes = await _context.VideoLikes.AsNoTracking().Where(x => x.CreatedAt >= cutOffDate)
                .Select(x => new { x.UserId, x.MaTinDang, x.CreatedAt }).ToListAsync();
            foreach (var l in likes) AddScoreWithDecay(tempScores, l.UserId, l.MaTinDang, SCORE_LIKE, l.CreatedAt);

            // 2. Comments
            var comments = await _context.VideoComments.AsNoTracking().Where(x => x.CreatedAt >= cutOffDate)
                .Select(x => new { x.UserId, x.MaTinDang, x.CreatedAt }).ToListAsync();
            foreach (var c in comments) AddScoreWithDecay(tempScores, c.UserId, c.MaTinDang, SCORE_COMMENT, c.CreatedAt);

            // 3. Saves
            var saves = await _context.VideoTinDangSaves.AsNoTracking().Where(x => x.NgayLuu >= cutOffDate)
                .Select(x => new { UserId = x.MaNguoiDung, x.MaTinDang, CreatedAt = x.NgayLuu }).ToListAsync();
            foreach (var s in saves) AddScoreWithDecay(tempScores, s.UserId, s.MaTinDang, SCORE_SAVE_VIDEO, s.CreatedAt);

            // 4. Favorites
            var favs = await _context.TinDangYeuThichs.AsNoTracking().Where(x => x.NgayTao >= cutOffDate)
                .Select(x => new { UserId = x.MaNguoiDung, x.MaTinDang, CreatedAt = x.NgayTao }).ToListAsync();
            foreach (var f in favs) AddScoreWithDecay(tempScores, f.UserId, f.MaTinDang, SCORE_FAVORITE_ITEM, f.CreatedAt);

            // 5. Shares
            var shares = await _context.Shares.AsNoTracking().Where(s => s.TinDangId.HasValue && s.SharedAt >= cutOffDate)
                .Select(s => new { s.UserId, TinDangId = s.TinDangId.Value, s.ShareType, s.SharedAt }).ToListAsync();
            foreach (var s in shares)
            {
                float shareScore = (s.ShareType == ShareType.SocialMedia) ? SCORE_SHARE_SOCIAL : SCORE_SHARE_INTERNAL;
                AddScoreWithDecay(tempScores, s.UserId, s.TinDangId, shareScore, s.SharedAt);
            }

            // 6. Reports
            var reports = await _context.Reports.AsNoTracking()
                .Where(r => r.CreatedAt >= cutOffDate && r.TargetType == ReportTargetType.Post)
                .Select(r => new { r.ReporterId, r.TargetId, r.CreatedAt }).ToListAsync();
            foreach (var r in reports) AddScoreWithDecay(tempScores, r.ReporterId, r.TargetId, SCORE_REPORT, r.CreatedAt.DateTime);

            // ---------------------------------------------------------
            // D. OUTPUT
            // ---------------------------------------------------------
            var trainingData = new List<VideoRating>();
            foreach (var item in tempScores)
            {
                if (Math.Abs(item.Value) > 0.2f)
                {
                    trainingData.Add(new VideoRating
                    {
                        UserId = item.Key.userId,
                        VideoId = (float)item.Key.videoId,
                        Label = item.Value
                    });
                }
            }

            return trainingData;
        }

        // ============================================================
        // 4. HELPER: TIME DECAY
        // ============================================================
        private void AddScoreWithDecay(
            Dictionary<(string, int), float> map,
            string userId,
            int videoId,
            float baseScore,
            DateTime actionDate)
        {
            if (string.IsNullOrEmpty(userId)) return;

            double daysOld = (DateTime.UtcNow - actionDate).TotalDays;
            if (daysOld < 0) daysOld = 0;

            double decayFactor = 1.0 / (1.0 + (daysOld / 30.0));
            float finalScore = baseScore * (float)decayFactor;

            var key = (userId, videoId);
            if (map.TryGetValue(key, out float currentScore))
            {
                map[key] = currentScore + finalScore;
            }
            else
            {
                map[key] = finalScore;
            }
        }
    }
}