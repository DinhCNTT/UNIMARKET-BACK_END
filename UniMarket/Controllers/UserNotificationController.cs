using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;
using UniMarket.Services; // Namespace chứa IUserNotificationService

namespace UniMarket.Controllers
{
    // API Route sẽ là: api/usernotification
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Bắt buộc đăng nhập mới gọi được
    public class UserNotificationController : ControllerBase
    {
        // ✅ Inject Service mới (IUserNotificationService)
        private readonly IUserNotificationService _userNotificationService;

        public UserNotificationController(IUserNotificationService userNotificationService)
        {
            _userNotificationService = userNotificationService;
        }

        // 1. Lấy danh sách thông báo (có lọc theo filter)
        // GET: api/usernotification?filter=likes&page=1
        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] string filter = "all", [FromQuery] int page = 1)
        {
            // Lấy ID user từ Token đăng nhập
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Gọi Service để lấy dữ liệu (mặc định lấy 20 tin mỗi trang)
            var result = await _userNotificationService.GetNotifications(userId, filter, page, 20);

            return Ok(result);
        }

        // 2. Đếm số lượng tin chưa đọc (để hiển thị chấm đỏ trên chuông)
        // GET: api/usernotification/unread-count
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var count = await _userNotificationService.GetUnreadCount(userId);

            return Ok(new { count });
        }

        // 3. Đánh dấu đã đọc một thông báo cụ thể
        // POST: api/usernotification/{id}/read
        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            await _userNotificationService.MarkAsRead(id);
            return Ok(new { message = "Đã đánh dấu đã đọc" });
        }
    }
}