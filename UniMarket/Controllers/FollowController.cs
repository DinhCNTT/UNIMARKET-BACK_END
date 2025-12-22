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

        // 1. KHAI BÁO SERVICE MỚI (UserAffinityService)
        private readonly IUserAffinityService _affinityService;

        public FollowController(
            ApplicationDbContext context,
            IUserNotificationService notiService,
            UserRecommendationService recommendationService,
            IUserAffinityService affinityService) // 2. INJECT VÀO CONSTRUCTOR
        {
            _context = context;
            _notiService = notiService;
            _recommendationService = recommendationService;
            _affinityService = affinityService;
        }

        private string? GetUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        // =========================================================================================
        // 0. HÀM PHỤ TRỢ (HELPER) - KIỂM TRA QUYỀN XEM NỘI DUNG
        // =========================================================================================

        /// <summary>
        /// Logic: True nếu là chính mình HOẶC tài khoản công khai HOẶC đã follow và được CHẤP NHẬN.
        /// </summary>
        private async Task<bool> CanViewContent(string currentUserId, string targetUserId)
        {
            if (currentUserId == targetUserId) return true;

            var targetUser = await _context.Users.FindAsync(targetUserId);
            if (targetUser == null) return false;

            if (!targetUser.IsPrivateAccount) return true;

            var isAcceptedFollower = await _context.Follows
                .AnyAsync(f => f.FollowingId == targetUserId
                            && f.FollowerId == currentUserId
                            && f.Status == FollowStatus.Accepted);

            return isAcceptedFollower;
        }

        // =========================================================================================
        // *** NEW ***: API DANH SÁCH BẠN BÈ THÔNG MINH (SMART LIST)
        // =========================================================================================

        // API lấy danh sách bạn bè đã sắp xếp (Online > Điểm cao > Mới follow)
        // Thay thế cho API GetFollowing cũ ở màn hình chat/trang chủ
        [HttpGet("following-smart")]
        public async Task<IActionResult> GetSmartFollowingList([FromQuery] int page = 1, [FromQuery] int pageSize = 4) // Mặc định là 4 như yêu cầu
        {
            var currentUserId = GetUserId(); // Hàm lấy ID từ Token của bạn
            if (currentUserId == null) return Unauthorized();

            // Gọi Service thông minh bạn đã viết
            var result = await _affinityService.GetSmartSortedFollowingAsync(currentUserId, page, pageSize);

            return Ok(result);
        }

        // API Ghi nhận tương tác (để thuật toán học dần)
        [HttpPost("interact")]
        public async Task<IActionResult> Interact([FromBody] InteractRequest request)
        {
            var currentUserId = GetUserId();
            if (currentUserId == null) return Unauthorized();

            await _affinityService.TrackInteractionAsync(currentUserId, request.TargetUserId, request.Type);
            return Ok();
        }

        // =========================================================================================
        // 1. API TOGGLE FOLLOW (XỬ LÝ LOGIC RIÊNG TƯ / CÔNG KHAI)
        // =========================================================================================
        [HttpPost("toggle")]
        public async Task<IActionResult> ToggleFollow([FromQuery] string targetUserId)
        {
            var currentUserId = GetUserId();
            if (string.IsNullOrEmpty(currentUserId)) return Unauthorized("Vui lòng đăng nhập.");
            if (currentUserId == targetUserId) return BadRequest("Không thể follow chính mình.");

            var targetUser = await _context.Users.FindAsync(targetUserId);
            if (targetUser == null) return NotFound("Người dùng không tồn tại.");

            // Kiểm tra follow hiện tại
            var existingFollow = await _context.Follows
                .FirstOrDefaultAsync(f => f.FollowerId == currentUserId && f.FollowingId == targetUserId);

            bool isFollowedNow = false;
            bool isPending = false;
            int spamCooldownMinutes = 10; // Chống spam noti

            if (existingFollow != null)
            {
                // --- TRƯỜNG HỢP UNFOLLOW / HỦY YÊU CẦU ---
                _context.Follows.Remove(existingFollow);

                // Logic xóa thông báo nếu chưa quá hạn spam
                bool isSpamAction = existingFollow.FollowedAt > DateTime.UtcNow.AddMinutes(-spamCooldownMinutes);
                if (!isSpamAction)
                {
                    var typeToDelete = (existingFollow.Status == FollowStatus.Pending)
                                            ? NotificationType.FollowRequest
                                            : NotificationType.Follow;
                    try
                    {
                        var oldNoti = await _context.UserNotifications
                            .FirstOrDefaultAsync(n => n.Type == typeToDelete
                                                    && n.SenderId == currentUserId
                                                    && n.ReceiverId == targetUserId);
                        if (oldNoti != null) _context.UserNotifications.Remove(oldNoti);
                    }
                    catch { /* Ignore */ }
                }
            }
            else
            {
                // --- TRƯỜNG HỢP FOLLOW MỚI ---
                var newFollow = new Follow
                {
                    FollowerId = currentUserId,
                    FollowingId = targetUserId,
                    FollowedAt = DateTime.UtcNow
                };

                NotificationType notiType;
                string notiContent;

                if (targetUser.IsPrivateAccount)
                {
                    newFollow.Status = FollowStatus.Pending;
                    isPending = true;
                    isFollowedNow = false;
                    notiType = NotificationType.FollowRequest;
                    notiContent = "đã gửi yêu cầu theo dõi bạn.";
                }
                else
                {
                    newFollow.Status = FollowStatus.Accepted;
                    isFollowedNow = true;
                    isPending = false;
                    notiType = NotificationType.Follow;
                    notiContent = "đã bắt đầu follow bạn.";
                }

                _context.Follows.Add(newFollow);

                // Kiểm tra spam noti trước khi tạo
                var existingNoti = await _context.UserNotifications
                    .AnyAsync(n => n.SenderId == currentUserId
                                && n.ReceiverId == targetUserId
                                && n.Type == notiType);

                if (!existingNoti)
                {
                    await _notiService.CreateNotification(
                        senderId: currentUserId,
                        receiverId: targetUserId,
                        type: notiType,
                        refId: null,
                        content: notiContent
                    );
                }
            }

            await _context.SaveChangesAsync();

            // --- QUAN TRỌNG: ĐẾM LẠI ---
            var newFollowerCount = await _context.Follows
                .CountAsync(f => f.FollowingId == targetUserId && f.Status == FollowStatus.Accepted);

            return Ok(new
            {
                success = true,
                isFollowed = isFollowedNow,
                isPending = isPending,
                newFollowerCount = newFollowerCount
            });
        }

        // =========================================================================================
        // 2. API XỬ LÝ YÊU CẦU THEO DÕI (DÀNH CHO CHỦ TÀI KHOẢN RIÊNG TƯ)
        // =========================================================================================

        // Chấp nhận yêu cầu (Accept)
        [HttpPost("accept-request")]
        public async Task<IActionResult> AcceptRequest([FromQuery] string requesterId)
        {
            var currentUserId = GetUserId(); // Tôi (người được follow)

            var followRequest = await _context.Follows
                .FirstOrDefaultAsync(f => f.FollowerId == requesterId
                                    && f.FollowingId == currentUserId
                                    && f.Status == FollowStatus.Pending);

            if (followRequest == null)
                return NotFound("Yêu cầu không tồn tại hoặc đã được xử lý.");

            // 1. Cập nhật trạng thái thành Accepted
            followRequest.Status = FollowStatus.Accepted;
            followRequest.FollowedAt = DateTime.UtcNow;

            // 2. Gửi thông báo cho người yêu cầu biết
            await _notiService.CreateNotification(
                senderId: currentUserId,
                receiverId: requesterId,
                type: NotificationType.FollowAccepted,
                refId: null,
                content: "đã chấp nhận yêu cầu theo dõi của bạn."
            );

            // 3. Đánh dấu thông báo cũ là đã đọc
            var requestNoti = await _context.UserNotifications
                .FirstOrDefaultAsync(n => n.SenderId == requesterId
                                    && n.ReceiverId == currentUserId
                                    && n.Type == NotificationType.FollowRequest);
            if (requestNoti != null)
            {
                requestNoti.IsRead = true;
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Đã chấp nhận yêu cầu." });
        }

        // Từ chối yêu cầu (Decline)
        [HttpPost("decline-request")]
        public async Task<IActionResult> DeclineRequest([FromQuery] string requesterId)
        {
            var currentUserId = GetUserId();

            var followRequest = await _context.Follows
                .FirstOrDefaultAsync(f => f.FollowerId == requesterId
                                    && f.FollowingId == currentUserId
                                    && f.Status == FollowStatus.Pending);

            if (followRequest != null)
            {
                _context.Follows.Remove(followRequest);

                var noti = await _context.UserNotifications
                    .FirstOrDefaultAsync(n => n.SenderId == requesterId
                                           && n.ReceiverId == currentUserId
                                           && n.Type == NotificationType.FollowRequest);
                if (noti != null) _context.UserNotifications.Remove(noti);

                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, message = "Đã xóa yêu cầu." });
        }

        // =========================================================================================
        // 3. API LẤY DANH SÁCH (CÓ CHECK QUYỀN + LỌC TRẠNG THÁI ACCEPTED)
        // =========================================================================================

        [HttpGet("following")]
        public async Task<IActionResult> GetFollowing([FromQuery] string? targetUserId)
        {
            var currentUserId = GetUserId();
            if (currentUserId == null) return Unauthorized();

            var idToCheck = string.IsNullOrEmpty(targetUserId) ? currentUserId : targetUserId;

            var canView = await CanViewContent(currentUserId, idToCheck);
            if (!canView)
            {
                return StatusCode(403, "Tài khoản này là riêng tư. Bạn cần Follow để xem danh sách.");
            }

            var following = await _context.Follows
                .Where(f => f.FollowerId == idToCheck && f.Status == FollowStatus.Accepted)
                .Include(f => f.Following)
                .Select(f => new
                {
                    f.FollowingId,
                    Id = f.Following.Id,
                    f.Following.FullName,
                    f.Following.AvatarUrl,
                    f.Following.UserName,
                    f.FollowedAt,
                    IsFollowed = _context.Follows.Any(x => x.FollowerId == currentUserId
                                                        && x.FollowingId == f.FollowingId
                                                        && x.Status == FollowStatus.Accepted)
                })
                .ToListAsync();

            return Ok(following);
        }

        [HttpGet("followers")]
        public async Task<IActionResult> GetFollowers([FromQuery] string? targetUserId)
        {
            var currentUserId = GetUserId();
            if (currentUserId == null) return Unauthorized();

            var idToCheck = string.IsNullOrEmpty(targetUserId) ? currentUserId : targetUserId;

            var canView = await CanViewContent(currentUserId, idToCheck);
            if (!canView)
            {
                return StatusCode(403, "Tài khoản này là riêng tư. Bạn cần Follow để xem danh sách.");
            }

            var followers = await _context.Follows
                .Where(f => f.FollowingId == idToCheck && f.Status == FollowStatus.Accepted)
                .Include(f => f.Follower)
                .Select(f => new
                {
                    f.FollowerId,
                    Id = f.Follower.Id,
                    f.Follower.FullName,
                    f.Follower.AvatarUrl,
                    f.Follower.UserName,
                    f.FollowedAt,
                    IsFollowed = _context.Follows.Any(x => x.FollowerId == currentUserId
                                                        && x.FollowingId == f.FollowerId
                                                        && x.Status == FollowStatus.Accepted)
                })
                .ToListAsync();

            return Ok(followers);
        }

        // =========================================================================================
        // 4. API ĐỀ XUẤT & TIỆN ÍCH KHÁC
        // =========================================================================================

        [HttpGet("suggested")]
        public async Task<IActionResult> GetSuggestedUsers([FromQuery] string? targetUserId)
        {
            var currentUserId = GetUserId();
            if (currentUserId == null) return Unauthorized();

            var idToAnalyze = string.IsNullOrEmpty(targetUserId) ? currentUserId : targetUserId;

            try
            {
                var suggestions = await _recommendationService.GetSuggestedUsersAsync(idToAnalyze);

                var myRelationshipIds = await _context.Follows
                    .AsNoTracking()
                    .Where(f => f.FollowerId == currentUserId)
                    .Select(f => f.FollowingId)
                    .ToListAsync();

                foreach (var user in suggestions)
                {
                    user.IsFollowed = myRelationshipIds.Contains(user.Id);
                }

                return Ok(suggestions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi server: " + ex.Message);
            }
        }

        [HttpGet("is-following/{targetUserId}")]
        public async Task<IActionResult> IsFollowing(string targetUserId)
        {
            var userId = GetUserId();
            if (userId == null) return Ok(new { isFollowing = false, isPending = false });

            var followRecord = await _context.Follows
                .FirstOrDefaultAsync(f => f.FollowerId == userId && f.FollowingId == targetUserId);

            if (followRecord == null)
            {
                return Ok(new { isFollowing = false, isPending = false });
            }

            return Ok(new
            {
                isFollowing = followRecord.Status == FollowStatus.Accepted,
                isPending = followRecord.Status == FollowStatus.Pending
            });
        }

        [HttpGet("mutual")]
        public async Task<IActionResult> GetMutualFollows()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var myFollowingIds = await _context.Follows
                .Where(f => f.FollowerId == userId && f.Status == FollowStatus.Accepted)
                .Select(f => f.FollowingId)
                .ToListAsync();

            var mutualFriends = await _context.Follows
                .Where(f => f.FollowingId == userId
                         && f.Status == FollowStatus.Accepted
                         && myFollowingIds.Contains(f.FollowerId))
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

        // =========================================================================================
        // 5. API LEGACY
        // =========================================================================================

        [HttpPost("follow")]
        public async Task<IActionResult> FollowUser([FromQuery] string followingId)
        {
            return await ToggleFollow(followingId);
        }

        [HttpPost("unfollow")]
        public async Task<IActionResult> UnfollowUser([FromQuery] string followingId)
        {
            return await ToggleFollow(followingId);
        }
    }

    // Class Request Body cho API Interact
    public class InteractRequest
    {
        public string TargetUserId { get; set; }
        public InteractionType Type { get; set; }
    }
}