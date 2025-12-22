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
        private readonly IUserAffinityService _affinityService;

        // 1. KHAI BÁO THÊM SIGNIN MANAGER
        private readonly SignInManager<ApplicationUser> _signInManager;

        // 2. INJECT VÀO CONSTRUCTOR
        public UserProfileController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            PhotoService photoService,
            SignInManager<ApplicationUser> signInManager,
            IUserAffinityService affinityService) // <--- Thêm tham số này
        {
            _userManager = userManager;
            _context = context;
            _photoService = photoService;
            _signInManager = signInManager;
            _affinityService = affinityService;// <--- Gán giá trị
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
            public bool IsPrivateAccount { get; set; }
            public bool IsFollowing { get; set; }
            public int TotalLikes { get; set; }
            public bool IsPending { get; set; }
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
        public class SocialLinkDto
        {
            public string Provider { get; set; }
            public bool IsLinked { get; set; }
            public string? LinkedDate { get; set; }
            public string? ProfileUrl { get; set; } // 👈 Thêm trường này để trả về Link cho Frontend
        }

        public class LinkSocialModel
        {
            public string Provider { get; set; }
            public string? Url { get; set; }
        }
        public class PrivacyUpdateModel
        {
            public bool IsPrivateAccount { get; set; }
        }
        // 1. Lấy trạng thái riêng tư hiện tại
        [HttpGet("privacy")]
        public async Task<IActionResult> GetPrivacy()
        {
            // Lấy ID user từ Token
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            // Trả về trạng thái hiện tại
            return Ok(new { isPrivateAccount = user.IsPrivateAccount });
        }

        // 2. Cập nhật trạng thái riêng tư (Bật/Tắt)
        [HttpPut("privacy")]
        public async Task<IActionResult> UpdatePrivacy([FromBody] PrivacyUpdateModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            // Cập nhật giá trị
            user.IsPrivateAccount = model.IsPrivateAccount;

            // Lưu thay đổi vào DB thông qua UserManager
            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
                return BadRequest(new { message = "Cập nhật thất bại.", errors = result.Errors });

            return Ok(new
            {
                message = model.IsPrivateAccount ? "Đã chuyển sang tài khoản riêng tư." : "Đã chuyển sang tài khoản công khai.",
                isPrivateAccount = user.IsPrivateAccount
            });
        }

        // =========================================================================
        // API QUẢN LÝ THIẾT BỊ (DEVICES)
        // =========================================================================
        [HttpGet("devices")]
        public async Task<IActionResult> GetUserDevices()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Sắp xếp: Thiết bị hiện tại lên đầu, sau đó đến ngày đăng nhập gần nhất
            var devices = await _context.UserDevices
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.IsCurrent)
                .ThenByDescending(d => d.LastLogin)
                .Select(d => new
                {
                    d.Id,
                    d.DeviceName,
                    d.Location,
                    d.LastLogin,
                    d.IsCurrent // True nếu là thiết bị đang dùng request này
                })
                .ToListAsync();

            return Ok(devices);
        }

        // 3. Xóa (Đăng xuất) thiết bị theo ID
        [HttpDelete("devices/{id}")]
        public async Task<IActionResult> DeleteDevice(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            // Tìm thiết bị trong DB
            // QUAN TRỌNG: Phải kiểm tra UserId để đảm bảo user chỉ xóa được thiết bị của chính mình
            var device = await _context.UserDevices
                                       .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

            if (device == null)
            {
                return NotFound(new { message = "Thiết bị không tồn tại hoặc không thuộc về bạn." });
            }

            // Xóa khỏi DB
            _context.UserDevices.Remove(device);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã đăng xuất thiết bị thành công." });
        }
        // --- API 1: LẤY DANH SÁCH LIÊN KẾT ---
        [HttpGet("social-links")]
        public async Task<IActionResult> GetSocialLinks()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // 1. Lấy danh sách các link user đã lưu trong DB
            var userLinks = await _context.UserSocialLinks
                                          .Where(x => x.UserId == userId)
                                          .ToListAsync();

            // 2. Định nghĩa danh sách các mạng xã hội hệ thống hỗ trợ
            var supportedProviders = new List<string> { "Facebook", "Google", "Instagram", "TikTok" };

            // 3. Map dữ liệu để trả về (Kết hợp danh sách hỗ trợ + dữ liệu DB)
            // Trong method GetSocialLinks:
            var result = supportedProviders.Select(provider => {
                var link = userLinks.FirstOrDefault(x => x.Provider == provider);
                return new SocialLinkDto
                {
                    Provider = provider,
                    IsLinked = link != null,
                    LinkedDate = link?.LinkedAt.ToString("dd/MM/yyyy"),
                    ProfileUrl = link?.ProfileUrl // 👈 Map dữ liệu từ DB ra
                };
            }).ToList();

            return Ok(result);
        }

        // --- API 2: LẤY DANH SÁCH LIÊN KẾT CÔNG KHAI (Cho người khác xem) ---
        [HttpGet("public-social-links/{targetUserId}")]
        public async Task<IActionResult> GetPublicSocialLinks(string targetUserId)
        {
            if (string.IsNullOrEmpty(targetUserId))
            {
                return BadRequest("Vui lòng cung cấp User ID.");
            }

            // 1. Lấy danh sách các link user mục tiêu đã lưu trong DB
            var userLinks = await _context.UserSocialLinks
                                          .Where(x => x.UserId == targetUserId)
                                          .ToListAsync();

            // 2. Chỉ trả về những tài khoản ĐÃ LIÊN KẾT
            // (Khác với API trên: không cần trả về danh sách false/chưa liên kết)
            var result = userLinks.Select(link => new SocialLinkDto
            {
                Provider = link.Provider,
                IsLinked = true,
                LinkedDate = link.LinkedAt.ToString("dd/MM/yyyy"),
                ProfileUrl = link.ProfileUrl
            }).ToList();

            return Ok(result);
        }

        // --- API 2: LIÊN KẾT / HỦY LIÊN KẾT (TOGGLE) ---
        [HttpPost("toggle-social")]
        public async Task<IActionResult> ToggleSocialLink([FromBody] LinkSocialModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var existingLink = await _context.UserSocialLinks
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Provider == model.Provider);

            // TRƯỜNG HỢP 1: NẾU ĐÃ CÓ LINK -> CẬP NHẬT URL MỚI HOẶC XÓA (Tùy logic, ở đây mình làm Cập nhật/Thêm mới)
            // Nếu bạn muốn nút này vừa là thêm, vừa là cập nhật:

            if (existingLink != null)
            {
                // Nếu user gửi chuỗi rỗng lên thì coi như là HỦY LIÊN KẾT
                if (string.IsNullOrEmpty(model.Url))
                {
                    _context.UserSocialLinks.Remove(existingLink);
                    await _context.SaveChangesAsync();
                    return Ok(new { message = $"Đã hủy liên kết {model.Provider}", isLinked = false });
                }
                else
                {
                    // Cập nhật lại Link mới
                    existingLink.ProfileUrl = model.Url;
                    existingLink.LinkedAt = DateTime.UtcNow; // Cập nhật lại ngày nếu muốn
                    await _context.SaveChangesAsync();
                    return Ok(new { message = $"Đã cập nhật liên kết {model.Provider}!", isLinked = true });
                }
            }
            else
            {
                // TRƯỜNG HỢP 2: CHƯA CÓ -> THÊM MỚI
                if (string.IsNullOrEmpty(model.Url)) return BadRequest(new { message = "Vui lòng nhập đường dẫn liên kết." });

                var newLink = new UserSocialLink
                {
                    UserId = userId,
                    Provider = model.Provider,
                    LinkedAt = DateTime.UtcNow,
                    ExternalUserId = "manual-add",
                    ProfileUrl = model.Url // 👈 Lưu Url user nhập vào DB
                };

                _context.UserSocialLinks.Add(newLink);
                await _context.SaveChangesAsync();
                return Ok(new { message = $"Đã liên kết {model.Provider} thành công!", isLinked = true });
            }
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

        // =========================================================================
        // API XÓA TÀI KHOẢN (SOFT DELETE + ANONYMIZATION)
        // =========================================================================
        [HttpDelete("delete")]
        [Authorize]
        public async Task<IActionResult> DeleteAccount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized(new { message = "Bạn chưa đăng nhập." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    try
                    {
                        // 1.1. Xóa Token, Thiết bị, Social Link
                        var userTokens = _context.UserTokens.Where(ut => ut.UserId == userId);
                        _context.UserTokens.RemoveRange(userTokens);

                        var devices = _context.UserDevices.Where(d => d.UserId == userId);
                        _context.UserDevices.RemoveRange(devices);

                        var socialLinks = _context.UserSocialLinks.Where(sl => sl.UserId == userId);
                        _context.UserSocialLinks.RemoveRange(socialLinks);

                        // 1.2. Xóa Lịch sử tìm kiếm & Thông báo
                        var history = _context.SearchHistories.Where(h => h.UserId == userId);
                        _context.SearchHistories.RemoveRange(history);

                        // --- SỬA LỖI TẠI ĐÂY: Dùng ReceiverId thay vì UserId ---
                        var notis = _context.UserNotifications.Where(n => n.ReceiverId == userId);
                        _context.UserNotifications.RemoveRange(notis);

                        // 1.3. Xóa Follow
                        var follows = _context.Follows.Where(f => f.FollowerId == userId || f.FollowingId == userId);
                        _context.Follows.RemoveRange(follows);

                        // 1.4. Xóa Bookmark
                        var saves = _context.VideoTinDangSaves.Where(s => s.MaNguoiDung == userId);
                        _context.VideoTinDangSaves.RemoveRange(saves);

                        // 1.5. Xóa Chặn (Dùng đúng tên cột BlockerId/BlockedId như đã sửa ở turn trước)
                        var blocks = _context.BlockedUsers.Where(b => b.BlockerId == userId || b.BlockedId == userId);
                        _context.BlockedUsers.RemoveRange(blocks);

                        // 2. SOFT DELETE TinDang
                        var myPosts = await _context.TinDangs
                                            .Where(t => t.MaNguoiBan == userId && !t.IsDeleted)
                                            .ToListAsync();

                        foreach (var post in myPosts)
                        {
                            post.IsDeleted = true;
                            // post.DeletedAt = DateTime.UtcNow; // Nếu model TinDang có cột này
                        }

                        // 3. ANONYMIZATION USER
                        /* if (!string.IsNullOrEmpty(user.AvatarUrl))
                        {
                            try { await _photoService.DeleteMediaByUrlAsync(user.AvatarUrl); } catch { }
                        } */

                        string deletedToken = $"deleted_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                        user.UserName = deletedToken;
                        user.NormalizedUserName = deletedToken.ToUpper();
                        user.Email = $"{deletedToken}@deleted.unimarket";
                        user.NormalizedEmail = user.Email.ToUpper();
                        user.FullName = "Người dùng đã xóa";
                        user.AvatarUrl = "https://your-domain.com/default-avatar.png";
                        user.Address = null;
                        user.Age = null;
                        user.PhoneNumber = null;
                        user.PasswordHash = null;
                        user.SecurityStamp = Guid.NewGuid().ToString();
                        user.IsOnline = false;
                        user.LastOnlineTime = null;
                        user.IsDeleted = true;
                        user.DeletedAt = DateTime.UtcNow;

                        // 4. LƯU DB & LOGOUT
                        var updateResult = await _userManager.UpdateAsync(user);
                        if (!updateResult.Succeeded)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = "Lỗi cập nhật trạng thái xóa.", errors = updateResult.Errors });
                        }

                        var roles = await _userManager.GetRolesAsync(user);
                        if (roles.Count > 0)
                        {
                            await _userManager.RemoveFromRolesAsync(user, roles);
                        }

                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        // --- ĐÃ CÓ BIẾN NÀY ĐỂ DÙNG ---
                        await _signInManager.SignOutAsync();

                        return Ok(new { message = "Tài khoản đã được xóa thành công." });
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return StatusCode(500, new { message = "Lỗi hệ thống.", error = ex.Message });
                    }
                }
            });
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
            var targetUser = await _userManager.FindByIdAsync(userId);
            if (targetUser == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            string? currentUserId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                currentUserId = _userManager.GetUserId(User);
            }

            // =========================================================================
            // 🔥 SỬA LẠI LOGIC RIÊNG TƯ: Check thêm bảng Follows
            // =========================================================================
            bool isAllowedToView = true;

            // Nếu là chính chủ -> Được xem
            if (currentUserId == userId)
            {
                isAllowedToView = true;
            }
            // Nếu là tài khoản công khai -> Được xem
            else if (!targetUser.IsPrivateAccount)
            {
                isAllowedToView = true;
            }
            // Nếu là tài khoản riêng tư -> Phải check xem đã Follow và được Accept chưa
            else
            {
                if (currentUserId == null) // Chưa đăng nhập mà đòi xem riêng tư -> Chặn
                {
                    isAllowedToView = false;
                }
                else
                {
                    // Check trong Database
                    var isFollowing = await _context.Follows
                        .AnyAsync(f => f.FollowerId == currentUserId
                                    && f.FollowingId == userId
                                    && f.Status == FollowStatus.Accepted); // Quan trọng là Accepted

                    isAllowedToView = isFollowing;
                }
            }

            // Nếu không được phép xem -> Trả về rỗng
            if (!isAllowedToView)
            {
                return Ok(new List<UserPostDto>());
            }
            // =========================================================================

            // ... (Code query dữ liệu giữ nguyên như cũ)
            var posts = await _context.TinDangs
                  .AsNoTracking()
                  .Where(t => t.MaNguoiBan == userId && t.TrangThai == TrangThaiTinDang.DaDuyet)
                  .Include(t => t.AnhTinDangs)
                  .Include(t => t.TinhThanh)
                  .Include(t => t.QuanHuyen)
                  .Include(t => t.TinDangYeuThichs)
                  .OrderByDescending(t => t.NgayDang)
                  .Select(t => new UserPostDto
                  {
                      MaTinDang = t.MaTinDang,
                      TieuDe = t.TieuDe,
                      Gia = (double)t.Gia,
                      MoTa = t.MoTa,
                      VideoDuongDan = t.VideoUrl,
                      KhuVuc = (t.QuanHuyen != null ? t.QuanHuyen.TenQuanHuyen : "") +
                               (t.QuanHuyen != null && t.TinhThanh != null ? ", " : "") +
                               (t.TinhThanh != null ? t.TinhThanh.TenTinhThanh : ""),
                      NgayDang = t.NgayDang,
                      TinhTrang = t.TinhTrang,
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
            var targetUser = await _userManager.FindByIdAsync(userId);
            if (targetUser == null) return NotFound(new { message = "Không tìm thấy người dùng." });

            string? currentUserId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                currentUserId = _userManager.GetUserId(User);
            }

            // =========================================================================
            // 🔥 SỬA LẠI LOGIC RIÊNG TƯ
            // =========================================================================
            bool isAllowedToView = true;

            if (currentUserId == userId) isAllowedToView = true;
            else if (!targetUser.IsPrivateAccount) isAllowedToView = true;
            else
            {
                if (currentUserId == null) isAllowedToView = false;
                else
                {
                    isAllowedToView = await _context.Follows
                        .AnyAsync(f => f.FollowerId == currentUserId
                                    && f.FollowingId == userId
                                    && f.Status == FollowStatus.Accepted);
                }
            }

            if (!isAllowedToView)
            {
                return Ok(new List<UserVideoDto>());
            }
            // =========================================================================

            // ... (Code query dữ liệu giữ nguyên)
            var videos = await _context.TinDangs
                .AsNoTracking()
                .Where(t => t.MaNguoiBan == userId
                            && t.VideoUrl != null
                            && t.TrangThai == TrangThaiTinDang.DaDuyet)
                .Select(t => new UserVideoDto
                {
                    MaTinDang = t.MaTinDang,
                    TieuDe = t.TieuDe,
                    VideoDuongDan = t.VideoUrl,
                    AnhBia = t.AnhTinDangs.OrderBy(a => a.Order).Select(a => a.DuongDan).FirstOrDefault(),
                    SoLuongTym = _context.VideoLikes.Count(v => v.MaTinDang == t.MaTinDang),
                    Views = t.SoLuotXem,
                    CreatedAt = t.NgayDang,
                    DaTym = currentUserId != null
                            && _context.VideoLikes.Any(v => v.MaTinDang == t.MaTinDang && v.UserId == currentUserId)
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
            // 1. Tìm User mục tiêu
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng." });

            // 2. Xác định người đang xem (Viewer)
            var currentViewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // =================================================================
            // ✅ [INTEGRATION CODE 2] TRACKING AFFINITY (Ghi nhận hành vi xem)
            // =================================================================
            // Nếu người xem đã đăng nhập và không phải là chính chủ đang tự xem profile mình
            if (!string.IsNullOrEmpty(currentViewerId) && currentViewerId != userId)
            {
                // Gọi service tính điểm chạy ngầm (Fire and forget)
                // ViewProfile = 1 điểm
                _ = _affinityService.TrackInteractionAsync(currentViewerId, userId, InteractionType.ViewProfile);
            }

            // =================================================================
            // ✅ [INTEGRATION CODE 1] LOGIC TÍNH TOÁN & FOLLOW STATUS
            // =================================================================

            // 3. Đếm follow (QUAN TRỌNG: Chỉ đếm trạng thái Accepted)
            var followersCount = await _context.Follows
                .CountAsync(f => f.FollowingId == userId && f.Status == FollowStatus.Accepted);

            var followingCount = await _context.Follows
                .CountAsync(f => f.FollowerId == userId && f.Status == FollowStatus.Accepted);

            // 4. Logic tính tổng số Likes của các bài đăng thuộc User này
            var totalLikes = await _context.VideoLikes
                .Include(v => v.TinDang)
                .Where(v => v.TinDang.MaNguoiBan == userId)
                .CountAsync();

            // 5. Check trạng thái quan hệ giữa người xem và profile này
            bool isFollowing = false;
            bool isPending = false; // Biến này quan trọng để frontend hiện nút "Đã yêu cầu" hay "Follow"

            if (!string.IsNullOrEmpty(currentViewerId))
            {
                var followRecord = await _context.Follows
                    .FirstOrDefaultAsync(f => f.FollowingId == userId && f.FollowerId == currentViewerId);

                if (followRecord != null)
                {
                    isFollowing = (followRecord.Status == FollowStatus.Accepted);
                    isPending = (followRecord.Status == FollowStatus.Pending);
                }
            }

            // 6. Trả về kết quả
            var result = new UserInfoDto
            {
                Id = user.Id,
                UserName = user.UserName,
                FullName = user.FullName ?? "",
                AvatarUrl = user.AvatarUrl,
                DaXacMinhEmail = user.EmailConfirmed,
                PhoneNumber = user.PhoneNumber,
                FollowersCount = followersCount,
                FollowingCount = followingCount,
                TotalLikes = totalLikes,
                IsPrivateAccount = user.IsPrivateAccount,
                IsFollowing = isFollowing,
                IsPending = isPending
            };

            return Ok(result);
        }
    }
}