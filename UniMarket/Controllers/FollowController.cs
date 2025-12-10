using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Services;

namespace UniMarket.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FollowController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        private readonly IUserNotificationService _notiService;


        public FollowController(ApplicationDbContext context, IUserNotificationService notiService)
        {
            _context = context;
            _notiService = notiService;
        }


        private string? GetUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        // =========================================================================================
        // 1. API TOGGLE FOLLOW (Đã sửa lỗi so sánh Enum)
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

                // [MỚI] Xóa thông báo cũ khi Unfollow để dọn dẹp Database
                try
                {
                    // SỬA LỖI TẠI ĐÂY: Dùng NotificationType.Follow thay vì "Follow"
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

                // ========================================================================
                // [MỚI] SỬA LỖI SPAM THÔNG BÁO
                // ========================================================================
                try
                {
                    // 1. Tìm thông báo trùng
                    // SỬA LỖI TẠI ĐÂY: Dùng NotificationType.Follow thay vì "Follow"
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
                // ========================================================================
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
        // 2. CÁC API KHÁC (GIỮ NGUYÊN)
        // =========================================================================================
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


        // ✅ Unfollow ai đó (Cũ)
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

        // ✅ Lấy danh sách mình đang follow (Following)
        [HttpGet("following")]
        public async Task<IActionResult> GetFollowing()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var following = await _context.Follows
                .Where(f => f.FollowerId == userId)
                .Include(f => f.Following)
                .Select(f => new
                {
                    f.FollowingId,
                    f.Following.FullName,
                    f.Following.AvatarUrl,
                    f.FollowedAt
                })
                .ToListAsync();

            return Ok(following);
        }

        // ✅ Lấy danh sách ai đang follow mình (Followers)
        [HttpGet("followers")]
        public async Task<IActionResult> GetFollowers()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var followers = await _context.Follows
                .Where(f => f.FollowingId == userId)
                .Include(f => f.Follower)
                .Select(f => new
                {
                    f.FollowerId,
                    f.Follower.FullName,
                    f.Follower.AvatarUrl,
                    f.FollowedAt
                })
                .ToListAsync();

            return Ok(followers);
        }

        // ✅ Lấy danh sách bạn bè (Mutual Follow - 2 người follow nhau)
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

        // ✅ Kiểm tra trạng thái follow cụ thể (cho trang Profile)
        [HttpGet("is-following/{targetUserId}")]
        public async Task<IActionResult> IsFollowing(string targetUserId)
        {
            var userId = GetUserId();
            if (userId == null) return Ok(new { isFollowing = false });

            var isFollowing = await _context.Follows
                .AnyAsync(f => f.FollowerId == userId && f.FollowingId == targetUserId);

            return Ok(new { isFollowing });
        }
    }
}