using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Services;
using System.Security.Claims;

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

        // =========================================================================
        // PHẦN 1: CÁC DTO (DATA TRANSFER OBJECTS)
        // =========================================================================

        public class UpdateAvatarModel
        {
            public string AvatarUrl { get; set; }
        }

        public class UserProfileDTO
        {
            public string UserName { get; set; }
            public string Email { get; set; }
            public bool EmailConfirmed { get; set; }
            public string PhoneNumber { get; set; }
            public string FullName { get; set; }
            public bool CanChangeEmail { get; set; }
            public string? AvatarUrl { get; set; }
        }

        public class UserInfoDto
        {
            public string Id { get; set; }
            public string UserName { get; set; }
            public string FullName { get; set; }
            public string AvatarUrl { get; set; }
            public bool DaXacMinhEmail { get; set; }
            public string PhoneNumber { get; set; }
            public int FollowersCount { get; set; }
            public int FollowingCount { get; set; }
        }

        // ✅ DTO MỚI CHO POST
        public class UserPostDto
        {
            public int MaTinDang { get; set; }
            public string TieuDe { get; set; }
            public double Gia { get; set; }       // Đang để double
            public string MoTa { get; set; }
            public string VideoDuongDan { get; set; }
            public string KhuVuc { get; set; }
            public DateTime NgayDang { get; set; }
            public string TinhTrang { get; set; }
            public List<string> AnhDuongDans { get; set; }
            public int SoLuongTym { get; set; }
        }

        // ✅ DTO MỚI CHO VIDEO
        public class UserVideoDto
        {
            public int MaTinDang { get; set; }
            public string TieuDe { get; set; }
            public string VideoDuongDan { get; set; }
            public string AnhBia { get; set; } // Thumbnail
            public int SoLuongTym { get; set; }
            public int Views { get; set; } // Map từ SoLuotXem
            public bool DaTym { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class UpdateProfileModel
        {
            public string FullName { get; set; }
            public string PhoneNumber { get; set; }
        }

        public class UpdateEmailModel
        {
            public string NewEmail { get; set; }
        }

        public class ChangePasswordModel
        {
            public string? CurrentPassword { get; set; }
            public string NewPassword { get; set; } = string.Empty;
            public string ConfirmNewPassword { get; set; } = string.Empty;
        }

        // =========================================================================
        // PHẦN 2: CÁC API QUẢN LÝ TÀI KHOẢN (ACCOUNT MANAGEMENT)
        // =========================================================================

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

        [HttpPut("email")]
        public async Task<IActionResult> UpdateEmail([FromBody] UpdateEmailModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            if (string.Equals(user.Email, model.NewEmail, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Email mới giống với email hiện tại." });
            }

            var existingUser = await _userManager.FindByEmailAsync(model.NewEmail);
            if (existingUser != null && existingUser.Id != user.Id)
            {
                return BadRequest(new { message = "Email này đã được sử dụng bởi người dùng khác." });
            }

            if (user.EmailConfirmed)
            {
                return BadRequest(new { message = "Email hiện tại đã được xác minh, không thể thay đổi." });
            }

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
                if (string.IsNullOrWhiteSpace(model.CurrentPassword))
                    return BadRequest(new { message = "Vui lòng nhập mật khẩu hiện tại." });

                result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            }
            else
            {
                result = await _userManager.AddPasswordAsync(user, model.NewPassword);
            }

            if (!result.Succeeded)
                return BadRequest(new { message = "Không thể cập nhật mật khẩu.", errors = result.Errors });

            return Ok(new { message = "Mật khẩu đã được cập nhật thành công." });
        }

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
                if (!string.IsNullOrEmpty(user.AvatarUrl))
                {
                    var deleteResult = await _photoService.DeleteMediaByUrlAsync(user.AvatarUrl);
                    if (!deleteResult)
                    {
                        Console.WriteLine("⚠️ Không thể xóa avatar cũ từ Cloudinary.");
                    }
                }

                var uploadResult = await _photoService.UploadFileToCloudinaryAsync(avatar, "avatars");

                if (uploadResult.Error != null)
                {
                    return BadRequest(new { message = "Upload thất bại", error = uploadResult.Error.Message });
                }

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

        // =========================================================================
        // PHẦN 3: CÁC API LẤY DỮ LIỆU HIỂN THỊ (QUAN TRỌNG - ĐÃ SỬA)
        // =========================================================================

        // 👇 API 1: LẤY DANH SÁCH TIN ĐĂNG (USER POSTS) - ĐÃ CẬP NHẬT LOGIC
        [HttpGet("user-posts/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUserPosts(string userId)
        {
            var posts = await _context.TinDangs
                .AsNoTracking() // Tối ưu hiệu suất
                .Where(t => t.MaNguoiBan == userId && t.TrangThai == TrangThaiTinDang.DaDuyet)
                .Include(t => t.AnhTinDangs) // ✅ Join lấy ảnh
                .Include(t => t.TinhThanh)   // ✅ Join lấy tên Tỉnh
                .Include(t => t.QuanHuyen)   // ✅ Join lấy tên Huyện
                .OrderByDescending(t => t.NgayDang)
                .Select(t => new UserPostDto
                {
                    MaTinDang = t.MaTinDang,
                    TieuDe = t.TieuDe,

                    // ✅ [FIX LỖI TẠI ĐÂY] Ép kiểu từ decimal sang double
                    Gia = (double)t.Gia,

                    MoTa = t.MoTa,

                    // ✅ Map VideoUrl sang VideoDuongDan
                    VideoDuongDan = t.VideoUrl,

                    // ✅ Xử lý địa chỉ
                    KhuVuc = (t.QuanHuyen != null ? t.QuanHuyen.TenQuanHuyen : "") +
                             (t.QuanHuyen != null && t.TinhThanh != null ? ", " : "") +
                             (t.TinhThanh != null ? t.TinhThanh.TenTinhThanh : ""),

                    NgayDang = t.NgayDang,
                    TinhTrang = t.TinhTrang,

                    // ✅ Xử lý ảnh
                    AnhDuongDans = t.AnhTinDangs
                        .OrderBy(a => a.Order)
                        .Select(a => a.DuongDan.StartsWith("http")
                            ? a.DuongDan
                            : (a.DuongDan.StartsWith("/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}"))
                        .ToList(),

                    SoLuongTym = t.TinDangYeuThichs.Count()
                })
                .ToListAsync();

            return Ok(posts);
        }

        // 👇 API 2: LẤY DANH SÁCH VIDEO (USER VIDEOS) - ĐÃ CẬP NHẬT LOGIC
        [HttpGet("user-videos/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUserVideos(string userId)
        {
            string? currentUserId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                currentUserId = _userManager.GetUserId(User);
            }

            var videos = await _context.TinDangs
                .AsNoTracking()
                .Where(t => t.MaNguoiBan == userId && t.VideoUrl != null && t.TrangThai == TrangThaiTinDang.DaDuyet)
                .Select(t => new UserVideoDto
                {
                    MaTinDang = t.MaTinDang,
                    TieuDe = t.TieuDe,
                    VideoDuongDan = t.VideoUrl,
                    AnhBia = t.AnhTinDangs.OrderBy(a => a.Order).Select(a => a.DuongDan).FirstOrDefault(),

                    // 👇 SỬA LẠI DÒNG NÀY: Truy vấn trực tiếp từ bảng VideoLikes
                    SoLuongTym = _context.VideoLikes.Count(v => v.MaTinDang == t.MaTinDang),

                    Views = t.SoLuotXem,
                    CreatedAt = t.NgayDang,

                    // 👇 SỬA LẠI DÒNG NÀY: Truy vấn trực tiếp từ bảng VideoLikes
                    DaTym = currentUserId != null && _context.VideoLikes.Any(v => v.MaTinDang == t.MaTinDang && v.UserId == currentUserId)
                })
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            return Ok(videos);
        }

        // 👇 API 3: LẤY THÔNG TIN USER (INFO + FOLLOW)
        [HttpGet("user-info/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUserInfo(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var followersCount = await _context.Follows.CountAsync(f => f.FollowingId == userId);
            var followingCount = await _context.Follows.CountAsync(f => f.FollowerId == userId);

            var result = new UserInfoDto
            {
                Id = user.Id,
                UserName = user.UserName,
                FullName = user.FullName ?? "",
                AvatarUrl = user.AvatarUrl,
                DaXacMinhEmail = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                FollowersCount = followersCount,
                FollowingCount = followingCount
            };

            return Ok(result);
        }
    }
}