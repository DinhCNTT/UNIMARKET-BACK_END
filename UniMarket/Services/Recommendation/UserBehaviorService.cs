using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Models.ML; // Đảm bảo namespace này chứa class VideoRating

namespace UniMarket.Services.Recommendation
{
    // DTO: Chân dung khách hàng (Sở thích hiện tại được phân tích từ hành vi)
    public class UserProfileDto
    {
        public List<string> RecentSearchKeywords { get; set; } = new(); // Từ khóa hay tìm
        public decimal PreferredMinPrice { get; set; } = 0;             // Khoảng giá thấp nhất chấp nhận
        public decimal PreferredMaxPrice { get; set; } = 0;             // Khoảng giá cao nhất chấp nhận
        public int? PreferredLocationId { get; set; }                   // Tỉnh thành quan tâm nhất
        public List<int> PreferredCategoryIds { get; set; } = new();    // Danh mục hay xem
        public bool HasData => RecentSearchKeywords.Any() || PreferredMaxPrice > 0;
    }

    public class UserBehaviorService
    {
        private readonly ApplicationDbContext _context;

        // ============================================================
        // 1. CẤU HÌNH ĐIỂM SỐ (WEIGHTS CONFIGURATION)
        // ============================================================

        // --- Hành vi Xem (Implicit Feedback) ---
        private const float SCORE_VIEW_SKIP = -2.0f;        // Lướt qua < 3s (Không thích)
        private const float SCORE_VIEW_SHORT = 0.5f;        // Xem < 10s (Tò mò chút xíu)
        private const float SCORE_VIEW_MEDIUM = 2.0f;       // Xem > 50% thời lượng (Khá thích)
        private const float SCORE_VIEW_COMPLETED = 4.0f;    // Xem hết video (Rất thích)
        private const float SCORE_REWATCH_BONUS = 3.0f;     // Điểm thưởng mỗi lần xem lại

        // --- Hành vi Tương tác (Explicit Feedback) ---
        private const float SCORE_LIKE = 5.0f;              // Thả tim
        private const float SCORE_COMMENT = 8.0f;           // Bình luận (Nỗ lực cao hơn like)
        private const float SCORE_SAVE = 10.0f;             // Lưu (Ý định mua/xem lại cao)

        // --- Hành vi Lan tỏa (Viral/Social) ---
        private const float SCORE_SHARE_INTERNAL = 10.0f;   // Share qua chat nội bộ
        private const float SCORE_SHARE_SOCIAL = 15.0f;     // Share ra Facebook/Zalo (Viral cao nhất)

        public UserBehaviorService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 2. PHÂN TÍCH CHÂN DUNG NGƯỜI DÙNG (USER PROFILING)
        // Dùng cho Content-Based Filtering & Cold Start
        // ============================================================
        public async Task<UserProfileDto> AnalyzeUserProfileAsync(string userId)
        {
            var profile = new UserProfileDto();

            // A. Học từ Lịch Sử Tìm Kiếm (Lấy 10 từ khóa gần nhất chưa bị xóa)
            // Nếu user xóa lịch sử, query này sẽ không ra kết quả -> AI tự quên.
            profile.RecentSearchKeywords = await _context.SearchHistories
                .AsNoTracking()
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.CreatedAt)
                .Take(10)
                .Select(h => h.Keyword.ToLower())
                .ToListAsync();

            // B. Học từ Tương tác (Like, Save, Comment) - Lấy 50 hành động gần nhất
            // Tại sao chỉ lấy 50? Để AI bắt trend sở thích MỚI NHẤT, bỏ qua sở thích cũ kỹ.
            var interactiveVideos = await _context.TinDangs
                .AsNoTracking()
                .Where(t =>
                    _context.VideoLikes.Any(l => l.UserId == userId && l.MaTinDang == t.MaTinDang) ||
                    _context.VideoTinDangSaves.Any(s => s.MaNguoiDung == userId && s.MaTinDang == t.MaTinDang)
                )
                .OrderByDescending(t => t.NgayDang)
                .Take(50)
                .Select(t => new { t.Gia, t.MaTinhThanh, t.MaDanhMuc })
                .ToListAsync();

            if (interactiveVideos.Any())
            {
                // 1. Học Giá Cả (Price Affinity)
                // Tính trung bình giá user quan tâm, rồi mở rộng biên độ 30%
                var avgPrice = interactiveVideos.Average(x => x.Gia);
                profile.PreferredMinPrice = avgPrice * 0.7m; // -30%
                profile.PreferredMaxPrice = avgPrice * 1.3m; // +30%

                // 2. Học Khu Vực (Location Affinity)
                // Tìm tỉnh thành xuất hiện nhiều nhất (Mode)
                profile.PreferredLocationId = interactiveVideos
                    .GroupBy(x => x.MaTinhThanh)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();

                // 3. Học Danh Mục (Category Affinity)
                profile.PreferredCategoryIds = interactiveVideos
                    .GroupBy(x => x.MaDanhMuc)
                    .OrderByDescending(g => g.Count())
                    .Take(3) // Lấy top 3 danh mục quan tâm nhất
                    .Select(g => g.Key)
                    .ToList();
            }

            return profile;
        }

        // ============================================================
        // 3. LẤY DỮ LIỆU HUẤN LUYỆN (TRAINING DATA GENERATION)
        // Dùng cho Collaborative Filtering (Matrix Factorization)
        // ============================================================
        public async Task<List<VideoRating>> GetTrainingDataAsync()
        {
            // Dictionary để cộng dồn điểm số cho từng cặp (User, Video)
            // Key: (UserId, VideoId) -> Value: Tổng điểm
            var tempScores = new Dictionary<(string userId, int videoId), float>();

            // Chỉ lấy dữ liệu tương tác trong 90 ngày gần nhất (Tránh User drift)
            var cutOffDate = DateTime.UtcNow.AddDays(-90);

            // ---------------------------------------------------------
            // A. XỬ LÝ DỮ LIỆU XEM (VIEWS)
            // ---------------------------------------------------------
            var views = await _context.VideoViews
                .AsNoTracking()
                .Where(v => v.UserId != null && v.StartedAt >= cutOffDate)
                .Select(v => new
                {
                    v.UserId,
                    v.MaTinDang,
                    v.WatchedSeconds,
                    v.IsCompleted,
                    v.RewatchCount,
                    v.StartedAt
                })
                .ToListAsync();

            foreach (var v in views)
            {
                float score = 0;

                // Logic tính điểm xem chi tiết
                if (v.WatchedSeconds < 3)
                {
                    score = SCORE_VIEW_SKIP; // Bị phạt điểm nếu lướt quá nhanh
                }
                else if (!v.IsCompleted)
                {
                    // Chưa xem hết: Nếu xem > 10s thì điểm trung bình, ngược lại điểm thấp
                    score = (v.WatchedSeconds >= 10) ? SCORE_VIEW_MEDIUM : SCORE_VIEW_SHORT;
                }
                else
                {
                    score = SCORE_VIEW_COMPLETED; // Xem hết
                }

                // Cộng điểm xem lại (Cap ở 5 lần để tránh spam)
                if (v.RewatchCount > 0)
                {
                    int validRewatch = Math.Min(v.RewatchCount, 5);
                    score += (validRewatch * SCORE_REWATCH_BONUS);
                }

                AddScoreWithDecay(tempScores, v.UserId!, v.MaTinDang, score, v.StartedAt);
            }

            // ---------------------------------------------------------
            // B. XỬ LÝ LIKE, COMMENT, SAVE, SHARE (Tương tác tích cực)
            // ---------------------------------------------------------

            // Likes
            var likes = await _context.VideoLikes
                .AsNoTracking().Where(x => x.CreatedAt >= cutOffDate)
                .Select(x => new { x.UserId, x.MaTinDang, x.CreatedAt }).ToListAsync();
            foreach (var l in likes)
                AddScoreWithDecay(tempScores, l.UserId, l.MaTinDang, SCORE_LIKE, l.CreatedAt);

            // Comments
            var comments = await _context.VideoComments
                .AsNoTracking().Where(x => x.CreatedAt >= cutOffDate)
                .Select(x => new { x.UserId, x.MaTinDang, x.CreatedAt }).ToListAsync();
            foreach (var c in comments)
                AddScoreWithDecay(tempScores, c.UserId, c.MaTinDang, SCORE_COMMENT, c.CreatedAt);

            // Saves
            var saves = await _context.VideoTinDangSaves
                .AsNoTracking().Where(x => x.NgayLuu >= cutOffDate)
                .Select(x => new { UserId = x.MaNguoiDung, x.MaTinDang, CreatedAt = x.NgayLuu }).ToListAsync();
            foreach (var s in saves)
                AddScoreWithDecay(tempScores, s.UserId, s.MaTinDang, SCORE_SAVE, s.CreatedAt);

            // Shares
            var shares = await _context.Shares
                .AsNoTracking().Where(s => s.TinDangId.HasValue && s.SharedAt >= cutOffDate)
                .Select(s => new { s.UserId, TinDangId = s.TinDangId.Value, s.ShareType, s.SharedAt }).ToListAsync();
            foreach (var s in shares)
            {
                float shareScore = (s.ShareType == ShareType.SocialMedia) ? SCORE_SHARE_SOCIAL : SCORE_SHARE_INTERNAL;
                AddScoreWithDecay(tempScores, s.UserId, s.TinDangId, shareScore, s.SharedAt);
            }

            // ---------------------------------------------------------
            // C. CHUYỂN ĐỔI SANG MODEL ĐẦU VÀO CHO AI
            // ---------------------------------------------------------
            var trainingData = new List<VideoRating>();

            foreach (var item in tempScores)
            {
                // Giữ lại cả điểm âm (ghét) và điểm dương (thích), lọc nhiễu (gần 0)
                if (Math.Abs(item.Value) > 0.1f)
                {
                    trainingData.Add(new VideoRating
                    {
                        UserId = item.Key.userId,
                        VideoId = (float)item.Key.videoId,
                        Label = item.Value // Label này sẽ được Matrix Factorization học
                    });
                }
            }

            return trainingData;
        }

        // ============================================================
        // HELPER: CỘNG ĐIỂM VỚI TIME DECAY
        // ============================================================
        private void AddScoreWithDecay(
            Dictionary<(string, int), float> map,
            string userId,
            int videoId,
            float baseScore,
            DateTime actionDate)
        {
            double daysOld = (DateTime.UtcNow - actionDate).TotalDays;
            if (daysOld < 0) daysOld = 0;

            // Công thức Decay: Điểm giảm một nửa sau mỗi 30 ngày
            // Hệ số = 1 / (1 + (Ngày_cũ / 30))
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