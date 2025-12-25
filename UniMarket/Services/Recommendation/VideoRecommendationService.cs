using Microsoft.EntityFrameworkCore;
using MongoDB.Driver; // Cần thêm thư viện này để query Mongo
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Models.ML;
using UniMarket.Models.Mongo; // Namespace chứa VideoViewLog
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UniMarket.Services.Recommendation
{
    // =================================================================================
    // DTO: CHỨA DỮ LIỆU ĐỂ CHẤM ĐIỂM (Lightweight Object)
    // =================================================================================
    public class VideoCandidateDTO
    {
        public int MaTinDang { get; set; }
        public int MaDanhMuc { get; set; }
        public string MaNguoiBan { get; set; } = string.Empty;
        public DateTime NgayDang { get; set; }
        public string TieuDe { get; set; } = string.Empty;
        public decimal Gia { get; set; }
        public int? MaTinhThanh { get; set; }

        // --- Metrics tương tác (SQL) ---
        public int ViewCount { get; set; }   
        public int ShareCount { get; set; }
        public int CommentCount { get; set; }
        public int LikeCount { get; set; }

        // --- Metrics Lưu/Thích (SQL) ---
        public int SaveCount { get; set; }
        public int FavoriteCount { get; set; }

        // --- Metrics chất lượng View (MongoDB) ---
        // Dữ liệu này sẽ được map từ Mongo sau khi query SQL xong
        public int TotalViewsProcessed { get; set; } = 0;
        public int CompletedViews { get; set; } = 0;
    }

    public class VideoRecommendationService
    {
        private readonly ApplicationDbContext _context;
        private readonly RecommendationEngine _aiEngine;
        private readonly UserBehaviorService _behaviorService;

        // Thêm Collection Mongo để truy vấn Views
        private readonly IMongoCollection<VideoViewLog> _viewLogsCollection;

        // ============================================================
        // 🎯 CẤU HÌNH TRỌNG SỐ (TUNED WEIGHTS)
        // ============================================================

        // 1. Trọng số Tương tác
        private const double WEIGHT_LIKE = 2.0;
        private const double WEIGHT_COMMENT = 4.0;
        private const double WEIGHT_SHARE = 8.0;
        private const double WEIGHT_SAVE = 10.0;
        private const double WEIGHT_FAVORITE = 15.0;

        // 2. Trọng số AI Prediction
        private const double WEIGHT_AI_PREDICTION = 15.0;

        // 3. Điểm thưởng Ngữ cảnh
        private const double BOOST_FOLLOWING = 50.0;
        private const double BOOST_CATEGORY = 30.0;
        private const double BOOST_SEARCH_MATCH = 40.0;
        private const double BOOST_PRICE_MATCH = 25.0;
        private const double BOOST_LOCATION_MATCH = 15.0;

        // 4. Điểm phạt
        private const double PENALTY_REPORTED_SELLER = 50.0;

        // Inject thêm IMongoDatabase để lấy Collection
        public VideoRecommendationService(
            ApplicationDbContext context,
            RecommendationEngine aiEngine,
            UserBehaviorService behaviorService,
            IMongoDatabase mongoDatabase) // <-- Inject Mongo Database
        {
            _context = context;
            _aiEngine = aiEngine;
            _behaviorService = behaviorService;
            // Map vào collection 'VideoViewLogs' (hoặc tên bạn đặt trong Mongo)
            _viewLogsCollection = mongoDatabase.GetCollection<VideoViewLog>("VideoViewLogs");
        }

        // =================================================================================
        // 🎯 HÀM CHÍNH: LẤY DANH SÁCH ID BÀI ĐĂNG ĐỀ XUẤT
        // =================================================================================
        public async Task<List<int>> GetRecommendedPostIds(string? userId, List<int> clientExcludedIds, int count = 10, bool isVideoOnly = true, string? categoryGroup = null)
        {
            var finalExcludedIds = new List<int>(clientExcludedIds);
            var userProfile = new UserProfileDto();
            var followingIds = new List<string>();
            var reportedSellerIds = new List<string>();

            // -----------------------------------------------------
            // BƯỚC 1: PHÂN TÍCH USER & XÂY DỰNG BLACKLIST
            // -----------------------------------------------------
            if (!string.IsNullOrEmpty(userId))
            {
                userProfile = await _behaviorService.AnalyzeUserProfileAsync(userId);

                followingIds = await _context.Follows.AsNoTracking()
                    .Where(f => f.FollowerId == userId)
                    .Select(f => f.FollowingId)
                    .ToListAsync();

                var reportedPostIds = await _context.Reports.AsNoTracking()
                    .Where(r => r.ReporterId == userId && r.TargetType == ReportTargetType.Post)
                    .Select(r => r.TargetId)
                    .ToListAsync();

                finalExcludedIds.AddRange(reportedPostIds);

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
            // BƯỚC 2: TẠO TẬP ỨNG VIÊN (QUERY SQL SERVER)
            // -----------------------------------------------------
            var query = _context.TinDangs.AsNoTracking()
                .Include(t => t.DanhMuc).ThenInclude(dm => dm.DanhMucCha)
                .Where(t => t.TrangThai == TrangThaiTinDang.DaDuyet)
                .Where(t => !finalExcludedIds.Contains(t.MaTinDang));

            if (!string.IsNullOrEmpty(categoryGroup))
            {
                var keyword = categoryGroup.ToLower().Trim();
                query = query.Where(t =>
                    t.DanhMuc.DanhMucCha != null &&
                    t.DanhMuc.DanhMucCha.TenDanhMucCha.ToLower().Contains(keyword)
                );
            }

            if (isVideoOnly)
            {
                query = query.Where(t => t.VideoUrl != null && t.VideoUrl != "");
            }
            else
            {
                query = query.Where(t => t.AnhTinDangs.Any() || t.VideoUrl != null);
            }

            if (userProfile.HasData)
            {
                query = query.Where(t =>
                    userProfile.PreferredCategoryIds.Contains(t.MaDanhMuc) ||
                    (t.Gia >= userProfile.PreferredMinPrice && t.Gia <= userProfile.PreferredMaxPrice) ||
                    t.SoLuotXem > 100
                );
            }
            else
            {
                query = query.Where(t => t.NgayDang >= DateTime.UtcNow.AddDays(-30));
            }

            // --- LẤY DỮ LIỆU TỪ SQL TRƯỚC (BỎ PHẦN VIEW LOG CỦA MONGO RA KHỎI ĐÂY) ---
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

                    ViewCount = t.SoLuotXem, // SQL View (hiển thị)

                    LikeCount = _context.VideoLikes.Count(l => l.MaTinDang == t.MaTinDang),
                    CommentCount = _context.VideoComments.Count(c => c.MaTinDang == t.MaTinDang),
                    ShareCount = _context.Shares.Count(s => s.TinDangId == t.MaTinDang),
                    SaveCount = _context.VideoTinDangSaves.Count(sv => sv.MaTinDang == t.MaTinDang),
                    FavoriteCount = _context.TinDangYeuThichs.Count(ty => ty.MaTinDang == t.MaTinDang),

                    // Tạm thời để 0, sẽ fill từ Mongo ở bước sau
                    TotalViewsProcessed = 0,
                    CompletedViews = 0
                })
                .ToListAsync();

            // -----------------------------------------------------
            // BƯỚC 2.5: LẤY DỮ LIỆU VIEW TỪ MONGODB (AGGREGATION)
            // -----------------------------------------------------
            if (candidates.Any())
            {
                // Lấy danh sách ID các video cần check view
                var candidateIds = candidates.Select(c => c.MaTinDang).ToList();

                // Query Aggregate trên Mongo: Group theo MaTinDang
                var viewStats = await _viewLogsCollection.Aggregate()
                    .Match(x => candidateIds.Contains(x.MaTinDang)) // Chỉ lấy log của 500 video trên
                    .Group(x => x.MaTinDang, g => new
                    {
                        MaTinDang = g.Key,
                        TotalCount = g.Count(),
                        CompletedCount = g.Sum(x => x.IsCompleted ? 1 : 0)
                    })
                    .ToListAsync();

                // Merge dữ liệu Mongo vào List Candidates (In-Memory Join)
                foreach (var candidate in candidates)
                {
                    var stat = viewStats.FirstOrDefault(s => s.MaTinDang == candidate.MaTinDang);
                    if (stat != null)
                    {
                        candidate.TotalViewsProcessed = (int)stat.TotalCount;
                        candidate.CompletedViews = stat.CompletedCount;
                    }
                }
            }

            // -----------------------------------------------------
            // BƯỚC 3: SCORING & RANKING (CHẤM ĐIỂM)
            // -----------------------------------------------------
            var scoredVideos = new List<(int Id, double Score)>();
            var now = DateTime.UtcNow;

            foreach (var video in candidates)
            {
                double finalScore = 0;

                // --- A. ĐIỂM TƯƠNG TÁC ---
                finalScore += (video.LikeCount * WEIGHT_LIKE);
                finalScore += (video.CommentCount * WEIGHT_COMMENT);
                finalScore += (video.ShareCount * WEIGHT_SHARE);
                finalScore += (video.SaveCount * WEIGHT_SAVE);
                finalScore += (video.FavoriteCount * WEIGHT_FAVORITE);

                // --- B. ĐIỂM CHẤT LƯỢNG VIEW (Dữ liệu từ Mongo đã merge ở trên) ---

                // 1. Logarithmic Score từ View hiển thị (SQL)
                if (video.ViewCount > 0)
                {
                    finalScore += Math.Log10(video.ViewCount) * 5.0;
                }

                // 2. Completion Rate Bonus (Tính từ Mongo Data)
                if (video.TotalViewsProcessed > 5)
                {
                    double completionRate = (double)video.CompletedViews / video.TotalViewsProcessed;
                    if (completionRate > 0.6) finalScore += 20.0;
                    else if (completionRate < 0.2) finalScore -= 10.0;
                }

                // --- C. ĐIỂM CÁ NHÂN HÓA ---
                if (!string.IsNullOrEmpty(userId))
                {
                    float aiScore = _aiEngine.PredictScore(userId, video.MaTinDang);
                    finalScore += (aiScore * WEIGHT_AI_PREDICTION);

                    foreach (var kw in userProfile.RecentSearchKeywords)
                    {
                        if (video.TieuDe.ToLower().Contains(kw))
                        {
                            finalScore += BOOST_SEARCH_MATCH;
                            break;
                        }
                    }

                    if (userProfile.PreferredCategoryIds.Contains(video.MaDanhMuc))
                    {
                        finalScore += BOOST_CATEGORY;
                    }

                    if (userProfile.PreferredMaxPrice > 0)
                    {
                        if (video.Gia >= userProfile.PreferredMinPrice && video.Gia <= userProfile.PreferredMaxPrice)
                            finalScore += BOOST_PRICE_MATCH;
                        else if (video.Gia > userProfile.PreferredMaxPrice * 2)
                            finalScore -= 5.0;
                    }

                    if (userProfile.PreferredLocationId.HasValue && video.MaTinhThanh == userProfile.PreferredLocationId.Value)
                        finalScore += BOOST_LOCATION_MATCH;

                    if (followingIds.Contains(video.MaNguoiBan))
                        finalScore += BOOST_FOLLOWING;

                    if (reportedSellerIds.Contains(video.MaNguoiBan))
                    {
                        finalScore -= PENALTY_REPORTED_SELLER;
                    }
                }

                // --- D. TIME DECAY ---
                double hoursOld = (now - video.NgayDang).TotalHours;
                if (hoursOld < 24) finalScore += 20.0;

                double daysOld = hoursOld / 24.0;
                if (daysOld > 1.5)
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