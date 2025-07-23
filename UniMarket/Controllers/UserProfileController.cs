using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SqlServer.Server;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;
using UniMarket.Services;


namespace UniMarket.Controllers
{
    [ApiController]
    [Route("api/userprofile")]
    [Authorize] // Áp dụng cho toàn bộ controller
    public class UserProfileController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly PhotoService _photoService;

        public UserProfileController(UserManager<ApplicationUser> userManager, ApplicationDbContext context, PhotoService photoService)
        {
            _userManager = userManager;
            _context = context;
            _photoService = photoService;
        }
        // DTO: Cập nhật avatar
        public class UpdateAvatarModel
        {
            public string AvatarUrl { get; set; }
        }

        // DTO: Dữ liệu trả về khi gọi GET /me
        public class UserProfileDTO
        {
            public string UserName { get; set; }
            public string Email { get; set; }
            public bool EmailConfirmed { get; set; }
            public string PhoneNumber { get; set; }
            public string FullName { get; set; }
            public bool CanChangeEmail { get; set; } // ✅ mới thêm
            public string? AvatarUrl { get; set; }
        }

        // DTO: Cập nhật thông tin cá nhân
        public class UpdateProfileModel
        {
            public string FullName { get; set; }
            public string PhoneNumber { get; set; }
        }

        // DTO: Cập nhật email
        public class UpdateEmailModel
        {
            public string NewEmail { get; set; }
        }

        // DTO: Đổi mật khẩu
        public class ChangePasswordModel
        {
            public string? CurrentPassword { get; set; }
            public string NewPassword { get; set; } = string.Empty;
            public string ConfirmNewPassword { get; set; } = string.Empty;
        }


        [HttpGet("me")]
        public async Task<IActionResult> GetUserProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "UserId claim not found in token." });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "Người dùng không tồn tại." });
            }

            var profile = new UserProfileDTO
            {
                UserName = user.UserName,
                Email = user.Email,
                EmailConfirmed = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                FullName = user.FullName,
                CanChangeEmail = !user.EmailConfirmed,
                AvatarUrl = user.AvatarUrl
            };

            return Ok(profile);
        }


        // PUT: api/userprofile/update
        [HttpPut("update")]
        public async Task<IActionResult> UpdateUserProfile([FromBody] UpdateProfileModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "User is not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            user.FullName = model.FullName;
            user.PhoneNumber = model.PhoneNumber;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(new { message = "Failed to update profile.", errors = result.Errors });

            return Ok(new { message = "Profile updated successfully." });
        }

        // PUT: api/userprofile/email
        [HttpPut("email")]
        public async Task<IActionResult> UpdateEmail([FromBody] UpdateEmailModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            // Kiểm tra nếu email mới giống email hiện tại
            if (string.Equals(user.Email, model.NewEmail, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Email mới giống với email hiện tại." });
            }

            // ✅ Kiểm tra email đã được dùng bởi user khác chưa
            var existingUser = await _userManager.FindByEmailAsync(model.NewEmail);
            if (existingUser != null && existingUser.Id != user.Id)
            {
                return BadRequest(new { message = "Email này đã được sử dụng bởi người dùng khác." });
            }

            // Không cho đổi nếu email cũ đã xác minh và đang cố đổi thành email khác
            if (user.EmailConfirmed)
            {
                return BadRequest(new { message = "Email hiện tại đã được xác minh, không thể thay đổi." });
            }

            // Cập nhật email và đặt lại trạng thái xác minh
            user.Email = model.NewEmail;
            user.NormalizedEmail = _userManager.NormalizeEmail(model.NewEmail);
            user.EmailConfirmed = false;

            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                return BadRequest(new { message = "Cập nhật email thất bại", errors = result.Errors });
            }

            return Ok(new { message = "✅ Email đã được cập nhật. Vui lòng xác minh." });
        }




        [HttpPut("password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordModel model)
        {
            if (model.NewPassword != model.ConfirmNewPassword)
                return BadRequest(new { message = "Mật khẩu mới và xác nhận không khớp." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });

            var hasPassword = await _userManager.HasPasswordAsync(user);

            IdentityResult result;
            if (hasPassword)
            {
                // Trường hợp user đã có mật khẩu → bắt buộc nhập mật khẩu hiện tại
                if (string.IsNullOrWhiteSpace(model.CurrentPassword))
                    return BadRequest(new { message = "Vui lòng nhập mật khẩu hiện tại." });

                result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            }
            else
            {
                // Trường hợp user login bằng Google chưa có mật khẩu → tạo mới
                result = await _userManager.AddPasswordAsync(user, model.NewPassword);
            }

            if (!result.Succeeded)
                return BadRequest(new { message = "Không thể cập nhật mật khẩu.", errors = result.Errors });

            return Ok(new { message = "Mật khẩu đã được cập nhật thành công." });
        }
        // GET: api/userprofile/has-password
        [HttpGet("has-password")]
        public async Task<IActionResult> HasPassword()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { hasPassword = false });

            var hasPassword = await _userManager.HasPasswordAsync(user);
            return Ok(new { hasPassword });
        }

        // DELETE: api/userprofile/delete
        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteAccount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "User is not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            var result = await _userManager.DeleteAsync(user);

            if (!result.Succeeded)
                return BadRequest(new { message = "Không thể xóa tài khoản.", errors = result.Errors });

            return Ok(new { message = "Tài khoản đã được xóa thành công." });
        }

        // PUT: api/userprofile/update-avatar
        [HttpPut("update-avatar")]
        public async Task<IActionResult> UpdateAvatar([FromBody] UpdateAvatarModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "User is not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            user.AvatarUrl = model.AvatarUrl;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(new { message = "Failed to update avatar.", errors = result.Errors });

            return Ok(new { message = "Avatar updated successfully.", avatarUrl = user.AvatarUrl });
        }

        [HttpPost("upload-avatar")]
        public async Task<IActionResult> UploadAvatar([FromForm] IFormFile avatar)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "User is not authenticated." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            if (avatar == null || avatar.Length == 0)
                return BadRequest(new { message = "No file uploaded." });

            try
            {
                // ✅ Nếu user có avatar cũ, xóa trên Cloudinary trước
                if (!string.IsNullOrEmpty(user.AvatarUrl))
                {
                    var deleteResult = await _photoService.DeleteMediaByUrlAsync(user.AvatarUrl);

                    if (!deleteResult)
                    {
                        // Không cần return lỗi, chỉ log cảnh báo thôi
                        Console.WriteLine("⚠️ Không thể xóa avatar cũ từ Cloudinary.");
                    }
                }

                // ✅ Upload ảnh mới
                var uploadResult = await _photoService.UploadFileToCloudinaryAsync(avatar, "avatars");

                if (uploadResult.Error != null)
                {
                    return BadRequest(new { message = "Upload thất bại", error = uploadResult.Error.Message });
                }

                // ✅ Cập nhật AvatarUrl
                user.AvatarUrl = uploadResult.SecureUrl.ToString();
                await _userManager.UpdateAsync(user);

                return Ok(new
                {
                    message = "✅ Ảnh đại diện đã được cập nhật!",
                    avatarUrl = user.AvatarUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi upload ảnh đại diện", error = ex.Message });
            }
        }
        [HttpGet("user-posts/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUserPosts(string userId)
        {
            var posts = await _context.TinDangs
                .Where(t => t.MaNguoiBan == userId && t.TrangThai == TrangThaiTinDang.DaDuyet)
                .Select(t => new UserPostDto
                {
                    MaTinDang = t.MaTinDang,
                    TieuDe = t.TieuDe,
                    Gia = t.Gia,
                    VideoUrl = t.VideoUrl,
                    DiaChi = t.DiaChi,
                    NgayDang = t.NgayDang,
                    TinhTrang = t.TinhTrang,
                    AnhDuongDans = t.AnhTinDangs.Select(a => a.DuongDan).ToList() // ✅ lấy ảnh
                })
                .ToListAsync();

            return Ok(posts);
        }


        [HttpGet("user-videos/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUserVideos(string userId)
        {
            // Cho phép lấy thông tin kể cả khi chưa đăng nhập
            string? currentUserId = null;

            if (User.Identity?.IsAuthenticated == true)
            {
                currentUserId = _userManager.GetUserId(User);
            }

            var videos = await _context.TinDangs
                .Where(t => t.MaNguoiBan == userId && t.VideoUrl != null && t.TrangThai == TrangThaiTinDang.DaDuyet)
                .Select(t => new UserVideoDto
                {
                    MaTinDang = t.MaTinDang,
                    TieuDe = t.TieuDe,
                    VideoUrl = t.VideoUrl,
                    SoLuongTym = _context.VideoLikes.Count(v => v.MaTinDang == t.MaTinDang),
                    DaTym = currentUserId != null && _context.VideoLikes.Any(v => v.MaTinDang == t.MaTinDang && v.UserId == currentUserId)
                })
                .ToListAsync();

            return Ok(videos);
        }


        [HttpGet("user-info/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUserInfo(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var result = new UserInfoDto
            {
                FullName = user.FullName ?? "",
                AvatarUrl = user.AvatarUrl,
                DaXacMinhEmail = user.EmailConfirmed
            };

            return Ok(result);
        }

    }
}