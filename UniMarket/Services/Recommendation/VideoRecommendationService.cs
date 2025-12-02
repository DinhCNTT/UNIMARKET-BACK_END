using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Models.ML;

namespace UniMarket.Services.Recommendation
{
    // =================================================================================
    // DTO: CHỨA DỮ LIỆU ĐỂ CHẤM ĐIỂM (Lightweight Object)
    // Tối ưu hóa việc lấy dữ liệu 1 lần, không query database trong vòng lặp scoring
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

        // 🔥 ĐÃ TÁCH RIÊNG (Theo yêu cầu Code 2):
        public int SaveCount { get; set; }      // Chỉ đếm VideoTinDangSave (Lưu để xem lại nội dung)
        public int FavoriteCount { get; set; }  // Chỉ đếm TinDangYeuThich (Quan tâm mua sản phẩm)

        // Metrics chất lượng View (Tính từ lịch sử xem)
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

        // 🔥 Cập nhật trọng số mới (Code 2)
        private const double WEIGHT_SAVE = 7.0;        // Quan tâm nội dung video
        private const double WEIGHT_FAVORITE = 12.0;   // Quan tâm sản phẩm (Tín hiệu mua hàng mạnh nhất)

        // 2. Trọng số AI
        private const double WEIGHT_AI_PREDICTION = 10.0;

        // 3. Điểm thưởng Ngữ cảnh (Positive Boosts)
        private const double BOOST_FOLLOWING = 50.0;
        private const double BOOST_CATEGORY = 15.0;
        private const double BOOST_SEARCH_MATCH = 40.0;
        private const double BOOST_PRICE_MATCH = 20.0;
        private const double BOOST_LOCATION_MATCH = 15.0;

        // 4. Điểm phạt (Negative Penalties)
        // Phạt người bán mà user từng báo cáo xấu (Soft Filter)
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
        // 🎯 HÀM CHÍNH: LẤY DANH SÁCH ID VIDEO ĐỀ XUẤT
        // =================================================================================
        public async Task<List<int>> GetForYouVideoIds(string? userId, List<int> clientExcludedIds, int count = 10)
        {
            var finalExcludedIds = new List<int>(clientExcludedIds);
            var userProfile = new UserProfileDto();
            var followingIds = new List<string>();
            var reportedSellerIds = new List<string>(); // Danh sách người bán bị user này ghét

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
                // Join bảng Report với TinDang để tìm ra ai là chủ nhân của cái tin bị report đó
                reportedSellerIds = await _context.Reports.AsNoTracking()
                    .Where(r => r.ReporterId == userId && r.TargetType == ReportTargetType.Post)
                    .Join(_context.TinDangs,
                          report => report.TargetId,
                          post => post.MaTinDang,
                          (report, post) => post.MaNguoiBan) // Chỉ lấy ID người bán
                    .Distinct()
                    .ToListAsync();
            }

            // -----------------------------------------------------
            // BƯỚC 2: TẠO TẬP ỨNG VIÊN (CANDIDATE GENERATION)
            // -----------------------------------------------------
            var query = _context.TinDangs.AsNoTracking()
                .Where(t => t.VideoUrl != null && t.TrangThai == TrangThaiTinDang.DaDuyet)
                .Where(t => !finalExcludedIds.Contains(t.MaTinDang));

            // Chiến lược Cold/Warm Start
            if (userProfile.PreferredCategoryIds.Any())
            {
                query = query.Where(t => userProfile.PreferredCategoryIds.Contains(t.MaDanhMuc) || t.SoLuotXem > 100);
            }
            else
            {
                query = query.Where(t => t.NgayDang >= DateTime.UtcNow.AddDays(-30));
            }

            // Projection ra DTO (Updated: Tách SaveCount và FavoriteCount)
            var candidates = await query
                .OrderByDescending(t => t.NgayDang)
                .Take(500)
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

                    // 🔥 TÁCH RIÊNG Ở ĐÂY:
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

            foreach (var video in candidates)
            {
                double finalScore = 0;

                // --- A. ĐIỂM TƯƠNG TÁC (Đã cập nhật Code 2) ---
                finalScore += (video.LikeCount * WEIGHT_LIKE);
                finalScore += (video.CommentCount * WEIGHT_COMMENT);
                finalScore += (video.ShareCount * WEIGHT_SHARE);

                // Cộng điểm riêng biệt với trọng số mới
                finalScore += (video.SaveCount * WEIGHT_SAVE);         // +7đ mỗi lượt lưu
                finalScore += (video.FavoriteCount * WEIGHT_FAVORITE); // +12đ mỗi lượt yêu thích

                // --- B. ĐIỂM CHẤT LƯỢNG VIEW ---
                if (video.TotalViewsProcessed > 5)
                {
                    double completionRate = (double)video.CompletedViews / video.TotalViewsProcessed;
                    if (completionRate > 0.6) finalScore += 20.0;
                    else if (completionRate < 0.2) finalScore -= 10.0;
                }

                // --- C. ĐIỂM CÁ NHÂN HÓA ---
                if (!string.IsNullOrEmpty(userId))
                {
                    // 1. AI Prediction
                    float aiScore = _aiEngine.PredictScore(userId, video.MaTinDang);
                    finalScore += (aiScore * WEIGHT_AI_PREDICTION);

                    // 2. Search Match
                    foreach (var kw in userProfile.RecentSearchKeywords)
                    {
                        if (video.TieuDe.ToLower().Contains(kw))
                        {
                            finalScore += BOOST_SEARCH_MATCH;
                            break;
                        }
                    }

                    // 3. Price Match
                    if (userProfile.PreferredMaxPrice > 0)
                    {
                        if (video.Gia >= userProfile.PreferredMinPrice && video.Gia <= userProfile.PreferredMaxPrice)
                            finalScore += BOOST_PRICE_MATCH;
                        else if (video.Gia > userProfile.PreferredMaxPrice * 2)
                            finalScore -= 5.0;
                    }

                    // 4. Location Match
                    if (userProfile.PreferredLocationId.HasValue && video.MaTinhThanh == userProfile.PreferredLocationId.Value)
                        finalScore += BOOST_LOCATION_MATCH;

                    // 5. Follow Shop
                    if (followingIds.Contains(video.MaNguoiBan))
                        finalScore += BOOST_FOLLOWING;

                    // 6. 🔥 PHẠT UY TÍN NGƯỜI BÁN (REPUTATION PENALTY)
                    // Nếu tin này thuộc về người bán mà user từng báo cáo xấu
                    if (reportedSellerIds.Contains(video.MaNguoiBan))
                    {
                        finalScore -= PENALTY_REPORTED_SELLER;
                    }
                }

                // --- D. TIME DECAY ---
                double hoursOld = (now - video.NgayDang).TotalHours;
                if (hoursOld < 24) finalScore += 10.0;

                double daysOld = hoursOld / 24.0;
                if (daysOld > 2)
                {
                    finalScore = finalScore / Math.Log(daysOld + 2);
                }

                // --- E. RANDOMIZATION ---
                finalScore += (new Random().NextDouble() * 5.0);

                scoredVideos.Add((video.MaTinDang, finalScore));
            }

            // -----------------------------------------------------
            // BƯỚC 4: SẮP XẾP & TRẢ VỀ
            // -----------------------------------------------------
            var resultIds = scoredVideos
                .OrderByDescending(x => x.Score)
                .Take(count * 2)
                .OrderBy(x => Guid.NewGuid())
                .Take(count)
                .Select(x => x.Id)
                .ToList();

            return resultIds;
        }
    }
}