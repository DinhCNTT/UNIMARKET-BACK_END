using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using UniMarket.Models;
using System.Security.Claims;
using UniMarket.DTO;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserPresenceService _presenceService;

    public UserController(UserManager<ApplicationUser> userManager, UserPresenceService presenceService)
    {
        _userManager = userManager;
        _presenceService = presenceService;
    }

    // ✅ ENDPOINT MỚI - KHÔNG CẦN ĐĂNG NHẬP (Public)
    [HttpGet("public/status/{userId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicStatus(string userId)
    {
        // First check in-memory service for most up-to-date status
        var memoryStatus = _presenceService.GetStatus(userId);

        // Also get database info for fallback
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        bool isOnline;
        DateTime? lastActive;

        if (memoryStatus.HasValue)
        {
            // Use in-memory data if available (more current)
            isOnline = memoryStatus.Value.IsOnline;
            lastActive = memoryStatus.Value.IsOnline ? null : memoryStatus.Value.LastActive;
        }
        else
        {
            // Fallback to database
            isOnline = user.IsOnline;
            lastActive = user.IsOnline ? null : user.LastOnlineTime;
        }

        return Ok(new
        {
            isOnline = isOnline,
            lastActive = lastActive,
            formattedLastSeen = FormatLastSeen(lastActive)
        });
    }

    // ✅ GIỮ NGUYÊN ENDPOINT CŨ CHO USER ĐÃ ĐĂNG NHẬP
    [HttpGet("status/{userId}")]
    [Authorize]
    public async Task<IActionResult> GetStatus(string userId)
    {
        // First check in-memory service for most up-to-date status
        var memoryStatus = _presenceService.GetStatus(userId);

        // Also get database info for fallback
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        bool isOnline;
        DateTime? lastActive;

        if (memoryStatus.HasValue)
        {
            // Use in-memory data if available (more current)
            isOnline = memoryStatus.Value.IsOnline;
            lastActive = memoryStatus.Value.IsOnline ? null : memoryStatus.Value.LastActive;
        }
        else
        {
            // Fallback to database
            isOnline = user.IsOnline;
            lastActive = user.IsOnline ? null : user.LastOnlineTime;
        }

        return Ok(new
        {
            isOnline = isOnline,
            lastActive = lastActive,
            formattedLastSeen = FormatLastSeen(lastActive)
        });
    }

    public static string FormatLastSeen(DateTime? lastActive)
    {
        if (!lastActive.HasValue) return null;

        var timeAgo = DateTime.UtcNow - lastActive.Value;

        if (timeAgo.TotalMinutes < 1)
            return "vừa mới";
        else if (timeAgo.TotalMinutes < 60)
            return $"{(int)timeAgo.TotalMinutes} phút trước";
        else if (timeAgo.TotalHours < 24)
            return $"{(int)timeAgo.TotalHours} giờ trước";
        else
            return $"{(int)timeAgo.TotalDays} ngày trước";
    }

    [HttpGet("profile/{userId}")]
    [Authorize]
    public async Task<IActionResult> GetUserProfile(string userId)
    {
        // Kiểm tra user có quyền truy cập (chỉ được xem profile của chính mình)
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (currentUserId != userId)
        {
            return StatusCode(403, new { message = "Bạn không có quyền truy cập thông tin này." });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            fullName = user.FullName,
            phoneNumber = user.PhoneNumber,
            role = roles.FirstOrDefault() ?? "User",
            avatarUrl = user.AvatarUrl,
            emailConfirmed = user.EmailConfirmed
        });
    }

    [HttpPut("update-profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileModel model)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(currentUserId);

        if (user == null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        // Cập nhật thông tin
        if (!string.IsNullOrEmpty(model.FullName))
        {
            user.FullName = model.FullName;
        }

        if (!string.IsNullOrEmpty(model.PhoneNumber))
        {
            user.PhoneNumber = model.PhoneNumber;
        }

        if (!string.IsNullOrEmpty(model.AvatarUrl))
        {
            user.AvatarUrl = model.AvatarUrl;
        }

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        // Trả về thông tin user đã cập nhật
        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            fullName = user.FullName,
            phoneNumber = user.PhoneNumber,
            role = roles.FirstOrDefault() ?? "User",
            avatarUrl = user.AvatarUrl,
            emailConfirmed = user.EmailConfirmed,
            message = "Cập nhật thông tin thành công!"
        });
    }

    public class UpdateProfileModel
    {
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? AvatarUrl { get; set; }
    }
}