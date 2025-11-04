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
    [Authorize] // ✅ bắt buộc đăng nhập mới được gọi API
    public class FollowController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public FollowController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 📌 Lấy UserId từ Claims (Identity)
        private string? GetUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        // ✅ Follow ai đó (query string)
        // POST: /api/follow/follow?followingId=xxx
        [HttpPost("follow")]
        public async Task<IActionResult> FollowUser([FromQuery] string followingId)
        {
            var followerId = GetUserId();
            if (followerId == null)
                return Unauthorized("Bạn cần đăng nhập để follow.");

            if (followerId == followingId)
                return BadRequest("Không thể tự follow chính mình.");

            var exists = await _context.Follows
                .AnyAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);

            if (exists)
                return BadRequest("Bạn đã follow người này rồi.");

            var follow = new Follow
            {
                FollowerId = followerId,
                FollowingId = followingId
            };

            _context.Follows.Add(follow);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Follow thành công" });
        }

        // ✅ Unfollow ai đó (query string)
        // POST: /api/follow/unfollow?followingId=xxx
        [HttpPost("unfollow")]
        public async Task<IActionResult> UnfollowUser([FromQuery] string followingId)
        {
            var followerId = GetUserId();
            if (followerId == null)
                return Unauthorized("Bạn cần đăng nhập để unfollow.");

            var follow = await _context.Follows
                .FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);

            if (follow == null)
                return NotFound("Bạn chưa follow người này.");

            _context.Follows.Remove(follow);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Unfollow thành công" });
        }

        // ✅ Lấy danh sách mình đang follow
        [HttpGet("following")]
        public async Task<IActionResult> GetFollowing()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var following = await _context.Follows
                .Where(f => f.FollowerId == userId)
                .Include(f => f.Following)
                .ToListAsync();

            return Ok(following);
        }

        // ✅ Lấy danh sách ai đang follow mình
        [HttpGet("followers")]
        public async Task<IActionResult> GetFollowers()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var followers = await _context.Follows
                .Where(f => f.FollowingId == userId)
                .Include(f => f.Follower)
                .ToListAsync();

            return Ok(followers);
        }

        // ✅ Lấy danh sách mutual (hai người follow nhau)
        [HttpGet("mutual")]
        public async Task<IActionResult> GetMutualFollows()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var following = await _context.Follows
                .Where(f => f.FollowerId == userId)
                .Select(f => f.FollowingId)
                .ToListAsync();

            var followers = await _context.Follows
                .Where(f => f.FollowingId == userId)
                .Select(f => f.FollowerId)
                .ToListAsync();

            var mutual = following.Intersect(followers).ToList();

            return Ok(mutual);
        }

        // ✅ Kiểm tra đã follow người nào chưa
        [HttpGet("is-following/{targetUserId}")]
        public async Task<IActionResult> IsFollowing(string targetUserId)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var isFollowing = await _context.Follows
                .AnyAsync(f => f.FollowerId == userId && f.FollowingId == targetUserId);

            return Ok(new { isFollowing });
        }
    }
}
