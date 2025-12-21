using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniMarket.Services;
using UniMarket.Models;
using UniMarket.Models.Mongo;
using UniMarket.DataAccess;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace UniMarket.Controllers
{
    /// <summary>
    /// API cho lịch sử xem tin đăng (MongoDB)
    /// Mỗi lần user xem tin được lưu vào MongoDB
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ViewHistoryController : ControllerBase
    {
        private readonly ViewHistoryMongoService _mongoService;
        private readonly ApplicationDbContext _context;

        public ViewHistoryController(ViewHistoryMongoService mongoService, ApplicationDbContext context)
        {
            _mongoService = mongoService;
            _context = context;
        }

        /// <summary>
        /// Lấy lịch sử xem tin của user (limit)
        /// GET /api/viewhistory/history?limit=10
        /// Trả về complete TinDang data với images và seller info
        /// </summary>
        [Authorize]
        [HttpGet("history")]
        public async Task<IActionResult> GetViewHistory([FromQuery] int limit = 10)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated" });

            var history = await _mongoService.GetUserViewHistoryAsync(userId, limit);

            // Lấy tất cả MaTinDang từ history
            var maTinDangList = history.Select(x => x.MaTinDang).ToList();

            // Query tất cả TinDang + seller + images một lần
            var tinDangs = await _context.TinDangs
                .Where(t => maTinDangList.Contains(t.MaTinDang))
                .Include(t => t.NguoiBan)
                .Include(t => t.AnhTinDangs)
                .Include(t => t.TinhThanh)
                .ToListAsync();

            // Map dữ liệu
            var result = history.Select(v =>
            {
                var tinDang = tinDangs.FirstOrDefault(t => t.MaTinDang == v.MaTinDang);
                if (tinDang == null)
                    return null;

                return new
                {
                    id = v.Id.ToString(),
                    maTinDang = v.MaTinDang,
                    tieuDe = tinDang.TieuDe,
                    gia = tinDang.Gia,
                    moTa = tinDang.MoTa,
                    tinhTrang = tinDang.TinhTrang,
                    tinhThanh = tinDang.TinhThanh?.TenTinhThanh ?? "",
                    quanHuyen = tinDang.QuanHuyen?.TenQuanHuyen ?? "",
                    diaChi = tinDang.DiaChi,
                    viewedAt = v.LastViewedAt,
                    watchedSeconds = v.WatchedSeconds,
                    isCompleted = v.IsCompleted,
                    rewatchCount = v.RewatchCount,
                    images = (object?)(tinDang.AnhTinDangs?.OrderBy(a => a.Order)
                        .Select(a => new { url = a.DuongDan })
                        .ToList()),
                    seller = new
                    {
                        id = tinDang.NguoiBan?.Id,
                        fullName = tinDang.NguoiBan?.FullName,
                        avatarUrl = tinDang.NguoiBan?.AvatarUrl
                    }
                };
            }).Where(x => x != null).ToList();

            return Ok(new
            {
                success = true,
                data = result,
                count = result.Count
            });
        }

        /// <summary>
        /// Lấy lịch sử xem tin phân trang
        /// GET /api/viewhistory/history-paged?page=1&pageSize=12
        /// Trả về complete TinDang data với images và seller info
        /// </summary>
        [Authorize]
        [HttpGet("history-paged")]
        public async Task<IActionResult> GetViewHistoryPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 12)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated" });

            var (history, totalCount) = await _mongoService.GetUserViewHistoryPagedAsync(userId, page, pageSize);

            // Lấy tất cả MaTinDang từ history
            var maTinDangList = history.Select(x => x.MaTinDang).ToList();

            // Query tất cả TinDang + seller + images một lần
            var tinDangs = await _context.TinDangs
                .Where(t => maTinDangList.Contains(t.MaTinDang))
                .Include(t => t.NguoiBan)
                .Include(t => t.AnhTinDangs)
                .Include(t => t.TinhThanh)
                .Include(t => t.QuanHuyen)
                .ToListAsync();

            // Map dữ liệu
            var data = history.Select(v =>
            {
                var tinDang = tinDangs.FirstOrDefault(t => t.MaTinDang == v.MaTinDang);
                if (tinDang == null)
                    return null;

                return new
                {
                    id = v.Id.ToString(),
                    maTinDang = v.MaTinDang,
                    tieuDe = tinDang.TieuDe,
                    gia = tinDang.Gia,
                    moTa = tinDang.MoTa,
                    tinhTrang = tinDang.TinhTrang,
                    tinhThanh = tinDang.TinhThanh?.TenTinhThanh ?? "",
                    quanHuyen = tinDang.QuanHuyen?.TenQuanHuyen ?? "",
                    diaChi = tinDang.DiaChi,
                    viewedAt = v.LastViewedAt,
                    watchedSeconds = v.WatchedSeconds,
                    isCompleted = v.IsCompleted,
                    rewatchCount = v.RewatchCount,
                    images = (object?)(tinDang.AnhTinDangs?.OrderBy(a => a.Order)
                        .Select(a => new { url = a.DuongDan })
                        .ToList()),
                    seller = new
                    {
                        id = tinDang.NguoiBan?.Id,
                        fullName = tinDang.NguoiBan?.FullName,
                        avatarUrl = tinDang.NguoiBan?.AvatarUrl
                    }
                };
            }).Where(x => x != null).ToList();

            var totalPages = (int)System.Math.Ceiling((double)totalCount / pageSize);

            return Ok(new
            {
                success = true,
                data = data,
                pagination = new
                {
                    page = page,
                    pageSize = pageSize,
                    totalCount = totalCount,
                    totalPages = totalPages
                }
            });
        }

        /// <summary>
        /// Track khi user xem tin đăng
        /// POST /api/viewhistory/track
        /// Body: { "maTinDang": 123, "watchedSeconds": 45, "isCompleted": false }
        /// </summary>
        [Authorize]
        [HttpPost("track")]
        public async Task<IActionResult> TrackView([FromBody] TrackViewRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated" });

            var ipAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString();
            var deviceName = Request.Headers["User-Agent"].ToString();

            var viewHistory = await _mongoService.TrackViewAsync(
                request.MaTinDang,
                userId,
                request.WatchedSeconds ?? 0,
                request.IsCompleted ?? false,
                ipAddress,
                deviceName
            );

            return Ok(new
            {
                success = true,
                message = "View tracked successfully",
                data = new
                {
                    id = viewHistory.Id.ToString(),
                    maTinDang = viewHistory.MaTinDang,
                    viewedAt = viewHistory.StartedAt,
                    watchedSeconds = viewHistory.WatchedSeconds,
                    isCompleted = viewHistory.IsCompleted
                }
            });
        }

        /// <summary>
        /// Xóa 1 record lịch sử xem
        /// DELETE /api/viewhistory/{viewHistoryId}
        /// </summary>
        [Authorize]
        [HttpDelete("{viewHistoryId}")]
        public async Task<IActionResult> DeleteViewHistory(string viewHistoryId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated" });

            var deleted = await _mongoService.DeleteViewHistoryAsync(viewHistoryId);

            if (!deleted)
                return NotFound(new { success = false, message = "View history not found" });

            return Ok(new { success = true, message = "Deleted successfully" });
        }

        /// <summary>
        /// Xóa toàn bộ lịch sử xem của user
        /// DELETE /api/viewhistory/clear-history
        /// </summary>
        [Authorize]
        [HttpDelete("clear-history")]
        public async Task<IActionResult> ClearViewHistory()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { success = false, message = "User not authenticated" });

            var deletedCount = await _mongoService.ClearUserViewHistoryAsync(userId);

            return Ok(new
            {
                success = true,
                message = $"Deleted {deletedCount} view records"
            });
        }
    }

    /// <summary>
    /// Request model cho track view
    /// </summary>
    public class TrackViewRequest
    {
        public int MaTinDang { get; set; }
        public int? WatchedSeconds { get; set; }
        public bool? IsCompleted { get; set; }
    }
}