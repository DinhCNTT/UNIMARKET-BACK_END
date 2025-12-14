using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using UniMarket.Hubs;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ReportsController> _logger;
        private readonly IHubContext<NotificationHub> _notificationHub;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReportsController(ApplicationDbContext context, ILogger<ReportsController> logger, IHubContext<NotificationHub> notificationHub, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _logger = logger;
            _notificationHub = notificationHub;
            _userManager = userManager;
        }

        public class ReportRequest
        {
            [Required]
            public string TargetType { get; set; } = null!; // "Post" or "Video"

            [Required]
            public int TargetId { get; set; }

            [Required]
            [MaxLength(200)]
            public string Reason { get; set; } = null!;

            [MaxLength(2000)]
            public string? Details { get; set; }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateReport([FromBody] ReportRequest request)
        {
            if (request == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Người dùng chưa đăng nhập." });

            // Parse TargetType into enum
            if (!Enum.TryParse<ReportTargetType>(request.TargetType, true, out var parsedTargetType))
            {
                return BadRequest(new { message = "TargetType không hợp lệ. Chọn 'Post' hoặc 'Video'." });
            }

            // Validate target existence
            var exists = await _context.TinDangs.AnyAsync(t => t.MaTinDang == request.TargetId);
            if (!exists)
            {
                return NotFound(new { message = "Không tìm thấy mục được báo cáo." });
            }

            // Duplicate protection: same user cannot report the same target more than once
            var alreadyReported = await _context.Reports.AnyAsync(r =>
                r.ReporterId == userId &&
                r.TargetType == parsedTargetType &&
                r.TargetId == request.TargetId);

            if (alreadyReported)
            {
                return Conflict(new { message = "Bạn đã báo cáo mục này trước đó." });
            }

            var report = new Report
            {
                ReporterId = userId,
                TargetType = parsedTargetType,
                TargetId = request.TargetId,
                Reason = request.Reason.Trim(),
                Details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                IsResolved = false
            };

            _context.Reports.Add(report);
            await _context.SaveChangesAsync();

            _logger.LogInformation("New report created: {ReportId} by {UserId} for {TargetType}:{TargetId}", report.MaBaoCao, userId, parsedTargetType, request.TargetId);

            // Broadcast the raw report to admins so admin UI can update in real-time
            try
            {
                await _notificationHub.Clients.Group("admins").SendAsync("ReceiveReport", new
                {
                    id = report.MaBaoCao,
                    reporterId = report.ReporterId,
                    targetType = report.TargetType.ToString(),
                    targetId = report.TargetId,
                    reason = report.Reason,
                    details = report.Details,
                    createdAt = report.CreatedAt
                });
                _logger.LogInformation("Broadcasted report {ReportId} to admins", report.MaBaoCao);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast report {ReportId} to admins", report.MaBaoCao);
            }

            // Note: Do not create a notification for the owner at report creation time.
            // The desired flow is: Reporter -> Admin reviews/handles -> Admin may choose to notify the owner.
            // Admin-facing endpoints such as WarnSellerFromReportedPost already create notifications when appropriate.

            return CreatedAtAction(nameof(GetReportById), new { id = report.MaBaoCao }, new { id = report.MaBaoCao, message = "Báo cáo đã được gửi." });
        }

        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetReportById(int id)
        {
            var report = await _context.Reports
                .Include(r => r.Reporter)
                .FirstOrDefaultAsync(r => r.MaBaoCao == id);

            if (report == null) return NotFound();

            // Only admin or the reporter can view
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin && report.ReporterId != userId)
                return Forbid();

            return Ok(report);
        }

        // Check whether current user already reported a target
        [HttpGet("exists")]
        [Authorize]
        public async Task<IActionResult> ReportExists([FromQuery] string targetType, [FromQuery] int targetId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "Người dùng chưa đăng nhập." });

            if (!Enum.TryParse<ReportTargetType>(targetType, true, out var parsedTargetType))
            {
                return BadRequest(new { message = "TargetType không hợp lệ. Chọn 'Post' hoặc 'Video'." });
            }

            var exists = await _context.Reports.AnyAsync(r => r.ReporterId == userId && r.TargetType == parsedTargetType && r.TargetId == targetId);
            return Ok(new { exists });
        }

        // Admin: paged list with filters
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetReports([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] string? targetType = null, [FromQuery] bool? isResolved = null)
        {
            try
            {
                if (page <= 0) page = 1;
                if (pageSize <= 0 || pageSize > 200) pageSize = 50;

                var query = _context.Reports.Include(r => r.Reporter).AsQueryable();

                if (!string.IsNullOrWhiteSpace(targetType) && Enum.TryParse<ReportTargetType>(targetType, true, out var tt))
                {
                    query = query.Where(r => r.TargetType == tt);
                }

                if (isResolved.HasValue)
                {
                    query = query.Where(r => r.IsResolved == isResolved.Value);
                }

                var total = await query.CountAsync();
                var items = await query.OrderByDescending(r => r.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return Ok(new { total, page, pageSize, items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetReports (page={Page}, pageSize={PageSize})", page, pageSize);
                return StatusCode(500, new { message = "Lỗi khi lấy danh sách báo cáo.", detail = ex.Message });
            }
        }

        // Admin: mark report resolved
        [HttpPut("{id}/resolve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ResolveReport(int id)
        {
            var report = await _context.Reports.FindAsync(id);
            if (report == null) return NotFound();

            if (report.IsResolved) return BadRequest(new { message = "Báo cáo đã được xử lý." });

            report.IsResolved = true;
            report.ResolvedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Report {ReportId} marked resolved by admin", id);

            return Ok(new { message = "Đã đánh dấu là đã xử lý." });
        }

        // Admin: dismiss report (alias to resolve but keeps target intact)
        [HttpPost("{id}/dismiss")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DismissReport(int id, [FromBody] string? note = null)
        {
            var report = await _context.Reports.FindAsync(id);
            if (report == null) return NotFound();

            if (report.IsResolved) return BadRequest(new { message = "Báo cáo đã được xử lý." });

            report.IsResolved = true;
            report.ResolvedAt = DateTimeOffset.UtcNow;
            // Optionally store note in Details (append)
            if (!string.IsNullOrWhiteSpace(note))
            {
                report.Details = (report.Details ?? string.Empty) + "\nADMIN_NOTE: " + note;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Report {ReportId} dismissed by admin", id);
            return Ok(new { message = "Báo cáo đã bị bỏ qua." });
        }

        // Admin: delete the reported post (if target is Post)
        [HttpPost("{id}/delete-post")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteReportedPost(int id)
        {
            var report = await _context.Reports.FindAsync(id);
            if (report == null) return NotFound();

            if (report.TargetType != ReportTargetType.Post)
                return BadRequest(new { message = "Target không phải là tin đăng." });

            // Load the post
            var post = await _context.TinDangs
                .FirstOrDefaultAsync(t => t.MaTinDang == report.TargetId);

            if (post == null)
                return NotFound(new { message = "Không tìm thấy tin đăng." });

            // Capture snapshot info for notification
            var snapshotTitle = post.TieuDe;

            // Fetch owner (if exists) so we can notify them after deletion
            var owner = await _context.Users.FirstOrDefaultAsync(u => u.Id == post.MaNguoiBan);

            // Load related images for Cloudinary deletion
            var images = await _context.AnhTinDangs.Where(a => a.MaTinDang == report.TargetId).ToListAsync();

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // ✅ HARD DELETE: Xóa dữ liệu hoàn toàn (giống logic QuanLyTin)
                // Không chỉ đánh dấu status, mà xóa hẳn record khỏi DB
                
                // 1. Xóa file trên Cloudinary
                foreach (var img in images)
                {
                    if (!string.IsNullOrEmpty(img.DuongDan) && img.DuongDan.StartsWith("http"))
                    {
                        try
                        {
                            await DeleteCloudinaryPhotoByUrlAsync(img.DuongDan);
                        }
                        catch (Exception cloudEx)
                        {
                            _logger.LogWarning(cloudEx, "Failed to delete Cloudinary file for post {PostId}", report.TargetId);
                        }
                    }
                }

                // 2. Xóa dữ liệu liên quan trong SQL Server
                _context.AnhTinDangs.RemoveRange(_context.AnhTinDangs.Where(a => a.MaTinDang == report.TargetId));
                _context.TinDangYeuThichs.RemoveRange(_context.TinDangYeuThichs.Where(t => t.MaTinDang == report.TargetId));
                _context.VideoComments.RemoveRange(_context.VideoComments.Where(c => c.MaTinDang == report.TargetId));
                _context.VideoLikes.RemoveRange(_context.VideoLikes.Where(l => l.MaTinDang == report.TargetId));
                _context.VideoViews.RemoveRange(_context.VideoViews.Where(v => v.MaTinDang == report.TargetId));
                _context.VideoTinDangSaves.RemoveRange(_context.VideoTinDangSaves.Where(v => v.MaTinDang == report.TargetId));
                
                // 3. Xóa các chat liên quan (hard delete)
                var cuocTros = await _context.CuocTroChuyens.Where(c => c.MaTinDang == report.TargetId).ToListAsync();
                _context.CuocTroChuyens.RemoveRange(cuocTros);

                // Mark report as resolved
                report.IsResolved = true;
                report.ResolvedAt = DateTimeOffset.UtcNow;

                // 4. Xóa chi tiết trong MongoDB
                try
                {
                    var mongoDetail = await _mongoService.GetByMaTinDangAsync(report.TargetId);
                    if (mongoDetail != null)
                    {
                        await _mongoService.DeleteByIdAsync(mongoDetail.Id);
                        _logger.LogInformation("[MONGO] Deleted post details for post {PostId}", report.TargetId);
                    }
                }
                catch (Exception mongoEx)
                {
                    _logger.LogWarning(mongoEx, "Warning: Could not delete MongoDB details for post {PostId}", report.TargetId);
                    // Không rollback - tiếp tục xóa SQL ngay cả khi MongoDB không sẵn sàng
                }

                // Create a notification for the owner to inform them the post was deleted by admin
                Notification? notif = null;
                try
                {
                    if (owner != null)
                    {
                        var title = "Tin đăng của bạn đã bị xóa";
                        var url = $"/posts/{post.MaTinDang}";
                        var message = "Tin đăng của bạn đã bị xóa bởi Quản trị viên vì vi phạm chính sách cộng đồng.";

                        notif = new Notification
                        {
                            UserId = owner.Id,
                            Title = title,
                            Message = message,
                            Url = url,
                            IsRead = false,
                            CreatedAt = DateTimeOffset.UtcNow,
                            IsFromAdmin = true
                        };

                        _context.Notifications.Add(notif);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not prepare owner notification for deleted post {PostId}", post.MaTinDang);
                    notif = null;
                }

                // 5. Xóa TinDang chính từ SQL (hard delete)
                _context.TinDangs.Remove(post);

                await _context.SaveChangesAsync();

                // Commit DB transaction
                await tx.CommitAsync();

                // Broadcast notification (best-effort) after commit
                if (notif != null)
                {
                    try
                    {
                        await _notificationHub.Clients.Group($"user-{notif.UserId}")
                            .SendAsync("ReceiveNotification", new 
                            { 
                                id = notif.Id, 
                                title = notif.Title, 
                                message = notif.Message, 
                                url = notif.Url, 
                                createdAt = notif.CreatedAt, 
                                postTitle = snapshotTitle, 
                                type = "deleted", 
                                isFromAdmin = true 
                            });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to broadcast deletion notification for post {PostId}", post.MaTinDang);
                    }
                }

                _logger.LogInformation("Admin hard-deleted post {PostId} due to report {ReportId}", post.MaTinDang, report.MaBaoCao);
                return Ok(new { message = "Tin đăng đã bị xóa hoàn toàn và báo cáo đã được xử lý.", notificationCreated = notif != null });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error deleting post for report {ReportId}", id);
                return StatusCode(500, new { message = "Lỗi khi xóa tin đăng.", detail = ex.Message });
            }
        }

        // Admin: ban the user who owns the reported post
        [HttpPost("{id}/ban-user")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BanUserFromReportedPost(int id, [FromQuery] int days = 30)
        {
            var report = await _context.Reports.FindAsync(id);
            if (report == null) return NotFound();

            if (report.TargetType != ReportTargetType.Post)
                return BadRequest(new { message = "Target không phải là tin đăng." });

            var post = await _context.TinDangs.FirstOrDefaultAsync(t => t.MaTinDang == report.TargetId);
            if (post == null) return NotFound(new { message = "Không tìm thấy tin đăng." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == post.MaNguoiBan);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            try
            {
                // Use Identity's LockoutEnd to effectively ban the user for 'days'
                user.LockoutEnd = DateTimeOffset.UtcNow.AddDays(days);
                user.LockoutEnabled = true;

                // Mark report resolved
                report.IsResolved = true;
                report.ResolvedAt = DateTimeOffset.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin banned user {UserId} for {Days} days due to report {ReportId}", user.Id, days, id);
                return Ok(new { message = $"Người dùng đã bị khoá trong {days} ngày." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error banning user for report {ReportId}", id);
                return StatusCode(500, new { message = "Lỗi khi khoá người dùng.", detail = ex.Message });
            }
        }

        // Admin: warn the seller of the reported post (create a Notification)
        [HttpPost("{id}/warn-seller")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> WarnSellerFromReportedPost(int id)
        {
            var report = await _context.Reports.FindAsync(id);
            if (report == null) return NotFound();

            if (report.TargetType != ReportTargetType.Post)
                return BadRequest(new { message = "Target không phải là tin đăng." });

            var post = await _context.TinDangs.FirstOrDefaultAsync(t => t.MaTinDang == report.TargetId);
            if (post == null) return NotFound(new { message = "Không tìm thấy tin đăng." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == post.MaNguoiBan);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            // Try to create and send notification, but avoid creating duplicate warnings for the same post-owner
            Notification? notif = null;
            try
            {
                var title = "Cảnh báo: Tin đăng của bạn bị báo cáo";
                var url = $"/posts/{post.MaTinDang}";

                // Try to include the most recent unresolved report's reason/details so owner can see context
                string message = "Tin đăng của bạn đã nhận được báo cáo. Vui lòng kiểm tra nội dung và chỉnh sửa để tuân thủ chính sách.";
                try
                {
                    var recentReport = await _context.Reports
                        .Where(r => r.TargetType == ReportTargetType.Post && r.TargetId == post.MaTinDang && !r.IsResolved)
                        .OrderByDescending(r => r.CreatedAt)
                        .FirstOrDefaultAsync();

                    if (recentReport != null)
                    {
                        if (!string.IsNullOrWhiteSpace(recentReport.Reason))
                        {
                            message += $" Lý do: '{recentReport.Reason}'.";
                        }
                        if (!string.IsNullOrWhiteSpace(recentReport.Details))
                        {
                            message += $" Chi tiết: '{recentReport.Details}'.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch recent report for post {PostId} while warning seller", post.MaTinDang);
                }

                // If a recent similar notification already exists for this user & url, skip creating another one.
                var recentExisting = await _context.Notifications
                    .Where(n => n.UserId == user.Id && n.Url == url && n.Title.Contains("Cảnh báo"))
                    .OrderByDescending(n => n.CreatedAt)
                    .FirstOrDefaultAsync();

                if (recentExisting != null && recentExisting.CreatedAt >= DateTimeOffset.UtcNow.AddMinutes(-15))
                {
                    // Use existing notification — do not create a duplicate or broadcast again
                    notif = recentExisting;
                    _logger.LogInformation("Skipping duplicate warn-seller notification for user {UserId}, post {PostId} (existing notification id={NotifId})", user.Id, post.MaTinDang, notif.Id);
                }
                else
                {
                    notif = new Notification
                    {
                        UserId = user.Id,
                        Title = title,
                        Message = message,
                        Url = url,
                        IsRead = false,
                            CreatedAt = DateTimeOffset.UtcNow,
                            IsFromAdmin = true
                    };

                    _context.Notifications.Add(notif);
                    await _context.SaveChangesAsync();

                    // Broadcast to user and admins (best-effort), but avoid double-send if owner is admin
                    try
                    {
                        await _notificationHub.Clients.Group($"user-{user.Id}")
                            .SendAsync("ReceiveNotification", new { id = notif.Id, title = notif.Title, message = notif.Message, url = notif.Url, createdAt = notif.CreatedAt, isFromAdmin = true });

                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to broadcast warn-seller notification for report {ReportId}", report.MaBaoCao);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log but continue: notification creation failed (likely missing table/migration)
                _logger.LogWarning(ex, "Could not create notification for warn-seller (report {ReportId}); continuing to mark report resolved.", report.MaBaoCao);
                notif = null;
            }

            try
            {
                // Mark report resolved regardless of notification success
                report.IsResolved = true;
                report.ResolvedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin warned seller {UserId} for report {ReportId} (notificationCreated={NotificationCreated})", user.Id, id, notif != null);
                return Ok(new { message = "Đã gửi cảnh báo tới người bán và đánh dấu báo cáo là đã xử lý.", notificationCreated = notif != null });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving report state after warn-seller for report {ReportId}", id);
                return StatusCode(500, new { message = "Lỗi khi cập nhật trạng thái báo cáo.", detail = ex.Message });
            }
        }
    }
}
