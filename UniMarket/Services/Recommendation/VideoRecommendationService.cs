using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Models.ML;

namespace UniMarket.Services.Recommendation
{
    // DTO nội bộ: Chỉ lấy những dữ liệu cần thiết để tính toán (Nhẹ RAM)
    public class VideoCandidateDTO
    {
        public int MaTinDang { get; set; }
        public int MaDanhMuc { get; set; }
        public string MaNguoiBan { get; set; } = string.Empty;
        public DateTime NgayDang { get; set; }
        public string TieuDe { get; set; } = string.Empty; // 🔥 Cần cho so khớp từ khóa tìm kiếm
        public decimal Gia { get; set; }                   // 🔥 Cần cho so khớp giá
        public int? MaTinhThanh { get; set; }              // 🔥 Cần cho so khớp khu vực

        // Metrics tương tác (Số liệu thô)
        public int ViewCount { get; set; }
        public int ShareCount { get; set; }
        public int CommentCount { get; set; }
        public int LikeCount { get; set; }
        public int SaveCount { get; set; } // Đã bao gồm cả Save Video + Yêu thích Tin

        // Metrics chất lượng (Tính toán từ lịch sử View)
        public int TotalViewsProcessed { get; set; }
        public int CompletedViews { get; set; }
    }

    public class VideoRecommendationService
    {
        private readonly ApplicationDbContext _context;
        private readonly RecommendationEngine _aiEngine;
        private readonly UserBehaviorService _behaviorService; // 🔥 Inject thêm cái này

        // Cấu hình trọng số (Tùy chỉnh theo chiến lược kinh doanh)
        private const double WEIGHT_LIKE = 2.0;
        private const double WEIGHT_COMMENT = 4.0;
        private const double WEIGHT_SHARE = 8.0;
        private const double WEIGHT_SAVE = 10.0; // 🔥 Tăng lên 10 cho đồng bộ với Service kia

        // Điểm thưởng (Boost)
        private const double BOOST_FOLLOWING = 50.0;     // Follow shop
        private const double BOOST_CATEGORY = 15.0;      // Đúng danh mục hay xem
        private const double BOOST_SEARCH_MATCH = 40.0;  // 🔥 Khớp từ khóa tìm kiếm
        private const double BOOST_PRICE_MATCH = 15.0;   // 🔥 Khớp khoảng giá
        private const double BOOST_LOCATION_MATCH = 10.0;// 🔥 Khớp khu vực

        public VideoRecommendationService(
            ApplicationDbContext context,
            RecommendationEngine aiEngine,
            UserBehaviorService behaviorService) // Inject vào constructor
        {
            _context = context;
            _aiEngine = aiEngine;
            _behaviorService = behaviorService;
        }

        // =================================================================================
        // 🎯 HÀM CHÍNH: LẤY DANH SÁCH ID VIDEO ĐỀ XUẤT
        // =================================================================================
        public async Task<List<int>> GetForYouVideoIds(string? userId, List<int> excludedIds, int count = 10)
        {
            // -----------------------------------------------------
            // BƯỚC 1: LẤY DỮ LIỆU PROFILING CỦA USER (Deep Profile)
            // -----------------------------------------------------
            var userProfile = new UserProfileDto();
            var followingIds = new List<string>();

            if (!string.IsNullOrEmpty(userId))
            {
                // Dùng Service đã viết để lấy profile "xịn" (Search, Price, Location...)
                userProfile = await _behaviorService.AnalyzeUserProfileAsync(userId);

                // Lấy thêm danh sách Follow (cái này BehaviorService chưa lấy nên lấy thêm ở đây)
                followingIds = await _context.Follows.AsNoTracking()
                    .Where(f => f.FollowerId == userId)
                    .Select(f => f.FollowingId)
                    .ToListAsync();
            }

            // -----------------------------------------------------
            // BƯỚC 2: CANDIDATE GENERATION (LỌC ỨNG VIÊN)
            // -----------------------------------------------------
            var query = _context.TinDangs.AsNoTracking()
                .Where(t => t.VideoUrl != null && t.TrangThai == TrangThaiTinDang.DaDuyet)
                .Where(t => !excludedIds.Contains(t.MaTinDang));

            // Tối ưu Query: Nếu có danh mục yêu thích, ưu tiên lấy trong danh mục đó
            if (userProfile.PreferredCategoryIds.Any())
            {
                // Lấy video thuộc category user thích HOẶC video đang hot (View > 50)
                query = query.Where(t => userProfile.PreferredCategoryIds.Contains(t.MaDanhMuc) || t.SoLuotXem > 50);
            }

            // Projection dữ liệu ra RAM
            var candidates = await query
                .OrderByDescending(t => t.NgayDang)
                .Take(400) // Lấy pool 400
                .Select(t => new VideoCandidateDTO
                {
                    MaTinDang = t.MaTinDang,
                    MaDanhMuc = t.MaDanhMuc,
                    MaNguoiBan = t.MaNguoiBan,
                    NgayDang = t.NgayDang,
                    TieuDe = t.TieuDe,      // Lấy thêm
                    Gia = t.Gia,            // Lấy thêm
                    MaTinhThanh = t.MaTinhThanh, // Lấy thêm
                    ViewCount = t.SoLuotXem,

                    // Đếm tương tác
                    LikeCount = _context.VideoLikes.Count(l => l.MaTinDang == t.MaTinDang),
                    CommentCount = _context.VideoComments.Count(c => c.MaTinDang == t.MaTinDang),
                    ShareCount = _context.Shares.Count(s => s.TinDangId == t.MaTinDang),

                    // 🔥 Gộp đếm Save: VideoSave + TinYeuThich
                    SaveCount = _context.VideoTinDangSaves.Count(sv => sv.MaTinDang == t.MaTinDang)
                              + _context.TinDangYeuThichs.Count(ty => ty.MaTinDang == t.MaTinDang),

                    // Chất lượng view
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

                // --- A. ĐIỂM TƯƠNG TÁC (Global Popularity) ---
                finalScore += (video.LikeCount * WEIGHT_LIKE);
                finalScore += (video.CommentCount * WEIGHT_COMMENT);
                finalScore += (video.SaveCount * WEIGHT_SAVE);
                finalScore += (video.ShareCount * WEIGHT_SHARE);

                // --- B. ĐIỂM CHẤT LƯỢNG (Quality) ---
                if (video.TotalViewsProcessed > 5)
                {
                    double completionRate = (double)video.CompletedViews / video.TotalViewsProcessed;
                    if (completionRate > 0.6) finalScore += 20.0;
                    else if (completionRate < 0.2) finalScore -= 10.0;
                }

                // --- C. ĐIỂM CÁ NHÂN HÓA (Personalization - QUAN TRỌNG NHẤT) ---
                if (!string.IsNullOrEmpty(userId))
                {
                    // 1. AI Prediction (Machine Learning - Matrix Factorization)
                    float aiScore = _aiEngine.PredictScore(userId, video.MaTinDang);
                    finalScore += (aiScore * 5.0);

                    // 2. Khớp Từ Khóa Tìm Kiếm (Search History)
                    // Nếu user xóa lịch sử -> List này rỗng -> Không cộng điểm -> Đã học xóa
                    foreach (var kw in userProfile.RecentSearchKeywords)
                    {
                        if (video.TieuDe.ToLower().Contains(kw))
                        {
                            finalScore += BOOST_SEARCH_MATCH;
                            break;
                        }
                    }

                    // 3. Khớp Giá Cả (Price Affinity)
                    if (userProfile.PreferredMaxPrice > 0)
                    {
                        if (video.Gia >= userProfile.PreferredMinPrice && video.Gia <= userProfile.PreferredMaxPrice)
                        {
                            finalScore += BOOST_PRICE_MATCH;
                        }
                        else
                        {
                            // Phạt nhẹ nếu lệch giá quá xa (> 2 lần max price)
                            if (video.Gia > userProfile.PreferredMaxPrice * 2) finalScore -= 5.0;
                        }
                    }

                    // 4. Khớp Khu Vực (Location Affinity)
                    if (userProfile.PreferredLocationId.HasValue && video.MaTinhThanh == userProfile.PreferredLocationId.Value)
                    {
                        finalScore += BOOST_LOCATION_MATCH;
                    }

                    // 5. Khớp Danh Mục
                    if (userProfile.PreferredCategoryIds.Contains(video.MaDanhMuc))
                    {
                        finalScore += BOOST_CATEGORY;
                    }

                    // 6. Follow Shop
                    if (followingIds.Contains(video.MaNguoiBan))
                    {
                        finalScore += BOOST_FOLLOWING;
                    }
                }

                // --- D. ĐIỂM THỜI GIAN (Freshness Decay) ---
                double hoursOld = (now - video.NgayDang).TotalHours;
                if (hoursOld < 24) finalScore += 10.0;

                double daysOld = hoursOld / 24.0;
                if (daysOld > 2)
                {
                    finalScore = finalScore / Math.Log(daysOld + 2);
                }

                // --- E. Randomization ---
                finalScore += (new Random().NextDouble() * 3.0);

                scoredVideos.Add((video.MaTinDang, finalScore));
            }

            // -----------------------------------------------------
            // BƯỚC 4: TRẢ VỀ KẾT QUẢ
            // -----------------------------------------------------
            var resultIds = scoredVideos
                .OrderByDescending(x => x.Score)
                .Take(count * 2) // Lấy top 2x
                .OrderBy(x => Guid.NewGuid()) // Shuffle nhẹ
                .Take(count)
                .Select(x => x.Id)
                .ToList();

            return resultIds;
        }
    }
}