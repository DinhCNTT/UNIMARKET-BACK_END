using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Services.Recommendation;
using UniMarket.DTO; // Đảm bảo bạn đã có namespace DTO

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
                    isVideoOnly: true // 👈 Truyền true vì API này chuyên lấy Video (theo tên hàm GetForYouVideos)
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

                // 🔥 3.4. Video Saves (Lưu Video) - TÁCH RIÊNG
                var videoSaveCounts = await _context.VideoTinDangSaves
                    .Where(s => foundIds.Contains(s.MaTinDang))
                    .GroupBy(s => s.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // 🔥 3.5. Post Favorites (Yêu Thích Tin) - TÁCH RIÊNG
                var postFavCounts = await _context.TinDangYeuThichs
                    .Where(s => foundIds.Contains(s.MaTinDang))
                    .GroupBy(s => s.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // ---------------------------------------------------------
                // 4. Kiểm tra trạng thái cá nhân của User hiện tại
                // ---------------------------------------------------------
                var userLikedIds = new HashSet<int>();
                var userVideoSavedIds = new HashSet<int>();    // 🔥 Đã lưu video chưa
                var userPostFavoritedIds = new HashSet<int>(); // 🔥 Đã yêu thích tin chưa
                var userFollowedAuthors = new HashSet<string>();

                if (!string.IsNullOrEmpty(userId))
                {
                    // Lấy danh sách Like
                    var likes = await _context.VideoLikes
                        .Where(v => v.UserId == userId && foundIds.Contains(v.MaTinDang))
                        .Select(v => v.MaTinDang).ToListAsync();
                    userLikedIds = new HashSet<int>(likes);

                    // 🔥 Lấy danh sách Save Video
                    var saves = await _context.VideoTinDangSaves
                        .Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                        .Select(s => s.MaTinDang).ToListAsync();
                    userVideoSavedIds = new HashSet<int>(saves);

                    // 🔥 Lấy danh sách Favorite Post
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
                // 5. Map kết quả (Giữ thứ tự Ranking của recommendedIds)
                // ---------------------------------------------------------
                // Sử dụng .Join để đảm bảo thứ tự của recommendedIds được bảo toàn
                var result = recommendedIds
                    .Join(videosData, id => id, v => v.MaTinDang, (id, v) => v)
                    .Select(td => new
                    {
                        td.MaTinDang,
                        td.TieuDe,
                        td.MoTa,
                        td.VideoUrl,
                        // Lấy ảnh đầu tiên làm thumbnail
                        HinhAnh = td.AnhTinDangs?.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan ?? "",
                        td.Gia,
                        td.DiaChi,
                        TinhThanh = td.TinhThanh?.TenTinhThanh ?? "",
                        QuanHuyen = td.QuanHuyen?.TenQuanHuyen ?? "",
                        td.TinhTrang,
                        td.NgayDang,

                        // Thông số kỹ thuật hiển thị
                        td.SoLuotXem,
                        AnhCount = td.AnhTinDangs?.Count(a => a.LoaiMedia == MediaType.Image) ?? 0,

                        // --- SỐ LIỆU TƯƠNG TÁC (ĐÃ TÁCH) ---
                        SoTym = tymCounts.GetValueOrDefault(td.MaTinDang, 0),
                        SoBinhLuan = commentCounts.GetValueOrDefault(td.MaTinDang, 0),
                        SoLuotChiaSe = shareCounts.GetValueOrDefault(td.MaTinDang, 0),

                        // 🔥 Hiển thị riêng 2 chỉ số này cho Frontend
                        SoNguoiLuu = videoSaveCounts.GetValueOrDefault(td.MaTinDang, 0),
                        SoLuotYeuThich = postFavCounts.GetValueOrDefault(td.MaTinDang, 0),

                        // --- TRẠNG THÁI USER (ĐÃ TÁCH) ---
                        IsLiked = userLikedIds.Contains(td.MaTinDang),

                        // 🔥 Trả về 2 trạng thái riêng
                        IsSaved = userVideoSavedIds.Contains(td.MaTinDang),        // Trạng thái nút Bookmark
                        IsFavorited = userPostFavoritedIds.Contains(td.MaTinDang), // Trạng thái nút Tim/Giỏ hàng

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
                    })
                    .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetForYouVideos: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return StatusCode(500, new { message = "Lỗi hệ thống khi lấy danh sách video đề xuất." });
            }
        }
    }
}