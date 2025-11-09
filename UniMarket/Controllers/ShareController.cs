using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;
using Microsoft.AspNetCore.SignalR;
using UniMarket.Hubs;
namespace UniMarket.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShareController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly IHubContext<VideoHub> _videoHubContext;
        public ShareController(ApplicationDbContext context,
                               IConfiguration config,
                               IHubContext<VideoHub> videoHubContext)
        {
            _context = context;
            _config = config;
            _videoHubContext = videoHubContext;
        }



        // ===============================
        // SHARE RA MXH
        // ===============================
        // File: UniMarket/Controllers/ShareController.cs

        [HttpPost("social")]
        [AllowAnonymous]
        public async Task<IActionResult> ShareSocial([FromBody] ShareSocialRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Dữ liệu không hợp lệ.", errors = ModelState });

            var tinDang = await _context.TinDangs
                .Include(t => t.AnhTinDangs)
                .FirstOrDefaultAsync(t => t.MaTinDang == req.TinDangId);

            if (tinDang == null)
                return NotFound("Tin đăng không tồn tại.");

            string? userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);

            Share? share = null;
            if (!string.IsNullOrEmpty(userId))
            {
                share = new Share
                {
                    UserId = userId,
                    ShareType = ShareType.SocialMedia,
                    TargetType = req.DisplayMode == ShareDisplayMode.Video
                        ? ShareTargetType.Video
                        : ShareTargetType.TinDang,
                    DisplayMode = req.DisplayMode,
                    TinDangId = req.TinDangId, // Gán trực tiếp int vào int? (hoàn toàn hợp lệ)
                    Platform = req.Platform,
                    PreviewTitle = tinDang.TieuDe,
                    PreviewImage = tinDang.AnhTinDangs?.FirstOrDefault()?.DuongDan,
                    PreviewVideo = tinDang.VideoUrl,
                    SharedAt = DateTime.UtcNow
                };

                _context.Shares.Add(share);
                await _context.SaveChangesAsync();
            }

            // ✅✅ BƯỚC 5: LOGIC GỬI REAL-TIME (ĐÃ SỬA LỖI)
            // Vì req.TinDangId là 'int', nó luôn có giá trị.
            // Chúng ta chỉ cần kiểm tra xem nó có phải là ID hợp lệ không (vd: > 0)
            if (req.TinDangId > 0)
            {
                // Sử dụng trực tiếp req.TinDangId (không cần .Value)
                var tinDangId = req.TinDangId;

                // Đếm tổng số lượt share.
                // (s.TinDangId là int?, tinDangId là int. Phép so sánh này là hợp lệ)
                var totalShares = await _context.Shares
                    .CountAsync(s => s.TinDangId == tinDangId);

                // Gửi cập nhật real-time
                await _videoHubContext.Clients.Group(tinDangId.ToString())
                    .SendAsync("UpdateShareCount", tinDangId, totalShares);
            }

            var baseUrl = _config["AppSettings:FrontendUrl"] ?? "http://localhost:5173";

            string link = req.DisplayMode == ShareDisplayMode.Video
                ? $"{baseUrl}/video/{tinDang.MaTinDang}?index={req.Index ?? 0}"
                : $"{baseUrl}/tin-dang/{tinDang.MaTinDang}";

            return Ok(new
            {
                success = true,
                type = "social",
                ShareId = share?.ShareId,
                req.Platform,
                ShareLink = link,
                req.DisplayMode,
                tinDang.TieuDe,
                PreviewImage = tinDang.AnhTinDangs?.FirstOrDefault()?.DuongDan,
                tinDang.VideoUrl
            });
        }



        // ===============================
        // LẤY SHARE CỦA USER
        // ===============================
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetSharesByUser(string userId)
        {
            var shares = await _context.Shares
                .Include(s => s.TinDang)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.SharedAt)
                .ToListAsync();

            return Ok(shares);
        }

        // ===============================
        // LẤY SHARE THEO TIN ĐĂNG
        // ===============================
        [HttpGet("tin/{tinDangId}")]
        public async Task<IActionResult> GetSharesByTinDang(int tinDangId)
        {
            var shares = await _context.Shares
                .Include(s => s.User)
                .Where(s => s.TinDangId == tinDangId)
                .OrderByDescending(s => s.SharedAt)
                .ToListAsync();

            return Ok(shares);
        }
    }
}