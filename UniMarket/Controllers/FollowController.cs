using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;

namespace UniMarket.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // ✅ Bắt buộc đăng nhập mới được gọi API
    public class FollowController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public FollowController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 📌 Helper: Lấy UserId từ Claims (Identity)
        private string? GetUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        // =========================================================================================
        // ✅ API MỚI: TOGGLE FOLLOW (Dùng cho giao diện UserRow thông minh)
        // Tự động phát hiện Follow/Unfollow và trả về số liệu mới nhất
        // =========================================================================================
        [HttpPost("toggle")]
        public async Task<IActionResult> ToggleFollow([FromQuery] string targetUserId)
        {
            var currentUserId = GetUserId();
            if (currentUserId == null)
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
                // Đang follow -> Thực hiện Hủy follow (Unfollow)
                _context.Follows.Remove(existingFollow);
                isFollowedNow = false;
            }
            else
            {
                // Chưa follow -> Thực hiện Follow
                var newFollow = new Follow
                {
                    FollowerId = currentUserId,
                    FollowingId = targetUserId,
                    FollowedAt = DateTime.UtcNow
                };
                _context.Follows.Add(newFollow);
                isFollowedNow = true;
            }

            await _context.SaveChangesAsync();

            // 🔥 QUAN TRỌNG: Đếm lại tổng số follower của người kia để trả về cho Client cập nhật UI
            var newFollowerCount = await _context.Follows.CountAsync(f => f.FollowingId == targetUserId);

            return Ok(new
            {
                success = true,
                isFollowed = isFollowedNow,     // Trạng thái mới (true: đang follow, false: chưa)
                newFollowerCount = newFollowerCount // Số lượng follower mới nhất
            });
        }

        // =========================================================================================
        // CÁC API CŨ (Giữ lại để tương thích nếu cần)
        // =========================================================================================

        // ✅ Follow ai đó (Cũ)
        [HttpPost("follow")]
        public async Task<IActionResult> FollowUser([FromQuery] string followingId)
        {
            var followerId = GetUserId();
            if (followerId == null) return Unauthorized();
            if (followerId == followingId) return BadRequest("Không thể tự follow.");

            var exists = await _context.Follows.AnyAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);
            if (exists) return BadRequest("Đã follow rồi.");

            _context.Follows.Add(new Follow { FollowerId = followerId, FollowingId = followingId });
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
                .Include(f => f.Following) // Load thông tin người được follow
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
                .Include(f => f.Follower) // Load thông tin người follow mình
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

            // Lấy list ID những người mình follow
            var myFollowingIds = await _context.Follows
                .Where(f => f.FollowerId == userId)
                .Select(f => f.FollowingId)
                .ToListAsync();

            // Lấy list những người follow mình mà ID nằm trong list trên
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
            // Cho phép check kể cả khi chưa login (trả về false)
            if (userId == null) return Ok(new { isFollowing = false });

            var isFollowing = await _context.Follows
                .AnyAsync(f => f.FollowerId == userId && f.FollowingId == targetUserId);

            return Ok(new { isFollowing });
        }
    }
}