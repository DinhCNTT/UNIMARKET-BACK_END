using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using UniMarket.Models;
using System.Security.Claims;

[Route("api/[controller]")]
[ApiController]
[Authorize] // Yêu cầu authentication
public class UserController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpGet("profile/{userId}")]
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

    // Thêm method tiện ích để update user profile
    [HttpPut("update-profile")]
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