using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Models.ML;

namespace UniMarket.Services.Recommendation
{
    // =================================================================================
    // DTO: CHỨA DỮ LIỆU ĐỂ CHẤM ĐIỂM (Lightweight Object)
    // Dùng chung cho cả Video và Tin đăng thường
    // =================================================================================
    public class VideoCandidateDTO
    {
        public int MaTinDang { get; set; }
        public int MaDanhMuc { get; set; }
        public string MaNguoiBan { get; set; } = string.Empty;
        public DateTime NgayDang { get; set; }
        public string TieuDe { get; set; } = string.Empty; // Để so khớp từ khóa tìm kiếm
        public decimal Gia { get; set; }                   // Để so khớp khoảng giá
        public int? MaTinhThanh { get; set; }              // Để so khớp khu vực

        // --- Metrics tương tác (Số liệu thô) ---
        public int ViewCount { get; set; }
        public int ShareCount { get; set; }
        public int CommentCount { get; set; }
        public int LikeCount { get; set; }

        public int SaveCount { get; set; }      // Lưu để xem lại
        public int FavoriteCount { get; set; }  // Yêu thích (Quan tâm mua)

        // Metrics chất lượng View (Tính từ lịch sử xem - chủ yếu cho Video)
        public int TotalViewsProcessed { get; set; }
        public int CompletedViews { get; set; }
    }

    public class VideoRecommendationService
    {
        private readonly ApplicationDbContext _context;
        private readonly RecommendationEngine _aiEngine;
        private readonly UserBehaviorService _behaviorService;

        // ============================================================
        // CẤU HÌNH TRỌNG SỐ (WEIGHTS & BOOSTS)
        // ============================================================

        // 1. Trọng số Tương tác
        private const double WEIGHT_LIKE = 2.0;
        private const double WEIGHT_COMMENT = 4.0;
        private const double WEIGHT_SHARE = 8.0;

        private const double WEIGHT_SAVE = 7.0;        // Quan tâm nội dung
        private const double WEIGHT_FAVORITE = 12.0;   // Quan tâm sản phẩm (Tín hiệu mua hàng)

        // 2. Trọng số AI
        private const double WEIGHT_AI_PREDICTION = 10.0;

        // 3. Điểm thưởng Ngữ cảnh (Positive Boosts)
        private const double BOOST_FOLLOWING = 50.0;
        private const double BOOST_CATEGORY = 15.0;
        private const double BOOST_SEARCH_MATCH = 40.0;
        private const double BOOST_PRICE_MATCH = 20.0;
        private const double BOOST_LOCATION_MATCH = 15.0;

        // 4. Điểm phạt (Negative Penalties)
        private const double PENALTY_REPORTED_SELLER = 20.0;

        public VideoRecommendationService(
            ApplicationDbContext context,
            RecommendationEngine aiEngine,
            UserBehaviorService behaviorService)
        {
            _context = context;
            _aiEngine = aiEngine;
            _behaviorService = behaviorService;
        }

        // =================================================================================
        // 🎯 HÀM CHÍNH: LẤY DANH SÁCH ID ĐỀ XUẤT (VIDEO HOẶC TIN THƯỜNG)
        // =================================================================================
        public async Task<List<int>> GetRecommendedPostIds(string? userId, List<int> clientExcludedIds, int count = 10, bool isVideoOnly = true)
        {
            var finalExcludedIds = new List<int>(clientExcludedIds);
            var userProfile = new UserProfileDto();
            var followingIds = new List<string>();
            var reportedSellerIds = new List<string>();

            // -----------------------------------------------------
            // BƯỚC 1: PHÂN TÍCH USER & XÂY DỰNG BLACKLIST/PENALTY LIST
            // -----------------------------------------------------
            if (!string.IsNullOrEmpty(userId))
            {
                // 1.1. Lấy chân dung (Profile)
                userProfile = await _behaviorService.AnalyzeUserProfileAsync(userId);

                // 1.2. Lấy danh sách đang Follow
                followingIds = await _context.Follows.AsNoTracking()
                    .Where(f => f.FollowerId == userId)
                    .Select(f => f.FollowingId)
                    .ToListAsync();

                // 1.3. Lọc Báo xấu (Hard Filter) - Ẩn hoàn toàn tin đã report
                var reportedPostIds = await _context.Reports.AsNoTracking()
                    .Where(r => r.ReporterId == userId && r.TargetType == ReportTargetType.Post)
                    .Select(r => r.TargetId)
                    .ToListAsync();

                finalExcludedIds.AddRange(reportedPostIds);

                // 1.4. Lấy danh sách Người bán từng bị Report (Soft Filter)
                reportedSellerIds = await _context.Reports.AsNoTracking()
                    .Where(r => r.ReporterId == userId && r.TargetType == ReportTargetType.Post)
                    .Join(_context.TinDangs,
                          report => report.TargetId,
                          post => post.MaTinDang,
                          (report, post) => post.MaNguoiBan)
                    .Distinct()
                    .ToListAsync();
            }

            // -----------------------------------------------------
            // BƯỚC 2: TẠO TẬP ỨNG VIÊN (CANDIDATE GENERATION)
            // -----------------------------------------------------
            var query = _context.TinDangs.AsNoTracking()
                .Where(t => t.TrangThai == TrangThaiTinDang.DaDuyet) // Chỉ lấy tin đã duyệt
                .Where(t => !finalExcludedIds.Contains(t.MaTinDang));

            // 🔥 PHÂN LUỒNG: Lấy Video Only hay Lấy cả Tin thường
            if (isVideoOnly)
            {
                // Luồng 1: Chỉ đề xuất Video (Cho Video Feed lướt kiểu TikTok)
                query = query.Where(t => t.VideoUrl != null && t.VideoUrl != "");
            }
            else
            {
                // Luồng 2: Đề xuất Tin đăng (Cho Trang chủ/Dành cho bạn)
                // Lấy tin có ảnh HOẶC có video (đảm bảo có nội dung media để hiển thị)
                query = query.Where(t => t.AnhTinDangs.Any() || (t.VideoUrl != null && t.VideoUrl != ""));
            }

            // Chiến lược Cold/Warm Start
            if (userProfile.PreferredCategoryIds.Any())
            {
                // Nếu user có sở thích, lấy theo danh mục hoặc tin nổi bật (view > 50)
                query = query.Where(t => userProfile.PreferredCategoryIds.Contains(t.MaDanhMuc) || t.SoLuotXem > 50);
            }
            else
            {
                // Nếu user mới (Cold Start), lấy tin mới nhất trong 30 ngày
                query = query.Where(t => t.NgayDang >= DateTime.UtcNow.AddDays(-30));
            }

            // Projection ra DTO
            var candidates = await query
                .OrderByDescending(t => t.NgayDang)
                .Take(500) // Lấy pool 500 tin để chấm điểm
                .Select(t => new VideoCandidateDTO
                {
                    MaTinDang = t.MaTinDang,
                    MaDanhMuc = t.MaDanhMuc,
                    MaNguoiBan = t.MaNguoiBan,
                    NgayDang = t.NgayDang,
                    TieuDe = t.TieuDe,
                    Gia = t.Gia,
                    MaTinhThanh = t.MaTinhThanh,
                    ViewCount = t.SoLuotXem,

                    LikeCount = _context.VideoLikes.Count(l => l.MaTinDang == t.MaTinDang),
                    CommentCount = _context.VideoComments.Count(c => c.MaTinDang == t.MaTinDang),
                    ShareCount = _context.Shares.Count(s => s.TinDangId == t.MaTinDang),

                    SaveCount = _context.VideoTinDangSaves.Count(sv => sv.MaTinDang == t.MaTinDang),
                    FavoriteCount = _context.TinDangYeuThichs.Count(ty => ty.MaTinDang == t.MaTinDang),

                    TotalViewsProcessed = _context.VideoViews.Count(v => v.MaTinDang == t.MaTinDang),
                    CompletedViews = _context.VideoViews.Count(v => v.MaTinDang == t.MaTinDang && v.IsCompleted)
                })
                .ToListAsync();

            // -----------------------------------------------------
            // BƯỚC 3: SCORING & RANKING (CHẤM ĐIỂM CHI TIẾT)
            // -----------------------------------------------------
            var scoredVideos = new List<(int Id, double Score)>();
            var now = DateTime.UtcNow;

            foreach (var item in candidates)
            {
                double finalScore = 0;

                // --- A. ĐIỂM TƯƠNG TÁC ---
                finalScore += (item.LikeCount * WEIGHT_LIKE);
                finalScore += (item.CommentCount * WEIGHT_COMMENT);
                finalScore += (item.ShareCount * WEIGHT_SHARE);
                finalScore += (item.SaveCount * WEIGHT_SAVE);
                finalScore += (item.FavoriteCount * WEIGHT_FAVORITE);

                // --- B. ĐIỂM CHẤT LƯỢNG VIEW (Nếu là video) ---
                if (item.TotalViewsProcessed > 5)
                {
                    double completionRate = (double)item.CompletedViews / item.TotalViewsProcessed;
                    if (completionRate > 0.6) finalScore += 20.0;
                    else if (completionRate < 0.2) finalScore -= 10.0;
                }

                // --- C. ĐIỂM CÁ NHÂN HÓA ---
                if (!string.IsNullOrEmpty(userId))
                {
                    // 1. AI Prediction
                    float aiScore = _aiEngine.PredictScore(userId, item.MaTinDang);
                    finalScore += (aiScore * WEIGHT_AI_PREDICTION);

                    // 2. Search Match (Ưu tiên tin chứa từ khóa user hay tìm)
                    foreach (var kw in userProfile.RecentSearchKeywords)
                    {
                        if (item.TieuDe.ToLower().Contains(kw))
                        {
                            finalScore += BOOST_SEARCH_MATCH;
                            break;
                        }
                    }

                    // 3. Price Match (Khớp khoảng giá user quan tâm)
                    if (userProfile.PreferredMaxPrice > 0)
                    {
                        if (item.Gia >= userProfile.PreferredMinPrice && item.Gia <= userProfile.PreferredMaxPrice)
                            finalScore += BOOST_PRICE_MATCH;
                        else if (item.Gia > userProfile.PreferredMaxPrice * 2)
                            finalScore -= 5.0;
                    }

                    // 4. Location Match (Khớp khu vực)
                    if (userProfile.PreferredLocationId.HasValue && item.MaTinhThanh == userProfile.PreferredLocationId.Value)
                        finalScore += BOOST_LOCATION_MATCH;

                    // 5. Follow Shop (Shop user đang theo dõi)
                    if (followingIds.Contains(item.MaNguoiBan))
                        finalScore += BOOST_FOLLOWING;

                    // 6. PHẠT NGƯỜI BÁN (REPUTATION PENALTY)
                    if (reportedSellerIds.Contains(item.MaNguoiBan))
                    {
                        finalScore -= PENALTY_REPORTED_SELLER;
                    }
                }

                // --- D. TIME DECAY (Tin mới ưu tiên hơn tin cũ) ---
                double hoursOld = (now - item.NgayDang).TotalHours;
                if (hoursOld < 24) finalScore += 10.0;

                double daysOld = hoursOld / 24.0;
                if (daysOld > 2)
                {
                    finalScore = finalScore / Math.Log(daysOld + 2);
                }

                // --- E. RANDOMIZATION (Tránh lặp lại nhàm chán) ---
                finalScore += (new Random().NextDouble() * 5.0);

                scoredVideos.Add((item.MaTinDang, finalScore));
            }

            // -----------------------------------------------------
            // BƯỚC 4: SẮP XẾP & TRẢ VỀ
            // -----------------------------------------------------
            var resultIds = scoredVideos
                .OrderByDescending(x => x.Score)
                .Take(count * 2)             // Lấy top 2x số lượng cần
                .OrderBy(x => Guid.NewGuid()) // Shuffle nhẹ để tạo ngẫu nhiên trong top
                .Take(count)
                .Select(x => x.Id)
                .ToList();

            return resultIds;
        }
    }
}