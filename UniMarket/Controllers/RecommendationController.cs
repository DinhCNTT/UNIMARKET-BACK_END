using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Services.Recommendation;
using UniMarket.DTO; // Đảm bảo namespace này chứa ForYouRequestDto

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RecommendationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly VideoRecommendationService _recommendationService;

        public RecommendationController(ApplicationDbContext context, VideoRecommendationService recommendationService)
        {
            _context = context;
            _recommendationService = recommendationService;
        }

        // ==========================================
        // 🎯 API: LẤY DANH SÁCH VIDEO "FOR YOU"
        // ==========================================
        [HttpPost("foryou")]
        [AllowAnonymous]
        public async Task<IActionResult> GetForYouVideos([FromBody] ForYouRequestDto request)
        {
            try
            {
                var userId = User.Identity != null && User.Identity.IsAuthenticated
                    ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                    : null;

                // ---------------------------------------------------------
                // 1. Lấy danh sách ID video đề xuất từ Service AI (Đã sắp xếp theo điểm)
                // ---------------------------------------------------------
                var recommendedIds = await _recommendationService.GetRecommendedPostIds(
                    userId,
                    request.ExcludedIds ?? new List<int>(),
                    request.PageSize,
                    isVideoOnly: true // 👈 Chỉ lấy tin có Video
                );

                if (!recommendedIds.Any())
                {
                    return Ok(new List<object>());
                }

                // ---------------------------------------------------------
                // 2. Fetch chi tiết video từ Database (Bulk Query)
                // ---------------------------------------------------------
                var videosData = await _context.TinDangs
                    .AsNoTracking()
                    .Where(t => recommendedIds.Contains(t.MaTinDang))
                    .Include(t => t.NguoiBan)
                    .Include(t => t.AnhTinDangs)
                    .Include(t => t.TinhThanh)
                    .Include(t => t.QuanHuyen)
                    .ToListAsync();

                // ---------------------------------------------------------
                // 3. Tối ưu: Đếm số lượng tương tác (Bulk Count)
                // ---------------------------------------------------------
                var foundIds = videosData.Select(v => v.MaTinDang).ToList();

                // 3.1. Likes
                var tymCounts = await _context.VideoLikes
                    .Where(v => foundIds.Contains(v.MaTinDang))
                    .GroupBy(v => v.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // 3.2. Comments
                var commentCounts = await _context.VideoComments
                    .Where(c => foundIds.Contains(c.MaTinDang))
                    .GroupBy(c => c.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // 3.3. Shares
                var shareCounts = await _context.Shares
                    .Where(s => s.TinDangId.HasValue && foundIds.Contains(s.TinDangId.Value))
                    .GroupBy(s => s.TinDangId.Value)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // 3.4. Video Saves (Lưu Video)
                var videoSaveCounts = await _context.VideoTinDangSaves
                    .Where(s => foundIds.Contains(s.MaTinDang))
                    .GroupBy(s => s.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // 3.5. Post Favorites (Yêu Thích Tin - Giỏ hàng/Tim)
                var postFavCounts = await _context.TinDangYeuThichs
                    .Where(s => foundIds.Contains(s.MaTinDang))
                    .GroupBy(s => s.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // ---------------------------------------------------------
                // 4. Kiểm tra trạng thái cá nhân của User hiện tại
                // ---------------------------------------------------------
                var userLikedIds = new HashSet<int>();
                var userVideoSavedIds = new HashSet<int>();
                var userPostFavoritedIds = new HashSet<int>();
                var userFollowedAuthors = new HashSet<string>();

                if (!string.IsNullOrEmpty(userId))
                {
                    // Lấy danh sách Like
                    var likes = await _context.VideoLikes
                        .Where(v => v.UserId == userId && foundIds.Contains(v.MaTinDang))
                        .Select(v => v.MaTinDang).ToListAsync();
                    userLikedIds = new HashSet<int>(likes);

                    // Lấy danh sách Save Video
                    var saves = await _context.VideoTinDangSaves
                        .Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                        .Select(s => s.MaTinDang).ToListAsync();
                    userVideoSavedIds = new HashSet<int>(saves);

                    // Lấy danh sách Favorite Post
                    var favs = await _context.TinDangYeuThichs
                        .Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                        .Select(s => s.MaTinDang).ToListAsync();
                    userPostFavoritedIds = new HashSet<int>(favs);

                    // Lấy danh sách Follow Author
                    var authorIds = videosData.Select(v => v.MaNguoiBan).Distinct().ToList();
                    var follows = await _context.Follows
                        .Where(f => f.FollowerId == userId && authorIds.Contains(f.FollowingId))
                        .Select(f => f.FollowingId).ToListAsync();
                    userFollowedAuthors = new HashSet<string>(follows);
                }

                // ---------------------------------------------------------
                // 5. Map kết quả & Xử lý ưu tiên Thumbnail Video
                // ---------------------------------------------------------
                // Sử dụng .Join để đảm bảo thứ tự ranking của recommendedIds được bảo toàn
                var result = recommendedIds
                    .Join(videosData, id => id, v => v.MaTinDang, (id, v) => v)
                    .Select(td =>
                    {
                        // 🔥 LOGIC QUAN TRỌNG: Ưu tiên lấy Thumbnail từ Video Cloudinary
                        string? generatedThumb = GetCloudinaryThumbnail(td.VideoUrl);
                        string? productThumb = td.AnhTinDangs?.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan;

                        // Nếu tạo được thumb từ video thì dùng nó, nếu không thì dùng ảnh sản phẩm
                        string finalImage = !string.IsNullOrEmpty(generatedThumb) ? generatedThumb : (productThumb ?? "");

                        return new
                        {
                            td.MaTinDang,
                            td.TieuDe,
                            td.MoTa,
                            td.VideoUrl,

                            // ✅ Trả về ảnh đã được xử lý logic
                            HinhAnh = finalImage,

                            td.Gia,
                            td.DiaChi,
                            TinhThanh = td.TinhThanh?.TenTinhThanh ?? "",
                            QuanHuyen = td.QuanHuyen?.TenQuanHuyen ?? "",
                            td.TinhTrang,
                            td.NgayDang,

                            // Thông số kỹ thuật hiển thị
                            td.SoLuotXem,
                            AnhCount = td.AnhTinDangs?.Count(a => a.LoaiMedia == MediaType.Image) ?? 0,

                            // Số liệu tương tác
                            SoTym = tymCounts.GetValueOrDefault(td.MaTinDang, 0),
                            SoBinhLuan = commentCounts.GetValueOrDefault(td.MaTinDang, 0),
                            SoLuotChiaSe = shareCounts.GetValueOrDefault(td.MaTinDang, 0),

                            SoNguoiLuu = videoSaveCounts.GetValueOrDefault(td.MaTinDang, 0),
                            SoLuotYeuThich = postFavCounts.GetValueOrDefault(td.MaTinDang, 0),

                            // Trạng thái User
                            IsLiked = userLikedIds.Contains(td.MaTinDang),
                            IsSaved = userVideoSavedIds.Contains(td.MaTinDang),
                            IsFavorited = userPostFavoritedIds.Contains(td.MaTinDang),

                            // Thông tin người đăng
                            NguoiDang = td.NguoiBan != null
                                ? new
                                {
                                    td.NguoiBan.Id,
                                    td.NguoiBan.FullName,
                                    td.NguoiBan.AvatarUrl,
                                    IsFollowed = userFollowedAuthors.Contains(td.NguoiBan.Id)
                                }
                                : null
                        };
                    })
                    .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                // Log error (nên dùng ILogger trong thực tế)
                Console.WriteLine($"Error in GetForYouVideos: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi hệ thống khi lấy danh sách video đề xuất." });
            }
        }

        // ==========================================================
        // 🛠️ HÀM HỖ TRỢ: CHUYỂN LINK VIDEO CLOUDINARY -> ẢNH JPG
        // ==========================================================
        private string? GetCloudinaryThumbnail(string? videoUrl)
        {
            if (string.IsNullOrEmpty(videoUrl)) return null;

            if (videoUrl.Contains("cloudinary.com"))
            {
                // Cloudinary hỗ trợ đổi đuôi file để lấy thumbnail
                // VD: .../upload/v123/video.mp4 -> .../upload/v123/video.jpg

                if (videoUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    return videoUrl.Substring(0, videoUrl.LastIndexOf('.')) + ".jpg";
                }

                if (videoUrl.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
                {
                    return videoUrl.Substring(0, videoUrl.LastIndexOf('.')) + ".jpg";
                }

                // Nếu URL không có đuôi file rõ ràng nhưng là Cloudinary, thử append .jpg
                // (Trừ khi nó đã là ảnh)
                if (!videoUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                    !videoUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    return videoUrl + ".jpg";
                }
            }
            return null;
        }
    }
}