using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.DTO;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using UniMarket.Hubs;
using UniMarket.Services.Recommendation;
using UniMarket.Helpers;
using UniMarket.Extensions;
using UniMarket.Services;
using UniMarket.Services.Interfaces;
using UniMarket.Services.PriceAnalysis;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using UniMarket.Models.Mongo;
using MongoDB.Bson;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VideoController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<VideoHub> _hubContext;
        private readonly IUserNotificationService _notiService;
        private readonly RecommendationEngine _aiEngine;
        private readonly UserBehaviorService _behaviorService;
        private readonly ISearchService _searchService;
        private readonly PriceAnalysisService _priceAnalysisService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly VideoRecommendationService _recommendationService;
        private readonly IMongoCollection<TinDangDetail> _tinDangDetailsCollection;
        private readonly IMongoCollection<VideoViewLog> _viewLogCollection;


        public VideoController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IHubContext<VideoHub> hubContext,
            RecommendationEngine aiEngine,
            IUserNotificationService notiService,
            UserBehaviorService behaviorService,
            ISearchService searchService,
            PriceAnalysisService priceAnalysisService,
            IServiceScopeFactory scopeFactory,
            IMongoDatabase mongoDatabase,
            VideoRecommendationService recommendationService)
        {
            _context = context;
            _userManager = userManager;
            _hubContext = hubContext;
            _aiEngine = aiEngine;
            _notiService = notiService;
            _behaviorService = behaviorService;
            _searchService = searchService;
            _priceAnalysisService = priceAnalysisService;
            _scopeFactory = scopeFactory;
            _recommendationService = recommendationService;
            _tinDangDetailsCollection = mongoDatabase.GetCollection<TinDangDetail>("TinDangDetails");
            _viewLogCollection = mongoDatabase.GetCollection<VideoViewLog>("VideoViews");
        }

        // =================================================================================
        // API: LẤY DANH SÁCH VIDEO (GỢI Ý THÔNG MINH)
        // =================================================================================
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetVideos(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15,
        [FromQuery] int? categoryId = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null)
        {
            var userId = User.Identity != null && User.Identity.IsAuthenticated
                ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;

            // -----------------------------------------------------------------
            // BƯỚC 1: LẤY PROFILE NGƯỜI DÙNG (REAL-TIME)
            // -----------------------------------------------------------------
            UserProfileDto userProfile = new UserProfileDto();
            if (userId != null)
            {
                userProfile = await _behaviorService.AnalyzeUserProfileAsync(userId);
            }

            // -----------------------------------------------------------------
            // BƯỚC 2: CANDIDATE GENERATION (LỌC ỨNG VIÊN SƠ BỘ)
            // -----------------------------------------------------------------
            IQueryable<TinDang> query = _context.TinDangs
                .AsNoTracking() // Tối ưu hiệu năng đọc
                .Where(td => td.VideoUrl != null && td.TrangThai == TrangThaiTinDang.DaDuyet);

            // Áp dụng bộ lọc cứng từ UI
            if (categoryId.HasValue)
            {
                query = query.Where(td => td.MaDanhMuc == categoryId.Value ||
                                        (td.DanhMuc != null && td.DanhMuc.MaDanhMucCha == categoryId.Value));
            }
            if (minPrice.HasValue) query = query.Where(td => td.Gia >= minPrice.Value);
            if (maxPrice.HasValue) query = query.Where(td => td.Gia <= maxPrice.Value);

            // Lấy 400 tin mới nhất/phù hợp nhất để làm Pool cho AI sắp xếp
            var candidates = await query
                .OrderByDescending(t => t.NgayDang)
                .Take(400)
                .Include(t => t.NguoiBan)
                .Include(t => t.AnhTinDangs)
                .Include(t => t.TinhThanh)
                .Include(t => t.QuanHuyen)
                .ToListAsync();

            // -----------------------------------------------------------------
            // BƯỚC 3: CHUẨN BỊ DỮ LIỆU TƯƠNG TÁC (TÁCH BIỆT - KHÔNG GỘP)
            // -----------------------------------------------------------------
            var foundIds = candidates.Select(c => c.MaTinDang).ToList();
            var now = DateTime.UtcNow;

            // Load Dictionary (O(1) lookup)
            var likeCounts = await _context.VideoLikes.Where(x => foundIds.Contains(x.MaTinDang))
                .GroupBy(x => x.MaTinDang).ToDictionaryAsync(k => k.Key, v => v.Count());

            var shareCounts = await _context.Shares.Where(x => foundIds.Contains(x.TinDangId.Value))
                .GroupBy(x => x.TinDangId.Value).ToDictionaryAsync(k => k.Key, v => v.Count());

            var commentCounts = await _context.VideoComments.Where(x => foundIds.Contains(x.MaTinDang))
                .GroupBy(x => x.MaTinDang).ToDictionaryAsync(k => k.Key, v => v.Count());

            // 🔥 TÁCH RIÊNG: VideoSave (Lưu để xem lại) vs Favorite (Quan tâm mua)
            var videoSaveCounts = await _context.VideoTinDangSaves.Where(x => foundIds.Contains(x.MaTinDang))
                .GroupBy(x => x.MaTinDang).ToDictionaryAsync(k => k.Key, v => v.Count());

            var favPostCounts = await _context.TinDangYeuThichs.Where(x => foundIds.Contains(x.MaTinDang))
                .GroupBy(x => x.MaTinDang).ToDictionaryAsync(k => k.Key, v => v.Count());

            // Lấy trạng thái của User hiện tại
            var userLikedIds = new HashSet<int>();
            var userVideoSavedIds = new HashSet<int>();   // User đã lưu video này chưa?
            var userPostFavoriteIds = new HashSet<int>(); // User đã thả tim sản phẩm này chưa?

            if (userId != null)
            {
                var likes = await _context.VideoLikes.Where(l => l.UserId == userId && foundIds.Contains(l.MaTinDang))
                    .Select(l => l.MaTinDang).ToListAsync();
                userLikedIds = new HashSet<int>(likes);

                var videoSaves = await _context.VideoTinDangSaves.Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                    .Select(s => s.MaTinDang).ToListAsync();
                userVideoSavedIds = new HashSet<int>(videoSaves);

                var postFavs = await _context.TinDangYeuThichs.Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                    .Select(s => s.MaTinDang).ToListAsync();
                userPostFavoriteIds = new HashSet<int>(postFavs);
            }

            // -----------------------------------------------------------------
            // BƯỚC 4: 🔥 THUẬT TOÁN SCORING (CẬP NHẬT TRỌNG SỐ MỚI)
            // -----------------------------------------------------------------
            var sortedResults = candidates.Select(td =>
            {
                double score = 0;

                // --- A. ĐIỂM CÁ NHÂN HÓA (PERSONALIZATION) ---
                if (userId != null && userProfile.HasData)
                {
                    // 1. AI Prediction
                    float aiPrediction = _aiEngine.PredictScore(userId, td.MaTinDang);
                    score += (aiPrediction * 5.0);

                    // 2. Search Keyword Match (Quan trọng nhất)
                    foreach (var kw in userProfile.RecentSearchKeywords)
                    {
                        if (td.TieuDe.ToLower().Contains(kw))
                        {
                            score += 40.0;
                            break;
                        }
                    }

                    // 3. Price Affinity
                    if (userProfile.PreferredMaxPrice > 0)
                    {
                        if (td.Gia >= userProfile.PreferredMinPrice && td.Gia <= userProfile.PreferredMaxPrice)
                        {
                            score += 15.0;
                        }
                        else if (td.Gia > userProfile.PreferredMaxPrice * 2)
                        {
                            score -= 5.0;
                        }
                    }

                    // 4. Location Context
                    if (userProfile.PreferredLocationId.HasValue && td.MaTinhThanh == userProfile.PreferredLocationId.Value)
                    {
                        score += 10.0;
                    }

                    // 5. Category Context
                    if (userProfile.PreferredCategoryIds.Contains(td.MaDanhMuc))
                    {
                        score += 10.0;
                    }
                }

                // --- B. ĐIỂM VIRAL (POPULARITY) ---
                int likes = likeCounts.GetValueOrDefault(td.MaTinDang, 0);
                int shares = shareCounts.GetValueOrDefault(td.MaTinDang, 0);
                int comments = commentCounts.GetValueOrDefault(td.MaTinDang, 0);

                // 🔥 Lấy số liệu riêng biệt
                int saveVideos = videoSaveCounts.GetValueOrDefault(td.MaTinDang, 0);
                int favPosts = favPostCounts.GetValueOrDefault(td.MaTinDang, 0);

                score += (likes * 1.0);
                score += (comments * 2.0);
                score += (shares * 4.0);

                // 🔥 Trọng số riêng: Yêu thích sản phẩm (FavPosts) quan trọng hơn Lưu video (SaveVideos)
                score += (saveVideos * 3.0);  // Điểm Interest (Quan tâm nội dung)
                score += (favPosts * 5.0);    // Điểm Purchase Intent (Ý định mua) -> Cao nhất

                // --- C. ĐIỂM THỜI GIAN (FRESHNESS) ---
                double hoursOld = (now - td.NgayDang).TotalHours;
                if (hoursOld < 12) score += 20.0;
                else if (hoursOld < 24) score += 10.0;
                else if (hoursOld < 72) score += 5.0;

                if (hoursOld > 720) score -= 5.0;

                // --- D. NGẪU NHIÊN HÓA (EXPLORATION) ---
                score += (new Random().NextDouble() * 3.0);

                return new
                {
                    Data = td,
                    Score = score,
                    Interactions = new
                    {
                        Likes = likes,
                        Shares = shares,
                        Comments = comments,
                        SaveVideos = saveVideos, // Số lượng lưu video
                        FavPosts = favPosts      // Số lượng yêu thích tin
                    }
                };
            })
            .OrderByDescending(x => x.Score) // Sắp xếp điểm cao nhất lên đầu
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

            // -----------------------------------------------------------------
            // BƯỚC 5: MAPPING DTO TRẢ VỀ (CÓ PHÂN BIỆT SAVE/FAVORITE)
            // -----------------------------------------------------------------
            var resultDTO = sortedResults.Select(item => new
            {
                item.Data.MaTinDang,
                item.Data.TieuDe,
                item.Data.MoTa,
                item.Data.VideoUrl,
                HinhAnh = item.Data.AnhTinDangs != null && item.Data.AnhTinDangs.Any()
                        ? item.Data.AnhTinDangs.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan
                        : null,
                item.Data.Gia,
                item.Data.DiaChi,
                TinhThanh = item.Data.TinhThanh?.TenTinhThanh,
                QuanHuyen = item.Data.QuanHuyen?.TenQuanHuyen,
                item.Data.TinhTrang,
                item.Data.NgayDang,
                ThoiGianHienThi = CalculateTimeAgo(item.Data.NgayDang),
                AnhCount = item.Data.AnhTinDangs?.Count(a => a.LoaiMedia == MediaType.Image) ?? 0,
                AnhUrls = item.Data.AnhTinDangs?.Where(a => a.LoaiMedia == MediaType.Image)
                                                .Select(a => a.DuongDan).ToList() ?? new List<string>(),

                // Số liệu tương tác hiển thị UI
                SoTym = item.Interactions.Likes,
                SoLuotChiaSe = item.Interactions.Shares,
                SoBinhLuan = item.Interactions.Comments,

                // 🔥 Hiển thị riêng biệt
                SoNguoiLuu = item.Interactions.SaveVideos, // Hiển thị ở icon Bookmark
                SoLuotYeuThich = item.Interactions.FavPosts,    // Hiển thị ở icon Trái tim/Giỏ hàng

                item.Data.SoLuotXem,
                TongScore = Math.Round(item.Score, 2),

                NguoiDang = item.Data.NguoiBan != null ? new
                {
                    item.Data.NguoiBan.Id,
                    item.Data.NguoiBan.FullName,
                    item.Data.NguoiBan.AvatarUrl
                } : null,

                IsLiked = userLikedIds.Contains(item.Data.MaTinDang),

                // 🔥 Trạng thái riêng biệt cho UI tô màu nút
                IsSaved = userVideoSavedIds.Contains(item.Data.MaTinDang),       // User đã lưu video?
                IsFavorited = userPostFavoriteIds.Contains(item.Data.MaTinDang)  // User đã thích tin?
            });

            return Ok(resultDTO);
        }




        [AllowAnonymous]
        [HttpGet("{maTinDang}")]
        public async Task<IActionResult> GetVideoDetail(int maTinDang)
        {
            // 1. Lấy thông tin tin đăng kèm các quan hệ
            var tin = await _context.TinDangs
                .Include(td => td.NguoiBan)
                .Include(td => td.TinhThanh)
                .Include(td => td.QuanHuyen)
                .Include(td => td.AnhTinDangs) // Quan trọng để lấy thumbnail/list ảnh
                .FirstOrDefaultAsync(td => td.MaTinDang == maTinDang && td.VideoUrl != null);

            if (tin == null)
                return NotFound();

            // 2. Khởi tạo trạng thái mặc định
            bool isLiked = false;
            bool isSaved = false;     // Trạng thái Lưu Video (Bookmark)
            bool isFavorited = false; // Trạng thái Yêu thích Sản phẩm (Heart/Cart)

            // 3. Kiểm tra trạng thái nếu User đã đăng nhập
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(userId))
                {
                    isLiked = await _context.VideoLikes
                        .AnyAsync(v => v.MaTinDang == maTinDang && v.UserId == userId);

                    // 🔥 Check Lưu Video (VideoTinDangSave)
                    isSaved = await _context.VideoTinDangSaves
                        .AnyAsync(s => s.MaTinDang == maTinDang && s.MaNguoiDung == userId);

                    // 🔥 Check Yêu Thích Tin (TinDangYeuThich)
                    isFavorited = await _context.TinDangYeuThichs
                        .AnyAsync(f => f.MaTinDang == maTinDang && f.MaNguoiDung == userId);
                }
            }

            // 4. Đếm số lượng tương tác (Thống kê)
            var soLuotChiaSe = await _context.Shares.CountAsync(s => s.TinDangId == maTinDang);

            // 🔥 Đếm riêng biệt 2 loại lưu
            var soNguoiLuuVideo = await _context.VideoTinDangSaves.CountAsync(s => s.MaTinDang == maTinDang);
            var soLuotYeuThich = await _context.TinDangYeuThichs.CountAsync(s => s.MaTinDang == maTinDang);

            var soTym = await _context.VideoLikes.CountAsync(v => v.MaTinDang == tin.MaTinDang);
            var soBinhLuan = await _context.VideoComments.CountAsync(c => c.MaTinDang == tin.MaTinDang);

            // 5. Tổng hợp kết quả trả về
            var result = new
            {
                tin.MaTinDang,
                tin.TieuDe,
                tin.MoTa,
                tin.VideoUrl,

                // Thumbnail để hiển thị khi video chưa play
                HinhAnh = tin.AnhTinDangs != null && tin.AnhTinDangs.Any()
                        ? tin.AnhTinDangs.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan
                        : null,

                tin.Gia,
                DiaChi = tin.DiaChi,
                TinhThanh = tin.TinhThanh?.TenTinhThanh,
                QuanHuyen = tin.QuanHuyen?.TenQuanHuyen,
                tin.TinhTrang,
                tin.NgayDang,

                // --- Thống kê ---
                SoTym = soTym,
                SoBinhLuan = soBinhLuan,
                SoLuotChiaSe = soLuotChiaSe,

                // 🔥 TRẢ VỀ 2 SỐ LIỆU RIÊNG (Thay vì gộp chung)
                SoNguoiLuu = soNguoiLuuVideo,
                SoLuotYeuThich = soLuotYeuThich,

                tin.SoLuotXem,
                AnhCount = tin.AnhTinDangs?.Count(a => a.LoaiMedia == MediaType.Image) ?? 0,

                // --- Trạng thái User ---
                IsLiked = isLiked,
                IsSaved = isSaved,         // Trạng thái Lưu Video
                IsFavorited = isFavorited, // Trạng thái Yêu thích Tin

                // --- Thông tin người đăng ---
                NguoiDang = tin.NguoiBan != null ? new
                {
                    tin.NguoiBan.Id,
                    tin.NguoiBan.FullName,
                    tin.NguoiBan.AvatarUrl
                } : null,

                // --- Danh sách bình luận (Lazy load list comment) ---
                BinhLuans = await _context.VideoComments
                    .Where(c => c.MaTinDang == tin.MaTinDang)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => new
                    {
                        c.Content,
                        c.CreatedAt,
                        NguoiDung = new
                        {
                            c.User.Id,
                            c.User.FullName,
                            c.User.AvatarUrl
                        }
                    }).ToListAsync()
            };

            return Ok(result);
        }
        // ham lay video video da tym
        [HttpGet("liked")]
        [Authorize]
        public async Task<IActionResult> GetLikedVideos([FromQuery] string? userId)
        {
            // 1. Lấy thông tin User hiện tại
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Unauthorized();

            var targetId = string.IsNullOrEmpty(userId) ? currentUser.Id : userId;

            // =========================================================================
            // 🔥 GIỮ NGUYÊN LOGIC RIÊNG TƯ CŨ CỦA BẠN
            // =========================================================================
            if (targetId != currentUser.Id)
            {
                var targetUser = await _userManager.FindByIdAsync(targetId);
                if (targetUser == null) return NotFound(new { message = "Không tìm thấy người dùng." });

                // Nếu riêng tư -> Check Follow
                if (targetUser.IsPrivateAccount)
                {
                    // Kiểm tra xem "currentUser" (người xem) có đang follow "targetUser" (chủ profile) không
                    var isAcceptedFollower = await _context.Follows
                        .AnyAsync(f => f.FollowerId == currentUser.Id
                                    && f.FollowingId == targetId
                                    && f.Status == FollowStatus.Accepted);

                    // Nếu KHÔNG follow -> Chặn -> Trả về danh sách rỗng
                    if (!isAcceptedFollower)
                    {
                        return Ok(new List<object>());
                    }
                }
            }
            // =========================================================================

            // 2. Bước 1: Lấy dữ liệu từ SQL (BỎ Views ra khỏi query này)
            var likedVideosRaw = await _context.VideoLikes
                .AsNoTracking()
                .Where(v => v.UserId == targetId)
                .OrderByDescending(v => v.CreatedAt)
                .Select(v => new
                {
                    v.MaTinDang,
                    v.TinDang.TieuDe,
                    v.TinDang.VideoUrl,
                    // Giữ logic lấy ảnh bìa đầu tiên
                    AnhBia = v.TinDang.AnhTinDangs.OrderBy(a => a.Order).Select(a => a.DuongDan).FirstOrDefault(),
                    v.TinDang.Gia,

                    // Logic ghép chuỗi địa chỉ cũ của bạn
                    DiaChi = (v.TinDang.QuanHuyen != null ? v.TinDang.QuanHuyen.TenQuanHuyen : "") +
                             (v.TinDang.QuanHuyen != null && v.TinDang.TinhThanh != null ? ", " : "") +
                             (v.TinDang.TinhThanh != null ? v.TinDang.TinhThanh.TenTinhThanh : ""),

                    // Các chỉ số đếm từ SQL
                    SoTym = _context.VideoLikes.Count(x => x.MaTinDang == v.MaTinDang),
                    SoBinhLuan = _context.VideoComments.Count(x => x.MaTinDang == v.MaTinDang),

                    NguoiDang = new
                    {
                        v.TinDang.NguoiBan.Id,
                        v.TinDang.NguoiBan.FullName,
                        v.TinDang.NguoiBan.AvatarUrl
                    }
                })
                .ToListAsync();

            // 3. Bước 2: Lấy Views từ MongoDB và gộp kết quả
            var resultList = new List<object>();

            foreach (var video in likedVideosRaw)
            {
                // Query MongoDB để đếm Views
                var filter = Builders<VideoViewLog>.Filter.Eq(x => x.MaTinDang, video.MaTinDang);
                var viewCount = await _viewLogCollection.CountDocumentsAsync(filter);

                resultList.Add(new
                {
                    video.MaTinDang,
                    video.TieuDe,
                    video.VideoUrl,
                    video.AnhBia,
                    Views = viewCount, // <--- Số view lấy từ Mongo được gán vào đây
                    video.Gia,
                    video.DiaChi,
                    video.SoTym,
                    video.SoBinhLuan,
                    video.NguoiDang,
                    CurrentUser = new
                    {
                        currentUser.Id,
                        currentUser.FullName,
                        currentUser.AvatarUrl
                    }
                });
            }

            return Ok(resultList);
        }

        [Authorize]
        [HttpPost("{maTinDang}/like")]
        public async Task<IActionResult> LikeOrUnlikeVideo(int maTinDang)
        {
            // 1. Lấy User ID hiện tại
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized("Người dùng chưa đăng nhập.");

            // [TỐI ƯU] Lấy thông tin video NGAY TỪ ĐẦU
            var video = await _context.TinDangs
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.MaTinDang == maTinDang);

            if (video == null)
                return NotFound("Video không tồn tại.");

            // 2. Kiểm tra xem đã like chưa
            var existing = await _context.VideoLikes
                .FirstOrDefaultAsync(x => x.MaTinDang == maTinDang && x.UserId == userId);

            bool isLiked;

            if (existing != null)
            {
                // --- TRƯỜNG HỢP 1: ĐÃ LIKE -> BỎ LIKE (UNLIKE) ---
                _context.VideoLikes.Remove(existing);
                isLiked = false;

                // ========================================================================
                // [MỚI] XÓA THÔNG BÁO CŨ KHI UNLIKE (CHỐNG RÁC DATA)
                // ========================================================================
                try
                {
                    // SỬA LỖI TẠI ĐÂY: Đổi n.RefId -> n.ReferenceId
                    var oldNoti = await _context.UserNotifications
                        .FirstOrDefaultAsync(n => n.Type == NotificationType.Like
                                             && n.SenderId == userId
                                             && n.ReferenceId == maTinDang); // <-- Đã sửa

                    if (oldNoti != null)
                    {
                        _context.UserNotifications.Remove(oldNoti);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi xóa thông báo like: {ex.Message}");
                }
            }
            else
            {
                // --- TRƯỜNG HỢP 2: CHƯA LIKE -> THÊM LIKE (LIKE) ---
                _context.VideoLikes.Add(new VideoLike
                {
                    MaTinDang = maTinDang,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
                isLiked = true;

                // ========================================================================
                // TÍCH HỢP CODE 2: GỬI THÔNG BÁO (NOTIFICATION)
                // ========================================================================
                if (video.MaNguoiBan != userId)
                {
                    try
                    {
                        // [MỚI] CHỐNG SPAM: Kiểm tra và xóa thông báo trùng
                        // SỬA LỖI TẠI ĐÂY: Đổi n.RefId -> n.ReferenceId
                        var duplicateNoti = await _context.UserNotifications
                            .FirstOrDefaultAsync(n => n.Type == NotificationType.Like
                                                 && n.SenderId == userId
                                                 && n.ReferenceId == maTinDang); // <-- Đã sửa

                        if (duplicateNoti != null)
                        {
                            _context.UserNotifications.Remove(duplicateNoti);
                            await _context.SaveChangesAsync();
                        }

                        // Tạo thông báo mới
                        await _notiService.CreateNotification(
                            senderId: userId,
                            receiverId: video.MaNguoiBan,
                            type: NotificationType.Like,
                            refId: maTinDang,
                            content: "đã thích video của bạn"
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Lỗi gửi thông báo: {ex.Message}");
                    }
                }
            }

            // 3. Lưu thay đổi vào Database
            await _context.SaveChangesAsync();

            // 4. Đếm lại tổng số tym
            var soTym = await _context.VideoLikes.CountAsync(x => x.MaTinDang == maTinDang);

            // 5. Gửi Realtime (nếu có Hub)
            if (_hubContext != null)
            {
                await _hubContext.Clients.Group(maTinDang.ToString()).SendAsync("UpdateLikeCount", maTinDang, soTym);
            }

            return Ok(new
            {
                isLiked,
                soTym
            });
        }

        [Authorize]
        [HttpPost("{maTinDang}/comment")]
        public async Task<IActionResult> CommentVideo(int maTinDang, [FromBody] CreateVideoCommentDto model)
        {
            // 1. Validate đầu vào
            if (string.IsNullOrWhiteSpace(model.Content))
                return BadRequest("Nội dung bình luận không được để trống.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // 2. Validate Video & Lấy thông tin chủ Video
            var videoOwnerId = await _context.TinDangs
                .AsNoTracking()
                .Where(td => td.MaTinDang == maTinDang)
                .Select(td => td.MaNguoiBan)
                .FirstOrDefaultAsync();

            if (videoOwnerId == null)
                return NotFound("Tin đăng không tồn tại.");

            // 3. Validate Comment cha & Chuẩn bị biến lưu người được reply
            string? replyToUserId = null; // ID của người mà mình đang trả lời

            if (model.ParentCommentId.HasValue)
            {
                // Lấy MaTinDang và UserId của người đã viết comment cha
                var parentInfo = await _context.VideoComments
                    .AsNoTracking()
                    .Where(c => c.Id == model.ParentCommentId.Value)
                    .Select(c => new { c.MaTinDang, c.UserId }) // Lấy thêm UserId của cha
                    .FirstOrDefaultAsync();

                if (parentInfo == null)
                    return NotFound("Bình luận cha không tồn tại.");

                if (parentInfo.MaTinDang != maTinDang)
                    return BadRequest("Bình luận cha không thuộc về tin đăng này.");

                replyToUserId = parentInfo.UserId; // Lưu lại ID người được reply
            }

            // 4. Lưu Comment vào DB
            var comment = new VideoComment
            {
                MaTinDang = maTinDang,
                UserId = userId,
                Content = model.Content,
                CreatedAt = DateTime.UtcNow,
                ParentCommentId = model.ParentCommentId
            };

            _context.VideoComments.Add(comment);
            await _context.SaveChangesAsync();
            // 🔥 LƯU Ý: Phải SaveChangesAsync xong thì comment mới có ID để truyền vào thông báo

            // ========================================================================
            // [LOGIC THÔNG BÁO - ĐÃ CẬP NHẬT ENTITY ID]
            // ========================================================================
            try
            {
                // Cắt nội dung ngắn gọn cho thông báo
                string shortContent = model.Content.Length > 30
                    ? model.Content.Substring(0, 30) + "..."
                    : model.Content;

                // --- TRƯỜNG HỢP 1: Đây là Reply (Trả lời bình luận) ---
                if (replyToUserId != null)
                {
                    // Gửi thông báo cho người được trả lời (trừ khi tự trả lời chính mình)
                    if (replyToUserId != userId)
                    {
                        await _notiService.CreateNotification(
                            senderId: userId,
                            receiverId: replyToUserId,    // Gửi cho người viết comment cha
                            type: NotificationType.Reply,
                            refId: maTinDang,             // ID Video (để load trang)
                            content: $"đã trả lời bình luận của bạn: {shortContent}",
                            entityId: comment.Id          // 🔥 QUAN TRỌNG: ID Comment vừa tạo (để scroll tới)
                        );
                    }

                    // (Tùy chọn) Gửi cho chủ video nếu chủ video không phải là người đang comment và không phải người được reply
                    if (videoOwnerId != userId && videoOwnerId != replyToUserId)
                    {
                        await _notiService.CreateNotification(
                            senderId: userId,
                            receiverId: videoOwnerId,
                            type: NotificationType.Comment,
                            refId: maTinDang,
                            content: $"đã bình luận trong video của bạn: {shortContent}",
                            entityId: comment.Id          // 🔥 QUAN TRỌNG: ID Comment vừa tạo
                        );
                    }
                }
                // --- TRƯỜNG HỢP 2: Comment thường (Cấp 1) ---
                else
                {
                    // Chỉ gửi cho chủ video (nếu không phải tự comment video mình)
                    if (videoOwnerId != userId)
                    {
                        await _notiService.CreateNotification(
                            senderId: userId,
                            receiverId: videoOwnerId,
                            type: NotificationType.Comment,
                            refId: maTinDang,
                            content: $"đã bình luận: {shortContent}",
                            entityId: comment.Id          // 🔥 QUAN TRỌNG: ID Comment vừa tạo
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi gửi thông báo comment: {ex.Message}");
            }
            // ========================================================================

            // 5. Gửi Realtime cập nhật số lượng Comment (SignalR)
            var totalComments = await _context.VideoComments.CountAsync(c => c.MaTinDang == maTinDang);

            if (_hubContext != null)
            {
                await _hubContext.Clients.Group(maTinDang.ToString())
                    .SendAsync("UpdateCommentCount", maTinDang, totalComments);
            }

            // 6. Gửi Realtime nội dung Comment mới (SignalR)
            var userInfo = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.FullName, u.UserName, u.AvatarUrl })
                .FirstOrDefaultAsync();

            var newCommentDto = new VideoCommentDto
            {
                Id = comment.Id,
                Content = comment.Content,
                CreatedAt = comment.CreatedAt,
                UserId = comment.UserId,
                UserName = userInfo?.FullName ?? userInfo?.UserName ?? "Unknown User",
                AvatarUrl = userInfo?.AvatarUrl,
                Replies = new List<VideoCommentDto>(),
                TimeAgo = "Vừa xong"
            };

            if (_hubContext != null)
            {
                await _hubContext.Clients.Group(maTinDang.ToString())
                    .SendAsync("ReceiveComment", newCommentDto, comment.ParentCommentId);
            }

            return Ok(newCommentDto);
        }

        [HttpGet("{maTinDang}/comments")]
        [AllowAnonymous]
        public async Task<IActionResult> GetComments(int maTinDang)
        {
            var rawComments = await _context.VideoComments
                .AsNoTracking()
                .Where(c => c.MaTinDang == maTinDang)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new VideoCommentDto
                {
                    Id = c.Id,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt,
                    UserId = c.UserId,
                    UserName = c.User.FullName ?? c.User.UserName,
                    AvatarUrl = c.User.AvatarUrl,
                    ParentCommentId = c.ParentCommentId,
                })
                .ToListAsync();

            // ✅ VÒNG LẶP TÍNH TOÁN SAU KHI LẤY DỮ LIỆU TỪ DB
            foreach (var item in rawComments)
            {
                item.TimeAgo = CalculateTimeAgo(item.CreatedAt);
            }

            var commentTree = BuildCommentTree(rawComments);
            return Ok(commentTree);
        }

        // ✅ Hàm dựng cây sửa lại để làm việc với DTO (nhẹ hơn nhiều)
        private List<VideoCommentDto> BuildCommentTree(List<VideoCommentDto> allComments, int? parentId = null)
        {
            return allComments
                .Where(c => c.ParentCommentId == parentId)
                .Select(c => {
                    c.Replies = BuildCommentTree(allComments, c.Id); // Đệ quy
                    return c;
                })
                .ToList();
        }
        [Authorize]
        [HttpDelete("comment/{commentId}")]
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Load comment kèm replies (bình luận con)
            var comment = await _context.VideoComments
                .Include(c => c.Replies)
                .FirstOrDefaultAsync(c => c.Id == commentId);

            if (comment == null)
                return NotFound("Bình luận không tồn tại.");

            // Kiểm tra quyền xóa: chỉ người tạo mới được xóa
            if (comment.UserId != userId)
                return Forbid("Bạn không có quyền xóa bình luận này.");

            // ✅ Lấy MaTinDang TRƯỚC KHI XÓA (để dùng cho realtime)
            var maTinDang = comment.MaTinDang.ToString();

            // Xóa đệ quy tất cả bình luận con
            await DeleteCommentRecursive(comment);

            // Lưu thay đổi vào DB
            await _context.SaveChangesAsync();

            // ✅ --- THÊM ĐOẠN CODE 2 Ở ĐÂY ---
            // 1. Đếm lại tổng số bình luận sau khi xóa (bao gồm trường hợp xóa cả cha lẫn con)
            var totalComments = await _context.VideoComments
                .CountAsync(c => c.MaTinDang == int.Parse(maTinDang));

            // 2. Gửi realtime cập nhật số lượng bình luận cho tất cả người xem video
            await _hubContext.Clients.Group(maTinDang)
                .SendAsync("UpdateCommentCount", int.Parse(maTinDang), totalComments);
            // ✅ --- HẾT ĐOẠN MỚI ---

            // ✅ Gửi ID comment bị xóa cho mọi người (phần cũ, vẫn giữ nguyên)
            await _hubContext.Clients.Group(maTinDang)
                             .SendAsync("CommentDeleted", commentId);

            return Ok(new { message = "Đã xóa bình luận thành công." });
        }

        // Hàm đệ quy xóa comment và toàn bộ reply của nó
        private async Task DeleteCommentRecursive(VideoComment comment)
        {
            // Dùng ToList() để tránh lỗi "Collection was modified"
            foreach (var reply in comment.Replies.ToList())
            {
                // Load replies của reply
                var replyWithChildren = await _context.VideoComments
                    .Include(r => r.Replies)
                    .FirstOrDefaultAsync(r => r.Id == reply.Id);

                if (replyWithChildren != null)
                {
                    await DeleteCommentRecursive(replyWithChildren);
                }
            }

            _context.VideoComments.Remove(comment);
        }

        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchVideos(
            [FromQuery] string keyword,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest(new { message = "Từ khóa tìm kiếm không được để trống." });

            keyword = keyword.Trim();

            string? userId = User.Identity?.IsAuthenticated == true
                ? (User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value)
                : null;
            string? sessionId = Request.Headers["X-Session-ID"].FirstOrDefault();

            try
            {
                // ==========================================================
                // 2. KHỞI TẠO CÁC TÁC VỤ PHỤ (DÙNG SCOPE ĐỂ TRÁNH LỖI THREAD)
                // ==========================================================

                // A. Task Log Search (Chạy ngầm an toàn)
                if (page == 1)
                {
                    _ = Task.Run(async () =>
                    {
                        // Tạo scope mới -> DbContext mới -> Không xung đột
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
                            await searchService.LogSearchAsync(keyword, userId, sessionId, 0);
                        }
                    });
                }

                // B. Task Gợi ý khoảng giá (Chạy song song)
                var priceTask = Task.Run(async () =>
                {
                    if (page != 1) return null;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        // Lấy Service từ scope mới -> Nó sẽ có DbContext riêng
                        var priceService = scope.ServiceProvider.GetRequiredService<PriceAnalysisService>();
                        return await priceService.GetPriceSuggestionByKeywordAsync(keyword);
                    }
                });

                // C. Task Gợi ý từ khóa liên quan (Chạy song song)
                var relatedTask = Task.Run(async () =>
                {
                    if (page != 1) return new List<string>();
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
                        return await searchService.GetRelatedKeywordsAsync(keyword);
                    }
                });

                // ==========================================================
                // 3. TÌM KIẾM CHÍNH (Dùng _context của Controller - Main Thread)
                // ==========================================================
                var baseQuery = _context.TinDangs.AsNoTracking()
                    .Where(td => td.VideoUrl != null
                                 && td.TrangThai == TrangThaiTinDang.DaDuyet
                                 && td.TieuDe.Contains(keyword));

                var hotList = await baseQuery.OrderByDescending(td => td.SoLuotXem)
                    .Select(td => new CandidateDto
                    {
                        MaTinDang = td.MaTinDang,
                        NgayDang = td.NgayDang,
                        SoLuotXem = td.SoLuotXem
                    })
                    .Take(50).ToListAsync();

                var newList = await baseQuery.OrderByDescending(td => td.NgayDang)
                    .Select(td => new CandidateDto
                    {
                        MaTinDang = td.MaTinDang,
                        NgayDang = td.NgayDang,
                        SoLuotXem = td.SoLuotXem
                    })
                    .Take(50).ToListAsync();

                var candidates = hotList.Concat(newList)
                    .GroupBy(x => x.MaTinDang).Select(g => g.First()).ToList();

                int totalMatchCount = candidates.Count;

                // Nếu không có kết quả, vẫn phải đợi Task phụ xong để trả về gợi ý
                if (totalMatchCount == 0)
                {
                    var emptyRelated = await relatedTask;
                    return Ok(new
                    {
                        TotalItems = 0,
                        Items = new List<object>(),
                        Page = page,
                        RelatedKeywords = emptyRelated
                    });
                }

                // ==========================================================
                // 4. SCORING & RANKING (AI)
                // ==========================================================
                var rankedIds = new List<(int Id, double Score)>();
                var random = new Random();

                foreach (var item in candidates)
                {
                    double score = 0;
                    score += Math.Log(item.SoLuotXem + 1) * 0.5;

                    double daysOld = (DateTime.UtcNow - item.NgayDang).TotalDays;
                    if (daysOld < 3) score += 3.0;
                    else if (daysOld < 7) score += 1.0;

                    if (!string.IsNullOrEmpty(userId))
                    {
                        float aiScore = _aiEngine.PredictScore(userId, item.MaTinDang);
                        score += (aiScore * 6.0);
                    }
                    score += (random.NextDouble() * 8.0);
                    rankedIds.Add((item.MaTinDang, score));
                }

                // ==========================================================
                // 5. PHÂN TRANG & FETCH DATA
                // ==========================================================
                var pagedIds = rankedIds
                    .OrderByDescending(x => x.Score)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => x.Id)
                    .ToList();

                if (!pagedIds.Any())
                    return Ok(new { TotalItems = totalMatchCount, Items = new List<object>(), Page = page });

                var finalVideos = await _context.TinDangs.AsNoTracking()
                    .Where(td => pagedIds.Contains(td.MaTinDang))
                    .Include(td => td.NguoiBan)
                    .Include(td => td.TinhThanh)
                    .Include(td => td.QuanHuyen)
                    .ToListAsync();

                finalVideos = finalVideos.OrderBy(v => pagedIds.IndexOf(v.MaTinDang)).ToList();

                // ==========================================================
                // 6. THỐNG KÊ TIM & COMMENT
                // ==========================================================
                var listMaTin = finalVideos.Select(x => x.MaTinDang).ToList();

                var commentCounts = await _context.VideoComments.AsNoTracking()
                    .Where(c => listMaTin.Contains(c.MaTinDang))
                    .GroupBy(c => c.MaTinDang)
                    .Select(g => new { MaTin = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.MaTin, x => x.Count);

                var likeCounts = await _context.VideoLikes.AsNoTracking()
                    .Where(l => listMaTin.Contains(l.MaTinDang))
                    .GroupBy(l => l.MaTinDang)
                    .Select(g => new { MaTin = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.MaTin, x => x.Count);

                var userLikedIds = new HashSet<int>();
                if (userId != null)
                {
                    userLikedIds = (await _context.VideoLikes.AsNoTracking()
                        .Where(l => listMaTin.Contains(l.MaTinDang) && l.UserId == userId)
                        .Select(l => l.MaTinDang)
                        .ToListAsync()).ToHashSet();
                }

                // ==========================================================
                // 7. MAP DTO & TRẢ VỀ
                // ==========================================================
                var resultItems = finalVideos.Select(item => new VideoSearchResultDto
                {
                    MaTinDang = item.MaTinDang,
                    TieuDe = item.TieuDe,
                    VideoUrl = item.VideoUrl,
                    Gia = item.Gia,
                    DiaChi = item.DiaChi,
                    TinhThanh = item.TinhThanh?.TenTinhThanh ?? "",
                    QuanHuyen = item.QuanHuyen?.TenQuanHuyen ?? "",
                    SoTym = likeCounts.GetValueOrDefault(item.MaTinDang, 0),
                    SoBinhLuan = commentCounts.GetValueOrDefault(item.MaTinDang, 0),
                    NguoiDang = item.NguoiBan == null ? null : new UserSummaryDto
                    {
                        Id = item.NguoiBan.Id,
                        FullName = item.NguoiBan.FullName,
                        AvatarUrl = item.NguoiBan.AvatarUrl
                    },
                    IsLiked = userLikedIds.Contains(item.MaTinDang),
                    ThoiGianHienThi = CalculateTimeAgo(item.NgayDang)
                }).ToList();

                // Đợi các Task phụ hoàn thành và lấy kết quả
                await Task.WhenAll(priceTask, relatedTask);

                return Ok(new
                {
                    TotalItems = totalMatchCount,
                    TotalPages = (int)Math.Ceiling((double)totalMatchCount / pageSize),
                    Page = page,
                    PageSize = pageSize,
                    PriceSuggestion = await priceTask,    // Kết quả từ luồng phụ
                    RelatedKeywords = await relatedTask,  // Kết quả từ luồng phụ
                    Items = resultItems
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Search Error: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi hệ thống tìm kiếm: " + ex.Message });
            }
        }

        [HttpGet("suggest-smart")]
        [AllowAnonymous]
        public async Task<IActionResult> SuggestKeywordsSmart([FromQuery] string keyword)
        {
            var userId = User.Identity?.IsAuthenticated == true
                ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;

            var suggestions = await _searchService.GetSmartSuggestionsAsync(keyword, userId);
            return Ok(suggestions);
        }

        // ==========================================
        // API 3: TỪ KHÓA NỔI BẬT (TRENDING)
        // ==========================================
        [HttpGet("trending")]
        [AllowAnonymous] // Cho phép cả khách chưa đăng nhập cũng xem được trend
        public async Task<IActionResult> GetTrendingKeywords()
        {
            // 1. Lấy UserId từ Token hiện tại (nếu user đã đăng nhập)
            string? userId = null;

            // Kiểm tra xem request này có kèm Token hợp lệ không
            if (User.Identity?.IsAuthenticated == true)
            {
                // Lấy ID từ Claim (Thử lấy theo chuẩn NameIdentifier trước, nếu không có thì lấy theo key "id")
                userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("id")?.Value;
            }

            // 2. Truyền userId vào hàm
            // Nếu chưa đăng nhập -> userId = null -> Service sẽ trả về Trend cộng đồng (Global)
            // Nếu đã đăng nhập -> userId = "abc..." -> Service sẽ trộn thêm Trend cá nhân
            var trends = await _searchService.GetTrendingKeywordsAsync(userId);

            return Ok(trends);
        }

        [HttpGet("search-users")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchUsersByVideoKeyword([FromQuery] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Từ khóa tìm kiếm không được để trống.");

            keyword = keyword.ToLower();

            // Truy vấn người dùng đã đăng video có tiêu đề chứa keyword
            var users = await _context.TinDangs
                .Where(td =>
                    td.VideoUrl != null &&
                    td.TrangThai == TrangThaiTinDang.DaDuyet &&
                    EF.Functions.Like(td.TieuDe.ToLower(), $"%{keyword}%") &&
                    td.NguoiBan != null)
                .Select(td => new
                {
                    td.NguoiBan.Id,
                    td.NguoiBan.FullName,
                    td.NguoiBan.AvatarUrl
                })
                .Distinct()
                .ToListAsync();

            return Ok(users);
        }


        [HttpGet("search-users-smart")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchUsersSmart([FromQuery] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Từ khóa tìm kiếm không được để trống.");

            keyword = keyword.ToLower();

            // Lấy ID người dùng hiện tại (nếu đã đăng nhập)
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // --- BƯỚC 1: TRUY VẤN (QUERY) ---
            // Sử dụng AsNoTracking để tối ưu tốc độ đọc
            var query = _context.Users
                .AsNoTracking()
                .Where(u =>
                    // Tìm theo tên người dùng
                    u.FullName.ToLower().Contains(keyword) ||
                    // HOẶC Tìm theo người dùng có video chứa từ khóa
                    _context.TinDangs.Any(t => t.MaNguoiBan == u.Id &&
                                                t.VideoUrl != null &&
                                                t.TrangThai == TrangThaiTinDang.DaDuyet &&
                                                t.TieuDe.ToLower().Contains(keyword))
                )
                .Select(u => new
                {
                    User = u,

                    // 1. Đếm số Follower
                    FollowersCount = _context.Follows.Count(f => f.FollowingId == u.Id),

                    // 2. Đếm số Video đã duyệt
                    TotalVideos = _context.TinDangs.Count(t => t.MaNguoiBan == u.Id && t.TrangThai == TrangThaiTinDang.DaDuyet),

                    // 3. Đếm tổng lượt Tym (SỬA LỖI TẠI ĐÂY)
                    // Thay vì đi từ TinDang, ta đếm trực tiếp từ bảng VideoLikes dựa vào quan hệ ngược
                    TotalLikes = _context.VideoLikes
                                    .Count(vl => vl.TinDang.MaNguoiBan == u.Id),

                    // 4. Đếm tổng lượt Lưu (SỬA LỖI TẠI ĐÂY)
                    TotalSaves = _context.VideoTinDangSaves
                                    .Count(vs => vs.TinDang.MaNguoiBan == u.Id),

                    // 5. Tính tổng View
                    TotalViews = _context.TinDangs
                        .Where(t => t.MaNguoiBan == u.Id)
                        .Sum(t => (int?)t.SoLuotXem) ?? 0,

                    // 6. Check trạng thái Follow
                    IsFollowed = currentUserId != null &&
                                 _context.Follows.Any(f => f.FollowerId == currentUserId && f.FollowingId == u.Id)
                });

            // Lấy dữ liệu thô về Memory
            var rawData = await query.ToListAsync();

            // --- BƯỚC 2: TÍNH ĐIỂM & SẮP XẾP (RANKING ALGORITHM) ---
            var rankedUsers = rawData.Select(x => new UserSearchResultDto
            {
                Id = x.User.Id,
                FullName = x.User.FullName,
                AvatarUrl = x.User.AvatarUrl,
                PhoneNumber = x.User.PhoneNumber,

                FollowersCount = x.FollowersCount,
                TotalVideos = x.TotalVideos,
                TotalLikes = x.TotalLikes,
                TotalViews = x.TotalViews,
                TotalSaves = x.TotalSaves,
                IsFollowed = x.IsFollowed,

                // 🔥 CÔNG THỨC ĐỀ XUẤT (Ranking Formula)
                // - Ưu tiên người được Lưu nhiều (Interest cao)
                // - Ưu tiên người được Tym nhiều (Engagement cao)
                // - Thưởng điểm cho người chăm chỉ đăng video
                RankingScore = (x.TotalSaves * 3.0) +       // Hệ số 3
                               (x.TotalLikes * 2.0) +       // Hệ số 2
                               (x.FollowersCount * 1.5) +   // Hệ số 1.5
                               (x.TotalVideos * 5.0) +      // Thưởng 5đ mỗi video
                               (x.TotalViews * 0.01)        // View chỉ là phụ
            })
            .OrderByDescending(u => u.RankingScore) // Người điểm cao nhất lên đầu
            .ToList();

            return Ok(rankedUsers);
        }

        [AllowAnonymous]
        [HttpGet("search-users-sorted")]
        public async Task<IActionResult> SearchUsersByVideoKeywordSorted([FromQuery] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Từ khóa tìm kiếm không được để trống.");

            keyword = keyword.ToLower();

            // Lấy danh sách video thỏa điều kiện
            var videosQuery = _context.TinDangs
                .Where(td =>
                    !string.IsNullOrEmpty(td.VideoUrl) &&
                    td.TrangThai == TrangThaiTinDang.DaDuyet &&
                    !string.IsNullOrEmpty(td.TieuDe) &&
                    EF.Functions.Like(td.TieuDe.ToLower(), $"%{keyword}%") &&
                    td.NguoiBan != null);

            var videoInfos = await videosQuery
                .Select(td => new
                {
                    td.MaTinDang,
                    UserId = td.NguoiBan.Id
                })
                .ToListAsync();

            var maTinDangList = videoInfos.Select(v => v.MaTinDang).ToList();

            // Đếm lượt tym cho từng video
            var tymCounts = await _context.VideoLikes
                .Where(v => maTinDangList.Contains(v.MaTinDang))
                .GroupBy(v => v.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Đếm lượt bình luận cho từng video
            var binhLuanCounts = await _context.VideoComments
                .Where(c => maTinDangList.Contains(c.MaTinDang))
                .GroupBy(c => c.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Tính tổng lượt tym + bình luận cho mỗi người dùng
            var userScores = videoInfos
                .GroupBy(v => v.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    TotalScore = g.Sum(v => tymCounts.GetValueOrDefault(v.MaTinDang, 0) + binhLuanCounts.GetValueOrDefault(v.MaTinDang, 0))
                })
                .OrderByDescending(u => u.TotalScore)
                .ToList();

            var userIdsOrdered = userScores.Select(u => u.UserId).ToList();

            var users = await _context.Users
                .Where(u => userIdsOrdered.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.AvatarUrl
                })
                .ToListAsync();

            // Sắp xếp lại danh sách users theo thứ tự điểm đã tính
            var result = userIdsOrdered
                .Join(users, id => id, u => u.Id, (id, u) => u)
                .ToList();

            return Ok(result);
        }

        [HttpPost("ToggleSave")]
        [Authorize]
        public async Task<IActionResult> ToggleSaveVideo([FromBody] ToggleSaveRequest request)
        {
            var maTinDang = request.MaTinDang;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized("Bạn cần đăng nhập để lưu/bỏ lưu video.");

            // ✅ Kiểm tra tồn tại tin đăng (chỉ check chứ không load full object)
            bool tinDangExists = await _context.TinDangs
                .AnyAsync(t => t.MaTinDang == maTinDang);
            if (!tinDangExists)
                return NotFound("Tin đăng không tồn tại.");

            // ✅ Lấy dữ liệu lưu của user này + danh sách cùng lúc
            var saves = await _context.VideoTinDangSaves
                .Where(v => v.MaTinDang == maTinDang)
                .ToListAsync();

            var videoSave = saves.FirstOrDefault(v => v.MaNguoiDung == userId);

            bool saved;
            if (videoSave == null)
            {
                // Người dùng chưa lưu → thêm mới
                _context.VideoTinDangSaves.Add(new VideoTinDangSave
                {
                    MaTinDang = maTinDang,
                    MaNguoiDung = userId,
                    NgayLuu = DateTime.UtcNow
                });
                saved = true;
            }
            else
            {
                // Người dùng đã lưu → bỏ lưu
                _context.VideoTinDangSaves.Remove(videoSave);
                saved = false;
            }

            await _context.SaveChangesAsync();

            // ✅ Tính total ngay tại memory (không query DB lần 3)
            int totalSaves = saved ? saves.Count + 1 : saves.Count - 1;

            // ✅✅ THÊM REALTIME CẬP NHẬT TỚI CÁC CLIENT ĐANG XEM VIDEO NÀY
            // Chỉ gửi số lượng mới cho cả nhóm
            await _hubContext.Clients.Group(request.MaTinDang.ToString()).SendAsync("UpdateSaveCount", request.MaTinDang, totalSaves);

            // ✅ Trả kết quả về cho client hiện tại
            return Ok(new
            {
                saved,
                totalSaves
            });
        }



        [HttpGet("{maTinDang}/savedinfo")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSavedInfo(int maTinDang)
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);

            var result = await _context.VideoTinDangSaves
                .Where(v => v.MaTinDang == maTinDang)
                .GroupBy(v => 1)
                .Select(g => new
                {
                    soNguoiLuu = g.Count(),
                    isSaved = userId != null && g.Any(v => v.MaNguoiDung == userId)
                })
                .FirstOrDefaultAsync();

            return Ok(result ?? new { soNguoiLuu = 0, isSaved = false });
        }

        [HttpGet("saved")]
        [Authorize]
        public async Task<IActionResult> GetSavedVideos([FromQuery] string? userId)
        {
            try
            {
                // 1. Lấy user hiện tại
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null) return Unauthorized();

                // 2. Xác định targetId (Xem của mình hay của người khác)
                var targetId = string.IsNullOrEmpty(userId) ? currentUser.Id : userId;

                // 3. Lấy dữ liệu cơ bản từ SQL trước (Bỏ qua phần Views tại đây)
                // Lưu ý: Ta vẫn đếm SoNguoiLuu và SoBinhLuan bằng SQL vì bảng này nằm trong SQL
                var savedVideosRaw = await _context.VideoTinDangSaves
                    .Where(v => v.MaNguoiDung == targetId)
                    .OrderByDescending(v => v.NgayLuu)
                    .Select(v => new
                    {
                        v.MaTinDang,
                        v.TinDang.TieuDe,
                        v.TinDang.VideoUrl,
                        v.TinDang.Gia,
                        v.TinDang.DiaChi,
                        // Null check an toàn cho TinhThanh/QuanHuyen
                        TinhThanh = v.TinDang.TinhThanh != null ? v.TinDang.TinhThanh.TenTinhThanh : null,
                        QuanHuyen = v.TinDang.QuanHuyen != null ? v.TinDang.QuanHuyen.TenQuanHuyen : null,

                        // Đếm số liệu từ SQL
                        SoNguoiLuu = _context.VideoTinDangSaves.Count(x => x.MaTinDang == v.MaTinDang),
                        SoBinhLuan = _context.VideoComments.Count(x => x.MaTinDang == v.MaTinDang),

                        NguoiDang = new
                        {
                            v.TinDang.NguoiBan.Id,
                            v.TinDang.NguoiBan.FullName,
                            v.TinDang.NguoiBan.AvatarUrl
                        }
                    })
                    .ToListAsync();

                // 4. Kết hợp dữ liệu: Lấy Views từ MongoDB cho từng video
                // Tạo một list kết quả mới
                var resultList = new List<object>();

                foreach (var video in savedVideosRaw)
                {
                    // Query MongoDB để đếm Views cho video này
                    // Filter: MaTinDang trong Mongo phải khớp với MaTinDang từ SQL
                    var filter = Builders<VideoViewLog>.Filter.Eq(x => x.MaTinDang, video.MaTinDang);
                    var viewCount = await _viewLogCollection.CountDocumentsAsync(filter);

                    resultList.Add(new
                    {
                        video.MaTinDang,
                        video.TieuDe,
                        video.VideoUrl,
                        Views = viewCount, // Gán số view lấy từ Mongo
                        video.Gia,
                        video.DiaChi,
                        video.TinhThanh,
                        video.QuanHuyen,
                        video.SoNguoiLuu,
                        video.SoBinhLuan,
                        video.NguoiDang,
                        CurrentUser = new
                        {
                            currentUser.Id,
                            currentUser.FullName,
                            currentUser.AvatarUrl
                        }
                    });
                }

                return Ok(resultList);
            }
            catch (Exception ex)
            {
                // Log lỗi nếu cần thiết
                return StatusCode(500, new { message = "Lỗi server", error = ex.Message });
            }
        }

        // Thêm các API này vào VideoController

        [HttpGet("search-history")]
        [Authorize]
        public async Task<IActionResult> GetSearchHistory()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var history = await _context.SearchHistories
                .Where(sh => sh.UserId == userId)
                .OrderByDescending(sh => sh.CreatedAt)
                .Take(10) // Giới hạn 10 lịch sử gần nhất
                .Select(sh => new { sh.Keyword, sh.CreatedAt })
                .ToListAsync();

            return Ok(history);
        }

        [HttpPost("search-history")]
        [Authorize]
        public async Task<IActionResult> SaveSearchHistory([FromBody] SearchHistoryRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Keyword))
                return BadRequest("Từ khóa không được để trống.");

            // Kiểm tra xem từ khóa đã tồn tại chưa
            var existing = await _context.SearchHistories
                .FirstOrDefaultAsync(sh => sh.UserId == userId && sh.Keyword == request.Keyword);

            if (existing != null)
            {
                // Cập nhật thời gian tìm kiếm
                existing.CreatedAt = DateTime.UtcNow;
                _context.SearchHistories.Update(existing);
            }
            else
            {
                // Tạo mới
                var newHistory = new SearchHistory
                {
                    UserId = userId,
                    Keyword = request.Keyword,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SearchHistories.Add(newHistory);
            }

            // Giới hạn số lượng lịch sử tìm kiếm (chỉ giữ 10 cái gần nhất)
            var historyCount = await _context.SearchHistories
                .CountAsync(sh => sh.UserId == userId);

            if (historyCount >= 10)
            {
                var oldestHistories = await _context.SearchHistories
                    .Where(sh => sh.UserId == userId)
                    .OrderBy(sh => sh.CreatedAt)
                    .Take(historyCount - 9) // Xóa để chỉ còn 9, add thêm 1 thành 10
                    .ToListAsync();

                _context.SearchHistories.RemoveRange(oldestHistories);
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("search-history")]
        [Authorize]
        public async Task<IActionResult> DeleteSearchHistory([FromQuery] string keyword)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Từ khóa không được để trống.");

            var history = await _context.SearchHistories
                .FirstOrDefaultAsync(sh => sh.UserId == userId && sh.Keyword == keyword);

            if (history != null)
            {
                _context.SearchHistories.Remove(history);
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpDelete("search-history/clear")]
        [Authorize]
        public async Task<IActionResult> ClearAllSearchHistory()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var histories = await _context.SearchHistories
                .Where(sh => sh.UserId == userId)
                .ToListAsync();

            if (histories.Any())
            {
                _context.SearchHistories.RemoveRange(histories);
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        // DTO class
        public class SearchHistoryRequest
        {
            public string Keyword { get; set; } = null!;
        }
        [HttpGet("detail/{maTinDang}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetVideoDetailInfo(int maTinDang)
        {
            // 1. Logic cũ: Lấy thông tin cơ bản từ SQL Server
            var tin = await _context.TinDangs
                .Include(td => td.NguoiBan)
                .Include(td => td.TinhThanh)
                .Include(td => td.QuanHuyen)
                .Include(td => td.AnhTinDangs)
                .Include(td => td.DanhMuc)
                    .ThenInclude(dm => dm.DanhMucCha)
                .FirstOrDefaultAsync(td => td.MaTinDang == maTinDang);

            if (tin == null)
                return NotFound(new { message = "Tin đăng không tồn tại" });

            // 2. Logic cũ: Xử lý danh sách ảnh
            var danhSachAnh = tin.AnhTinDangs != null
                ? tin.AnhTinDangs
                    .Where(a => a.LoaiMedia == MediaType.Image)
                    .OrderBy(a => a.Order)
                    .Select(a => a.DuongDan)
                    .ToList()
                : new List<string>();

            // 3. LOGIC MỚI: Lấy thông tin chi tiết động từ MongoDB
            var tinChiTietMongo = await _tinDangDetailsCollection
                .Find(x => x.MaTinDang == maTinDang)
                .FirstOrDefaultAsync();

            // Chuyển đổi BsonDocument sang Dictionary để API trả về JSON chuẩn
            object thongSoKyThuat = null;
            if (tinChiTietMongo != null && tinChiTietMongo.ChiTiet != null)
            {
                // Sử dụng BsonTypeMapper để chuyển đổi các kiểu dữ liệu Mongo sang .NET native types
                thongSoKyThuat = BsonTypeMapper.MapToDotNetValue(tinChiTietMongo.ChiTiet);
            }

            // 4. Kết hợp kết quả
            var result = new
            {
                tin.MaTinDang,
                tin.TieuDe,
                tin.MoTa,
                tin.Gia,
                tin.CoTheThoaThuan,
                tin.TinhTrang,
                tin.DiaChi,
                NgayDang = tin.NgayDang.ToString("dd/MM/yyyy"),
                TinhThanh = tin.TinhThanh?.TenTinhThanh,
                QuanHuyen = tin.QuanHuyen?.TenQuanHuyen,

                DanhSachAnh = danhSachAnh,

                // Trường mới thêm vào từ MongoDB
                ThongSoChiTiet = thongSoKyThuat,

                NguoiDang = tin.NguoiBan != null ? new
                {
                    tin.NguoiBan.Id,
                    tin.NguoiBan.FullName,
                    tin.NguoiBan.AvatarUrl,
                    tin.NguoiBan.PhoneNumber
                } : null,

                DanhMuc = tin.DanhMuc != null ? new
                {
                    MaDanhMuc = tin.DanhMuc.MaDanhMuc,
                    TenDanhMuc = tin.DanhMuc.TenDanhMuc,
                    DanhMucCha = tin.DanhMuc.DanhMucCha != null ? new
                    {
                        MaDanhMucCha = tin.DanhMuc.DanhMucCha.MaDanhMucCha,
                        TenDanhMucCha = tin.DanhMuc.DanhMucCha.TenDanhMucCha,
                        tin.DanhMuc.DanhMucCha.Icon
                    } : null
                } : null
            };

            return Ok(result);
        }
        [HttpPost("track-view")]
        [AllowAnonymous]
        public async Task<IActionResult> TrackVideoView([FromBody] TrackViewRequest request)
        {
            try
            {
                // ✅ Giờ Việt Nam
                var now = DateTime.UtcNow.AddHours(7);

                var userId = User.Identity?.IsAuthenticated == true
                    ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                    : null;

                var userAgent = Request.Headers["User-Agent"].FirstOrDefault() ?? "";
                var deviceName = UserAgentHelper.GetDeviceName(userAgent);
                var ipAddress = Request.GetClientIp(); // Giả sử bạn có extension method này

                // 1️⃣ TÌM VIEW CŨ TRONG MONGODB
                var builder = Builders<VideoViewLog>.Filter;
                FilterDefinition<VideoViewLog> filter;

                if (!string.IsNullOrEmpty(userId))
                {
                    // Tìm theo UserId + MaTinDang
                    filter = builder.Eq(x => x.MaTinDang, request.MaTinDang) &
                             builder.Eq(x => x.UserId, userId);
                }
                else
                {
                    // Tìm theo IP + Device + MaTinDang (cho khách vãng lai)
                    filter = builder.Eq(x => x.MaTinDang, request.MaTinDang) &
                             builder.Eq(x => x.UserId, null) &
                             builder.Eq(x => x.IpAddress, ipAddress) &
                             builder.Eq(x => x.DeviceName, deviceName);
                }

                // Lấy view mới nhất (Sort giảm dần theo StartedAt)
                var lastView = await _viewLogCollection.Find(filter)
                    .SortByDescending(x => x.StartedAt)
                    .FirstOrDefaultAsync();

                // Logic 30 phút tính là view mới
                bool createNew = lastView == null || (now - lastView.StartedAt).TotalMinutes >= 30;

                if (createNew)
                {
                    // 2️⃣ TẠO VIEW MỚI (INSERT VÀO MONGO)
                    var newView = new VideoViewLog
                    {
                        MaTinDang = request.MaTinDang,
                        UserId = userId,
                        IpAddress = ipAddress,
                        DeviceName = deviceName,
                        StartedAt = now,
                        WatchedSeconds = request.WatchedSeconds,
                        IsCompleted = request.IsCompleted,
                        RewatchCount = request.RewatchCount
                    };

                    await _viewLogCollection.InsertOneAsync(newView);

                    // 3️⃣ CẬP NHẬT COUNT VÀO SQL (Để hiển thị số view ngoài trang chủ cho nhanh)
                    int currentTotalViews = 0;

                    // Chỉ cộng view nếu xem trên 3s và không phải tua nhanh (SkipViewCount)
                    if (request.WatchedSeconds >= 3 && !request.SkipViewCount)
                    {
                        var tinDang = await _context.TinDangs.FindAsync(request.MaTinDang);
                        if (tinDang != null)
                        {
                            tinDang.SoLuotXem += 1;
                            // Lưu SQL chỉ để update con số đếm
                            await _context.SaveChangesAsync();
                            currentTotalViews = tinDang.SoLuotXem;
                            Console.WriteLine($"✅ [Mongo+SQL] Tăng view ID {request.MaTinDang}: {tinDang.SoLuotXem}");
                        }
                    }
                    else
                    {
                        // Nếu không tăng view thì lấy số cũ ra để return
                        currentTotalViews = await _context.TinDangs
                           .Where(t => t.MaTinDang == request.MaTinDang)
                           .Select(t => t.SoLuotXem)
                           .FirstOrDefaultAsync();
                    }

                    return Ok(new
                    {
                        success = true,
                        message = $"New view tracked in Mongo ({(userId != null ? "User" : "Anonymous")})",
                        isNewView = true,
                        totalViews = currentTotalViews,
                        startedAt = now.ToString("dd/MM/yyyy HH:mm:ss")
                    });
                }
                else
                {
                    // 4️⃣ UPDATE VIEW CŨ (UPDATE VÀO MONGO)
                    // Logic: Cập nhật giây xem max, đánh dấu completed
                    var updateBuilder = Builders<VideoViewLog>.Update;
                    var updates = new List<UpdateDefinition<VideoViewLog>>();

                    // Chỉ update nếu số giây xem lớn hơn cái cũ
                    if (request.WatchedSeconds > lastView.WatchedSeconds)
                        updates.Add(updateBuilder.Set(x => x.WatchedSeconds, request.WatchedSeconds));

                    // Update rewatch count
                    if (request.RewatchCount > lastView.RewatchCount)
                        updates.Add(updateBuilder.Set(x => x.RewatchCount, request.RewatchCount));

                    // Update Completed
                    if (request.IsCompleted && !lastView.IsCompleted)
                        updates.Add(updateBuilder.Set(x => x.IsCompleted, true));

                    // Thực hiện update nếu có thay đổi
                    if (updates.Any())
                    {
                        var combinedUpdate = updateBuilder.Combine(updates);
                        await _viewLogCollection.UpdateOneAsync(x => x.Id == lastView.Id, combinedUpdate);
                    }

                    // Lấy total view từ SQL để trả về (nhẹ database hơn là count từ Mongo mỗi lần request)
                    var totalViews = await _context.TinDangs
                        .Where(t => t.MaTinDang == request.MaTinDang)
                        .Select(t => t.SoLuotXem)
                        .FirstOrDefaultAsync();

                    return Ok(new
                    {
                        success = true,
                        message = "Existing view updated in Mongo",
                        isNewView = false,
                        isCompleted = request.IsCompleted || lastView.IsCompleted,
                        totalViews,
                        startedAt = now.ToString("dd/MM/yyyy HH:mm:ss")
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Track view Mongo error: {ex.Message}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("explore/categories")]
        public async Task<IActionResult> GetExploreCategories()
        {
            var categories = await _context.DanhMucChas
                .Include(c => c.DanhMucs) // Join bảng con
                .Select(c => new CategoryHierarchyDto
                {
                    Id = c.MaDanhMucCha,
                    Ten = c.TenDanhMucCha,
                    Icon = c.Icon,
                    DanhMucCons = c.DanhMucs.Select(dm => new ChildCategoryDto
                    {
                        Id = dm.MaDanhMuc,
                        Ten = dm.TenDanhMuc
                    }).ToList()
                })
                .ToListAsync();

            return Ok(categories);
        }
        [HttpGet("explore/videos")]
        public async Task<IActionResult> GetExploreVideos(
            [FromQuery] int? parentCategoryId,
            [FromQuery] int pageSize = 10, // AI tính toán nặng, nên load khoảng 10-20 video/lần thôi
            [FromQuery] string? excludedIdsJson = null) // <--- MỚI: Client gửi lên danh sách ID đã xem để không bị trùng
        {
            // 1. Lấy UserId từ Token (nếu đã đăng nhập)
            string? userId = User.FindFirst("Id")?.Value; // Hoặc ClaimTypes.NameIdentifier tùy cấu hình JWT

            // 2. Xử lý danh sách ID đã xem (để AI không gợi ý lại cái vừa xem ở trang trước)
            List<int> excludedIds = new List<int>();
            if (!string.IsNullOrEmpty(excludedIdsJson))
            {
                try
                {
                    excludedIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(excludedIdsJson) ?? new List<int>();
                }
                catch { /* Bỏ qua lỗi parse */ }
            }

            // 3. Xử lý Category Group (Chuyển đổi từ ID cha sang tên để Service hiểu - nếu Service cần String)
            string? categoryGroupKeyword = null;
            if (parentCategoryId.HasValue && parentCategoryId.Value > 0)
            {
                var category = await _context.DanhMucChas.FindAsync(parentCategoryId.Value);
                categoryGroupKeyword = category?.TenDanhMucCha;
            }

            // =================================================================================
            // 4. 🔥 GỌI AI RECOMMENDATION SERVICE 🔥
            // Thay vì query trực tiếp, ta nhờ Service tính toán danh sách ID tốt nhất
            // =================================================================================
            var recommendedIds = await _recommendationService.GetRecommendedPostIds(
                userId: userId,
                clientExcludedIds: excludedIds,
                count: pageSize,
                isVideoOnly: true, // Chỉ lấy Video
                categoryGroup: categoryGroupKeyword
            );

            // Nếu AI không trả về gì (lỗi hoặc hết tin), fallback về lấy tin mới nhất (Logic cũ)
            if (recommendedIds == null || !recommendedIds.Any())
            {
                return await GetFallbackVideos(parentCategoryId, pageSize, excludedIds);
            }

            // 5. Fetch dữ liệu chi tiết từ DB dựa trên list ID mà AI đưa cho
            // Lưu ý: SQL "IN" sẽ không giữ thứ tự, nên ta phải sort lại sau khi lấy dữ liệu
            var videosRaw = await _context.TinDangs
                .AsNoTracking()
                .Include(t => t.NguoiBan)
                .Include(t => t.AnhTinDangs)
                .Where(t => recommendedIds.Contains(t.MaTinDang))
                .ToListAsync();

            // 6. Mapping sang DTO & Sắp xếp lại đúng thứ tự AI đã rank (Quan trọng!)
            var result = recommendedIds
                .Join(videosRaw, id => id, vid => vid.MaTinDang, (id, vid) => vid) // Giữ thứ tự của recommendedIds
                .Select(t => new ExploreVideoDto
                {
                    MaTinDang = t.MaTinDang,
                    TieuDe = t.TieuDe,
                    VideoUrl = t.VideoUrl,
                    ThumbnailUrl = t.AnhTinDangs.OrderBy(a => a.Order).Select(a => a.DuongDan).FirstOrDefault(),
                    SoLuotXem = t.SoLuotXem,

                    // Count số tim (Tối ưu query con)
                    SoLuotTim = _context.VideoLikes.Count(vl => vl.MaTinDang == t.MaTinDang),

                    TenNguoiBan = t.NguoiBan.FullName ?? "Người dùng",
                    AvatarNguoiBan = t.NguoiBan.AvatarUrl,
                    UserId = t.NguoiBan.Id
                })
                .ToList();

            return Ok(result);
        }

        // Hàm dự phòng (Fallback) khi User chưa có dữ liệu hoặc AI chưa tính kịp
        private async Task<IActionResult> GetFallbackVideos(int? parentCategoryId, int pageSize, List<int> excludedIds)
        {
            var query = _context.TinDangs
               .AsNoTracking()
               .Include(t => t.NguoiBan)
               .Include(t => t.AnhTinDangs)
               .Where(t => t.TrangThai == TrangThaiTinDang.DaDuyet
                           && !t.IsDeleted
                           && !string.IsNullOrEmpty(t.VideoUrl)
                           && !excludedIds.Contains(t.MaTinDang)); // Trừ các tin đã xem

            if (parentCategoryId.HasValue)
            {
                query = query.Where(t => t.DanhMuc.MaDanhMucCha == parentCategoryId.Value);
            }

            var videos = await query
                .OrderByDescending(t => t.NgayDang) // Mặc định tin mới nhất
                .Take(pageSize)
                .Select(t => new ExploreVideoDto
                {
                    MaTinDang = t.MaTinDang,
                    TieuDe = t.TieuDe,
                    VideoUrl = t.VideoUrl,
                    ThumbnailUrl = t.AnhTinDangs.OrderBy(a => a.Order).Select(a => a.DuongDan).FirstOrDefault(),
                    SoLuotXem = t.SoLuotXem,
                    SoLuotTim = _context.VideoLikes.Count(vl => vl.MaTinDang == t.MaTinDang),
                    TenNguoiBan = t.NguoiBan.FullName ?? "Người dùng",
                    AvatarNguoiBan = t.NguoiBan.AvatarUrl,
                    UserId = t.NguoiBan.Id
                })
                .ToListAsync();

            return Ok(videos);
        }

        public class TrackViewRequest
        {
            public int MaTinDang { get; set; }
            public int WatchedSeconds { get; set; }
            public bool IsCompleted { get; set; }
            public int RewatchCount { get; set; } = 0;
            public bool SkipViewCount { get; set; } = false;
        }
        private string CalculateTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "Vừa xong";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} phút trước";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} giờ trước";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} ngày trước";
            if (timeSpan.TotalDays < 30)
                return $"{(int)(timeSpan.TotalDays / 7)} tuần trước";
            if (timeSpan.TotalDays < 365)
                return $"{(int)(timeSpan.TotalDays / 30)} tháng trước";

            return $"{(int)(timeSpan.TotalDays / 365)} năm trước";
        }
    }
}