using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Services;
using UniMarket.Services.Recommendation;

namespace UniMarket.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FollowController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserNotificationService _notiService;
        private readonly UserRecommendationService _recommendationService;

        public FollowController(
            ApplicationDbContext context,
            IUserNotificationService notiService,
            UserRecommendationService recommendationService)
        {
            _context = context;
            _notiService = notiService;
            _recommendationService = recommendationService;
        }

        private string? GetUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        // =========================================================================================
        // 1. API TOGGLE FOLLOW (Logic: Follow/Unfollow & Thông báo)
        // =========================================================================================
        [HttpPost("toggle")]
        public async Task<IActionResult> ToggleFollow([FromQuery] string targetUserId)
        {
            // Lấy UserID hiện tại
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized("Vui lòng đăng nhập để thực hiện chức năng này.");

            if (currentUserId == targetUserId)
                return BadRequest("Không thể follow chính mình.");

            // Kiểm tra xem user đích có tồn tại không
            var targetUserExists = await _context.Users.AnyAsync(u => u.Id == targetUserId);
            if (!targetUserExists)
                return NotFound("Người dùng không tồn tại.");

            // Kiểm tra trạng thái follow hiện tại
            var existingFollow = await _context.Follows
                .FirstOrDefaultAsync(f => f.FollowerId == currentUserId && f.FollowingId == targetUserId);

            bool isFollowedNow;

            if (existingFollow != null)
            {
                // --- TRƯỜNG HỢP 1: ĐANG FOLLOW -> HỦY FOLLOW (UNFOLLOW) ---
                _context.Follows.Remove(existingFollow);
                isFollowedNow = false;

                // Xóa thông báo cũ khi Unfollow để dọn dẹp Database
                try
                {
                    var oldNoti = await _context.UserNotifications
                        .FirstOrDefaultAsync(n => n.Type == NotificationType.Follow
                                               && n.SenderId == currentUserId
                                               && n.ReceiverId == targetUserId);
                    if (oldNoti != null)
                    {
                        _context.UserNotifications.Remove(oldNoti);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi xóa thông báo cũ: {ex.Message}");
                }
            }
            else
            {
                // --- TRƯỜNG HỢP 2: CHƯA FOLLOW -> THỰC HIỆN FOLLOW ---
                var newFollow = new Follow
                {
                    FollowerId = currentUserId,
                    FollowingId = targetUserId,
                    FollowedAt = DateTime.UtcNow
                };
                _context.Follows.Add(newFollow);
                isFollowedNow = true;

                // Gửi thông báo (Tránh Spam)
                try
                {
                    // 1. Tìm thông báo trùng
                    var duplicateNoti = await _context.UserNotifications
                        .FirstOrDefaultAsync(n => n.Type == NotificationType.Follow
                                               && n.SenderId == currentUserId
                                               && n.ReceiverId == targetUserId);

                    // 2. Nếu có rồi -> Xóa nó đi trước khi tạo cái mới
                    if (duplicateNoti != null)
                    {
                        _context.UserNotifications.Remove(duplicateNoti);
                        await _context.SaveChangesAsync();
                    }

                    // 3. Tạo thông báo mới
                    await _notiService.CreateNotification(
                        senderId: currentUserId,
                        receiverId: targetUserId,
                        type: NotificationType.Follow,
                        refId: null,
                        content: "đã bắt đầu follow bạn"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi xử lý thông báo follow: {ex.Message}");
                }
            }

            // Lưu tất cả thay đổi (Follow/Unfollow) vào DB
            await _context.SaveChangesAsync();

            // Đếm lại tổng số follower
            var newFollowerCount = await _context.Follows.CountAsync(f => f.FollowingId == targetUserId);

            return Ok(new
            {
                success = true,
                isFollowed = isFollowedNow,
                newFollowerCount = newFollowerCount
            });
        }

        // =========================================================================================
        // 2. API LẤY DANH SÁCH (ĐÃ SỬA: Hỗ trợ xem của người khác bằng targetUserId)
        // =========================================================================================

        // ✅ Lấy danh sách mình/người khác đang follow (Following)
        [HttpGet("following")]
        public async Task<IActionResult> GetFollowing([FromQuery] string? targetUserId)
        {
            var currentUserId = GetUserId(); // Người đang thực hiện hành động xem (VD: Bạn)
            if (currentUserId == null) return Unauthorized();

            // Người mà chúng ta đang xem danh sách của họ
            var idToCheck = string.IsNullOrEmpty(targetUserId) ? currentUserId : targetUserId;

            var following = await _context.Follows
                .Where(f => f.FollowerId == idToCheck)
                .Include(f => f.Following)
                .Select(f => new
                {
                    f.FollowingId,
                    f.Following.FullName,
                    f.Following.AvatarUrl,
                    f.Following.UserName,
                    f.FollowedAt,
                    // LOGIC MỚI: Kiểm tra xem 'currentUserId' (Bạn) có đang follow người này (f.FollowingId) không?
                    IsFollowed = _context.Follows.Any(x => x.FollowerId == currentUserId && x.FollowingId == f.FollowingId)
                })
                .ToListAsync();

            return Ok(following);
        }

        // ✅ Lấy danh sách ai đang follow mình/người khác (Followers)
        [HttpGet("followers")]
        public async Task<IActionResult> GetFollowers([FromQuery] string? targetUserId)
        {
            var currentUserId = GetUserId(); // Người đang thực hiện hành động xem (VD: Bạn)
            if (currentUserId == null) return Unauthorized();

            var idToCheck = string.IsNullOrEmpty(targetUserId) ? currentUserId : targetUserId;

            var followers = await _context.Follows
                .Where(f => f.FollowingId == idToCheck)
                .Include(f => f.Follower)
                .Select(f => new
                {
                    f.FollowerId,
                    f.Follower.FullName,
                    f.Follower.AvatarUrl,
                    f.Follower.UserName,
                    f.FollowedAt,
                    // LOGIC MỚI: Kiểm tra xem 'currentUserId' (Bạn) có đang follow người này (f.FollowerId) không?
                    IsFollowed = _context.Follows.Any(x => x.FollowerId == currentUserId && x.FollowingId == f.FollowerId)
                })
                .ToListAsync();

            return Ok(followers);
        }

        // ✅ API Đề xuất (Suggested) - Hỗ trợ ngữ cảnh Profile
        [HttpGet("suggested")]
        public async Task<IActionResult> GetSuggestedUsers([FromQuery] string? targetUserId)
        {
            var currentUserId = GetUserId();
            if (currentUserId == null) return Unauthorized();

            // 1. Xác định xem đang lấy đề xuất dựa trên ID nào
            // (Nếu xem profile người khác thì lấy đề xuất liên quan người đó, nếu xem chính mình thì lấy cho mình)
            var idToAnalyze = string.IsNullOrEmpty(targetUserId) ? currentUserId : targetUserId;

            try
            {
                // 2. Lấy danh sách thô từ Service
                var suggestions = await _recommendationService.GetSuggestedUsersAsync(idToAnalyze);

                // =================================================================================
                // ✅ BƯỚC QUAN TRỌNG: CHECK LẠI TRẠNG THÁI FOLLOW CỦA "TÔI" (CurrentUserId)
                // =================================================================================

                // Lấy danh sách những người mà TÔI đang follow
                var myFollowingIds = await _context.Follows
                    .AsNoTracking()
                    .Where(f => f.FollowerId == currentUserId)
                    .Select(f => f.FollowingId)
                    .ToListAsync();

                // Duyệt qua danh sách đề xuất và cập nhật trạng thái IsFollowed
                foreach (var user in suggestions)
                {
                    // Nếu ID của user đề xuất nằm trong danh sách tôi đang follow -> IsFollowed = true
                    if (myFollowingIds.Contains(user.Id))
                    {
                        user.IsFollowed = true;
                    }
                    else
                    {
                        user.IsFollowed = false;
                    }
                }

                // (Tùy chọn) Nếu bạn muốn ẩn luôn những người đã Follow khỏi mục đề xuất:
                // suggestions = suggestions.Where(u => !u.IsFollowed).ToList();

                return Ok(suggestions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi server: " + ex.Message);
            }
        }

        // =========================================================================================
        // 3. CÁC API KHÁC (Legacy / Utility)
        // =========================================================================================

        // ✅ Kiểm tra trạng thái follow cụ thể (cho trang Profile để hiện nút Follow/Following)
        [HttpGet("is-following/{targetUserId}")]
        public async Task<IActionResult> IsFollowing(string targetUserId)
        {
            var userId = GetUserId();
            if (userId == null) return Ok(new { isFollowing = false });

            var isFollowing = await _context.Follows
                .AnyAsync(f => f.FollowerId == userId && f.FollowingId == targetUserId);

            return Ok(new { isFollowing });
        }

        // ✅ Lấy danh sách bạn bè (Mutual Follow - 2 người follow nhau - Của bản thân)
        [HttpGet("mutual")]
        public async Task<IActionResult> GetMutualFollows()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var myFollowingIds = await _context.Follows
                .Where(f => f.FollowerId == userId)
                .Select(f => f.FollowingId)
                .ToListAsync();

            var mutualFriends = await _context.Follows
                .Where(f => f.FollowingId == userId && myFollowingIds.Contains(f.FollowerId))
                .Include(f => f.Follower)
                .Select(f => new
                {
                    UserId = f.FollowerId,
                    f.Follower.FullName,
                    f.Follower.AvatarUrl
                })
                .ToListAsync();

            return Ok(mutualFriends);
        }

        // ✅ API Follow User (Legacy - Dùng cho các nút đơn lẻ cũ nếu còn)
        [HttpPost("follow")]
        public async Task<IActionResult> FollowUser([FromQuery] string followingId)
        {
            var followerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(followerId)) return Unauthorized();
            if (followerId == followingId) return BadRequest("Không thể tự follow.");

            var exists = await _context.Follows.AnyAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);
            if (exists) return BadRequest("Đã follow rồi.");

            _context.Follows.Add(new Follow { FollowerId = followerId, FollowingId = followingId, FollowedAt = DateTime.UtcNow });

            try
            {
                await _notiService.CreateNotification(
                    senderId: followerId,
                    receiverId: followingId,
                    type: NotificationType.Follow,
                    refId: null,
                    content: "đã bắt đầu follow bạn"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi gửi thông báo (Legacy API): {ex.Message}");
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Follow thành công" });
        }

        // ✅ API Unfollow User (Legacy)
        [HttpPost("unfollow")]
        public async Task<IActionResult> UnfollowUser([FromQuery] string followingId)
        {
            var followerId = GetUserId();
            if (followerId == null) return Unauthorized();

            var follow = await _context.Follows.FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);
            if (follow == null) return NotFound("Chưa follow người này.");

            _context.Follows.Remove(follow);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Unfollow thành công" });
        }
    }
}