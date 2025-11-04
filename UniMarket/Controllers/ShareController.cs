using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;

namespace UniMarket.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShareController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        public ShareController(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }



        // ===============================
        // SHARE RA MXH
        // ===============================
        [HttpPost("social")]
        [AllowAnonymous]   // ✅ Cho phép gọi không cần token
        public async Task<IActionResult> ShareSocial([FromBody] ShareSocialRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { message = "Dữ liệu không hợp lệ.", errors = ModelState });

            var tinDang = await _context.TinDangs
                .Include(t => t.AnhTinDangs)
                .FirstOrDefaultAsync(t => t.MaTinDang == req.TinDangId);

            if (tinDang == null)
                return NotFound("Tin đăng không tồn tại.");

            // ⚡ Nếu user login thì lưu DB, không thì chỉ trả link
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
                    TinDangId = req.TinDangId,
                    Platform = req.Platform,
                    PreviewTitle = tinDang.TieuDe,
                    PreviewImage = tinDang.AnhTinDangs?.FirstOrDefault()?.DuongDan,
                    PreviewVideo = tinDang.VideoUrl,
                    SharedAt = DateTime.UtcNow
                };

                _context.Shares.Add(share);
                await _context.SaveChangesAsync();
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
