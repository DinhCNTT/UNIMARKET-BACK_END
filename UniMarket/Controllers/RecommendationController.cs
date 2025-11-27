using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Services.Recommendation;
using UniMarket.DTO;

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

                // 1. Lấy danh sách ID video đề xuất từ Service AI
                var recommendedIds = await _recommendationService.GetForYouVideoIds(
                    userId,
                    request.ExcludedIds ?? new List<int>(),
                    request.PageSize
                );

                if (!recommendedIds.Any())
                {
                    return Ok(new List<object>());
                }

                // 2. Fetch chi tiết video (NoTracking để tối ưu đọc)
                var videosData = await _context.TinDangs
                    .AsNoTracking()
                    .Where(t => recommendedIds.Contains(t.MaTinDang))
                    .Include(t => t.NguoiBan)
                    .Include(t => t.AnhTinDangs)
                    .Include(t => t.TinhThanh)
                    .Include(t => t.QuanHuyen)
                    .ToListAsync();

                // 3. Tối ưu: Bulk Query đếm tương tác
                // ✅ SỬA LỖI: Chạy tuần tự thay vì song song để tránh lỗi DbContext Threading
                var foundIds = videosData.Select(v => v.MaTinDang).ToList();

                var tymCounts = await _context.VideoLikes
                    .Where(v => foundIds.Contains(v.MaTinDang))
                    .GroupBy(v => v.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                var commentCounts = await _context.VideoComments
                    .Where(c => foundIds.Contains(c.MaTinDang))
                    .GroupBy(c => c.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                var shareCounts = await _context.Shares
                    .Where(s => s.TinDangId.HasValue && foundIds.Contains(s.TinDangId.Value))
                    .GroupBy(s => s.TinDangId.Value)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                var saveCounts = await _context.VideoTinDangSaves
                    .Where(s => foundIds.Contains(s.MaTinDang))
                    .GroupBy(s => s.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // 4. Kiểm tra trạng thái cá nhân (Like/Save/Follow)
                var userLikedIds = new HashSet<int>();
                var userSavedIds = new HashSet<int>();
                var userFollowedAuthors = new HashSet<string>();

                if (!string.IsNullOrEmpty(userId))
                {
                    // Lấy danh sách Like
                    var likes = await _context.VideoLikes
                        .Where(v => v.UserId == userId && foundIds.Contains(v.MaTinDang))
                        .Select(v => v.MaTinDang).ToListAsync();
                    userLikedIds = new HashSet<int>(likes);

                    // Lấy danh sách Save
                    var saves = await _context.VideoTinDangSaves
                        .Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                        .Select(s => s.MaTinDang).ToListAsync();
                    userSavedIds = new HashSet<int>(saves);

                    // Lấy danh sách Follow
                    var authorIds = videosData.Select(v => v.MaNguoiBan).Distinct().ToList();
                    var follows = await _context.Follows
                        .Where(f => f.FollowerId == userId && authorIds.Contains(f.FollowingId))
                        .Select(f => f.FollowingId).ToListAsync();
                    userFollowedAuthors = new HashSet<string>(follows);
                }

                // 5. Map kết quả (Join để giữ thứ tự Ranking)
                var result = recommendedIds
                    .Join(videosData, id => id, v => v.MaTinDang, (id, v) => v)
                    .Select(td => new
                    {
                        td.MaTinDang,
                        td.TieuDe,
                        td.MoTa,
                        td.VideoUrl,
                        HinhAnh = td.AnhTinDangs?.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan ?? "",
                        td.Gia,
                        td.DiaChi,
                        TinhThanh = td.TinhThanh?.TenTinhThanh ?? "",
                        QuanHuyen = td.QuanHuyen?.TenQuanHuyen ?? "",
                        td.TinhTrang,
                        td.NgayDang,

                        // Số liệu tương tác
                        SoTym = tymCounts.GetValueOrDefault(td.MaTinDang, 0),
                        SoBinhLuan = commentCounts.GetValueOrDefault(td.MaTinDang, 0),
                        SoLuotChiaSe = shareCounts.GetValueOrDefault(td.MaTinDang, 0),
                        SoNguoiLuu = saveCounts.GetValueOrDefault(td.MaTinDang, 0),
                        td.SoLuotXem,

                        // Trạng thái user
                        IsLiked = userLikedIds.Contains(td.MaTinDang),
                        IsSaved = userSavedIds.Contains(td.MaTinDang),

                        // Thông tin người đăng + Trạng thái Follow
                        NguoiDang = td.NguoiBan != null
                            ? new
                            {
                                td.NguoiBan.Id,
                                td.NguoiBan.FullName,
                                td.NguoiBan.AvatarUrl,
                                IsFollowed = userFollowedAuthors.Contains(td.NguoiBan.Id) // 🔥 Đã fix lỗi logic
                            }
                            : null
                    })
                    .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                // Ghi log lỗi
                Console.WriteLine($"Error in GetForYouVideos: {ex.Message}");
                // In ra stack trace để debug dễ hơn
                Console.WriteLine(ex.StackTrace);
                return StatusCode(500, new { message = "Lỗi hệ thống khi lấy danh sách video." });
            }
        }
    }
}