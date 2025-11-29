using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Models.ML;

namespace UniMarket.Services.Recommendation
{
    // ============================================================
    // DTO: CHÂN DUNG KHÁCH HÀNG (User Profiling)
    // Dùng để lọc nhanh (Content-Based) trước khi chạy AI chuyên sâu
    // ============================================================
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

        // --- Hành vi Tương tác Tích cực (Explicit Feedback) ---
        private const float SCORE_LIKE = 5.0f;              // Thả tim
        private const float SCORE_COMMENT = 8.0f;           // Bình luận (Nỗ lực cao hơn like)
        private const float SCORE_SAVE = 10.0f;             // Lưu (Ý định mua/xem lại cao)

        // --- Hành vi Lan tỏa (Viral/Social) ---
        private const float SCORE_SHARE_INTERNAL = 10.0f;   // Share qua chat nội bộ
        private const float SCORE_SHARE_SOCIAL = 15.0f;     // Share ra Facebook/Zalo (Viral cao nhất)

        // --- Hành vi Tiêu cực (Negative Feedback) ---
        // Rất quan trọng để đẩy các nội dung rác/spam ra xa người dùng
        private const float SCORE_REPORT = -50.0f;          // Báo cáo (Ghét cay ghét đắng)

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

            // A. Học từ Lịch Sử Tìm Kiếm (Lấy 10 từ khóa gần nhất)
            profile.RecentSearchKeywords = await _context.SearchHistories
                .AsNoTracking()
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.CreatedAt)
                .Take(10)
                .Select(h => h.Keyword.ToLower())
                .ToListAsync();

            // B. Học từ Tương tác (Like, Save) - TỐI ƯU HÓA TỐC ĐỘ QUERY
            // Thay vì join lồng nhau, ta lấy ID ra trước rồi lọc
            var likedPostIds = await _context.VideoLikes
                .AsNoTracking().Where(l => l.UserId == userId).Select(l => l.MaTinDang).ToListAsync();

            var savedPostIds = await _context.VideoTinDangSaves
                .AsNoTracking().Where(s => s.MaNguoiDung == userId).Select(s => s.MaTinDang).ToListAsync();

            // Gộp danh sách ID và loại bỏ trùng lặp
            var interactedIds = likedPostIds.Concat(savedPostIds).Distinct().ToList();

            if (interactedIds.Any())
            {
                // Truy vấn tin đăng dựa trên list ID (Nhanh hơn nhiều so với subquery)
                var interactiveVideos = await _context.TinDangs
                    .AsNoTracking()
                    .Where(t => interactedIds.Contains(t.MaTinDang))
                    .OrderByDescending(t => t.NgayDang)
                    .Take(50) // Chỉ phân tích 50 tin gần nhất để bắt trend sở thích mới
                    .Select(t => new { t.Gia, t.MaTinhThanh, t.MaDanhMuc })
                    .ToListAsync();

                if (interactiveVideos.Any())
                {
                    // 1. Học Giá Cả (Price Affinity): Trung bình +/- 30%
                    var avgPrice = interactiveVideos.Average(x => x.Gia);
                    profile.PreferredMinPrice = avgPrice * 0.7m;
                    profile.PreferredMaxPrice = avgPrice * 1.3m;

                    // 2. Học Khu Vực (Location Affinity): Mode (xuất hiện nhiều nhất)
                    profile.PreferredLocationId = interactiveVideos
                        .GroupBy(x => x.MaTinhThanh)
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key)
                        .FirstOrDefault();

                    // 3. Học Danh Mục (Category Affinity): Top 3
                    profile.PreferredCategoryIds = interactiveVideos
                        .GroupBy(x => x.MaDanhMuc)
                        .OrderByDescending(g => g.Count())
                        .Take(3)
                        .Select(g => g.Key)
                        .ToList();
                }
            }

            return profile;
        }

        // ============================================================
        // 3. LẤY DỮ LIỆU HUẤN LUYỆN (TRAINING DATA GENERATION)
        // Dùng cho Collaborative Filtering (Matrix Factorization)
        // ============================================================
        public async Task<List<VideoRating>> GetTrainingDataAsync()
        {
            // Dictionary cộng dồn điểm: (UserId, VideoId) -> TotalScore
            var tempScores = new Dictionary<(string userId, int videoId), float>();

            // Chỉ lấy dữ liệu trong 90 ngày gần nhất (Tránh User drift - sở thích thay đổi)
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
                    // Chưa xem hết: > 10s thì trung bình, ngược lại thấp
                    score = (v.WatchedSeconds >= 10) ? SCORE_VIEW_MEDIUM : SCORE_VIEW_SHORT;
                }
                else
                {
                    score = SCORE_VIEW_COMPLETED; // Xem hết
                }

                // Cộng điểm xem lại (Cap ở 5 lần)
                if (v.RewatchCount > 0)
                {
                    int validRewatch = Math.Min(v.RewatchCount, 5);
                    score += (validRewatch * SCORE_REWATCH_BONUS);
                }

                AddScoreWithDecay(tempScores, v.UserId!, v.MaTinDang, score, v.StartedAt);
            }

            // ---------------------------------------------------------
            // B. XỬ LÝ TƯƠNG TÁC TÍCH CỰC (LIKE, COMMENT, SAVE, SHARE)
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
            // C. XỬ LÝ TÍN HIỆU TIÊU CỰC (REPORTS) - CỰC KỲ QUAN TRỌNG
            // ---------------------------------------------------------
            var reports = await _context.Reports
                .AsNoTracking()
                .Where(r => r.CreatedAt >= cutOffDate && r.TargetType == ReportTargetType.Post)
                .Select(r => new { r.ReporterId, r.TargetId, r.CreatedAt })
                .ToListAsync();

            foreach (var r in reports)
            {
                // Trừ điểm thật nặng (-50) để AI đẩy vector sở thích ra xa item này
                AddScoreWithDecay(tempScores, r.ReporterId, r.TargetId, SCORE_REPORT, r.CreatedAt.DateTime);
            }

            // ---------------------------------------------------------
            // D. CHUYỂN ĐỔI SANG MODEL ĐẦU VÀO CHO AI
            // ---------------------------------------------------------
            var trainingData = new List<VideoRating>();

            foreach (var item in tempScores)
            {
                // Lọc nhiễu: Chỉ lấy các tương tác có độ lớn đáng kể (> 0.5 hoặc < -0.5)
                if (Math.Abs(item.Value) > 0.5f)
                {
                    trainingData.Add(new VideoRating
                    {
                        UserId = item.Key.userId,
                        VideoId = (float)item.Key.videoId,
                        // Label này chứa cả điểm ÂM (ghét) và DƯƠNG (thích)
                        Label = item.Value
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

            // Công thức Decay: Giá trị giảm dần theo thời gian
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