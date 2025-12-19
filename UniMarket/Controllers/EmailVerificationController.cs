using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using UniMarket.Models;
using UniMarket.Services;
using UniMarket.DataAccess; // Đảm bảo namespace này trỏ đến nơi chứa ApplicationDbContext
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Net.Http;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore; // 🔥 Cần thêm cái này để dùng ToListAsync

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmailVerificationController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailVerificationController> _logger;
        // 👇 1. KHAI BÁO BIẾN _context
        private readonly ApplicationDbContext _context;

        public EmailVerificationController(
            UserManager<ApplicationUser> userManager, 
            IEmailSender emailSender, 
            IConfiguration configuration, 
            ILogger<EmailVerificationController> logger,
            // 👇 2. INJECT ApplicationDbContext VÀO CONSTRUCTOR
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _configuration = configuration;
            _logger = logger;
            _context = context; // 👇 GÁN GIÁ TRỊ
        }

        [Authorize]
        [HttpPost("send-code")]
        public async Task<IActionResult> SendVerificationCode()
        {
            _logger.LogInformation("✅ API /send-code được gọi");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("⚠️ Không thể lấy UserId từ token.");
                return Unauthorized(new { message = "Không xác định được người dùng." });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning($"❌ Không tìm thấy người dùng với ID: {userId}");
                return NotFound(new { message = "Người dùng không tồn tại." });
            }

            if (user.EmailConfirmed)
            {
                _logger.LogInformation("📬 Email đã được xác minh trước đó.");
                return BadRequest(new { message = "Email của bạn đã được xác minh." });
            }

            // Kiểm tra thời gian gửi lại mã (1 phút)
            if (user.CodeGeneratedAt != null &&
                (DateTime.UtcNow - user.CodeGeneratedAt.Value).TotalMinutes < 1)
            {
                return BadRequest(new { message = "Vui lòng chờ ít nhất 1 phút trước khi gửi lại mã." });
            }

            // ✅ Tạo mã xác minh
            var code = new Random().Next(100000, 999999).ToString();

            // ✉️ Giao diện HTML
            var emailContent = $@"
            <div style='font-family:Arial,sans-serif;line-height:1.6'>
                <h2 style='color:#2e86de;'>🔐 Xác minh Email - UniMarket</h2>
                <p>Chào <strong>{user.FullName ?? user.UserName}</strong>,</p>
                <p>Đây là mã xác minh email của bạn:</p>
                <p style='font-size:24px;font-weight:bold;color:#27ae60'>{code}</p>
                <p><i>Mã có hiệu lực trong vòng <strong>5 phút</strong>.</i></p>
                <hr>
                <p style='font-size:12px;color:#888'>Nếu bạn không yêu cầu xác minh, vui lòng bỏ qua email này.</p>
            </div>";

            try
            {
                _logger.LogInformation($"📤 Đang gửi mã xác minh tới: {user.Email}");
                await _emailSender.SendEmailAsync(user.Email, "🔐 Mã xác minh tài khoản - UniMarket", emailContent);
                _logger.LogInformation("✅ Email đã được gửi thành công.");

                user.EmailVerificationCode = code;
                user.CodeGeneratedAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);

                return Ok(new
                {
                    message = "✅ Mã xác minh đã được gửi đến email của bạn.",
                    expiresInMinutes = 5
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Lỗi gửi email: {ex.Message}", ex);
                return StatusCode(500, new
                {
                    message = "❌ Có lỗi xảy ra khi gửi email. Vui lòng thử lại sau hoặc kiểm tra cấu hình máy chủ.",
                    error = ex.Message
                });
            }
        }

        [HttpPost("verify-code")]
        public async Task<IActionResult> VerifyCode([FromBody] VerifyModel model)
        {
            if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Code))
            {
                return BadRequest(new { message = "Email và mã xác minh không được để trống." });
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                _logger.LogWarning($"Không tìm thấy người dùng với email: {model.Email}");
                return NotFound(new { message = "Người dùng không tồn tại." });
            }

            if (user.EmailConfirmed)
            {
                return BadRequest(new { message = "Email này đã được xác minh từ trước." });
            }

            // Kiểm tra mã xác minh
            if (user.EmailVerificationCode != model.Code)
            {
                return BadRequest(new { message = "Mã xác minh không đúng!" });
            }

            // Kiểm tra xem mã xác minh có hết hạn không (10 phút)
            if (user.CodeGeneratedAt == null || (DateTime.UtcNow - user.CodeGeneratedAt.Value).TotalMinutes > 10)
            {
                return BadRequest(new { message = "Mã xác minh đã hết hạn!" });
            }

            user.EmailConfirmed = true;
            user.EmailVerificationCode = null;
            user.CodeGeneratedAt = null;

            try
            {
                await _userManager.UpdateAsync(user);
                _logger.LogInformation($"Email {model.Email} đã được xác minh thành công.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Lỗi khi cập nhật người dùng {model.Email}: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi khi xác minh email, vui lòng thử lại sau." });
            }

            return Ok(new { message = "Xác minh email thành công!" });
        }

        // Đăng nhập bằng Facebook
        [HttpPost("facebook-login")]
        public async Task<IActionResult> FacebookLogin([FromBody] FacebookLoginModel model)
        {
            var httpClient = new HttpClient();

            var response = await httpClient.GetAsync($"https://graph.facebook.com/me?fields=id,name,email&access_token={model.AccessToken}");
            if (!response.IsSuccessStatusCode)
                return BadRequest(new { message = "Token Facebook không hợp lệ." });

            var fbUser = JsonSerializer.Deserialize<FacebookUser>(await response.Content.ReadAsStringAsync());

            if (fbUser == null || string.IsNullOrEmpty(fbUser.Email))
                return BadRequest(new { message = "Không lấy được email từ Facebook." });

            var user = await _userManager.FindByEmailAsync(fbUser.Email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = fbUser.Email,
                    Email = fbUser.Email,
                    FullName = fbUser.Name,
                    EmailConfirmed = false // Chưa xác minh email
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });

                await _userManager.AddToRoleAsync(user, "User");
            }

            var roles = await _userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles.FirstOrDefault() ?? "User");

            return Ok(new
            {
                id = user.Id,
                email = user.Email,
                fullName = user.FullName,
                role = roles.FirstOrDefault() ?? "User",
                token = token,
                emailConfirmed = user.EmailConfirmed
            });
        }

        // Đăng nhập bằng Google
        [HttpPost("google-login")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginModel model)
        {
            try
            {
                // ============================================================
                // 1. XÁC MINH ID TOKEN TỪ GOOGLE
                // ============================================================
                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _configuration["Google:ClientId"] }
                };

                var payload = await GoogleJsonWebSignature.ValidateAsync(model.IdToken, settings);

                var email = payload.Email;
                var name = payload.Name;
                var picture = payload.Picture;

                if (string.IsNullOrEmpty(email))
                {
                    return BadRequest(new { message = "Không lấy được email từ Google." });
                }

                // ============================================================
                // 2. TÌM HOẶC TẠO USER
                // ============================================================
                var user = await _userManager.FindByEmailAsync(email);

                if (user == null)
                {
                    // --- CASE 2.1: User chưa có -> Tạo mới ---
                    user = new ApplicationUser
                    {
                        UserName = email,
                        Email = email,
                        FullName = name,
                        AvatarUrl = picture, // Lấy luôn ảnh Google làm ảnh đại diện ban đầu
                        EmailConfirmed = true
                    };

                    var createResult = await _userManager.CreateAsync(user);
                    if (!createResult.Succeeded)
                    {
                        return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });
                    }

                    await _userManager.AddToRoleAsync(user, "User");
                }
                else
                {
                    // --- CASE 2.2: User đã có -> Cập nhật ảnh (có điều kiện) ---
                    try
                    {
                        // Chỉ cập nhật ảnh nếu user hiện tại trong DB chưa có ảnh
                        if (!string.IsNullOrEmpty(picture) && string.IsNullOrEmpty(user.AvatarUrl))
                        {
                            user.AvatarUrl = picture;
                            await _userManager.UpdateAsync(user);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Không thể cập nhật avatar Google cho {email}: {ex.Message}");
                    }
                }

                // ============================================================
                // 3. KIỂM TRA KHÓA TÀI KHOẢN
                // ============================================================
                if (user.LockoutEnd != null && user.LockoutEnd > DateTime.UtcNow)
                {
                    return Unauthorized(new { message = "Tài khoản của bạn đã bị khóa bởi quản trị viên." });
                }

                // ============================================================
                // 4. ✅ LƯU THIẾT BỊ (LOGIC THÔNG MINH - NGĂN TRÙNG LẶP)
                // ============================================================
                try
                {
                    // 4.1. Lấy thông tin hiện tại
                    var userAgentRaw = Request.Headers["User-Agent"].ToString();
                    var deviceName = ParseUserAgent(userAgentRaw); // Hàm làm gọn tên thiết bị

                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    if (string.IsNullOrEmpty(ipAddress)) ipAddress = "Unknown IP";

                    // 4.2. Lấy tất cả thiết bị cũ của user
                    var userDevices = await _context.UserDevices
                        .Where(d => d.UserId == user.Id)
                        .ToListAsync();

                    // 4.3. Reset tất cả thiết bị về IsCurrent = false
                    foreach (var d in userDevices)
                    {
                        d.IsCurrent = false;
                    }

                    // 4.4. KIỂM TRA: Thiết bị này đã từng đăng nhập chưa?
                    var existingDevice = userDevices.FirstOrDefault(d => d.DeviceName == deviceName);

                    if (existingDevice != null)
                    {
                        // A. NẾU CÓ RỒI -> Cập nhật thời gian và IP mới nhất
                        existingDevice.LastLogin = DateTime.UtcNow;
                        existingDevice.Location = ipAddress;
                        existingDevice.IsCurrent = true;

                        _context.UserDevices.Update(existingDevice);
                    }
                    else
                    {
                        // B. NẾU CHƯA CÓ -> Tạo mới
                        var newDevice = new UserDevice
                        {
                            UserId = user.Id,
                            DeviceName = deviceName,
                            Location = ipAddress,
                            LastLogin = DateTime.UtcNow,
                            IsCurrent = true
                        };
                        _context.UserDevices.Add(newDevice);
                    }

                    // 4.5. Lưu thay đổi
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // Log lỗi nhưng không chặn quy trình đăng nhập
                    Console.WriteLine("Lỗi lưu thiết bị Google Login: " + ex.Message);
                }

                // ============================================================
                // 5. CẤP TOKEN & TRẢ KẾT QUẢ
                // ============================================================

                // Kiểm tra và gán Role nếu thiếu
                var roles = await _userManager.GetRolesAsync(user);
                if (!roles.Contains("User"))
                {
                    await _userManager.AddToRoleAsync(user, "User");
                    roles.Add("User");
                }

                var token = GenerateJwtToken(user, roles.FirstOrDefault() ?? "User");

                return Ok(new
                {
                    id = user.Id,
                    email = user.Email,
                    fullName = user.FullName,
                    role = roles.FirstOrDefault() ?? "User",
                    token = token,
                    emailConfirmed = user.EmailConfirmed,
                    avatarUrl = user.AvatarUrl
                });
            }
            catch (InvalidJwtException ex)
            {
                return BadRequest(new { message = "ID Token không hợp lệ.", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi không xác định.", detail = ex.Message });
            }
        }

        // Hàm tạo JWT token
        private string GenerateJwtToken(ApplicationUser user, string role)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? throw new ArgumentNullException("Jwt:Key không được để trống"));

            var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName ?? "")
            };

            int expireHours = int.TryParse(jwtSettings["ExpireHours"], out var h) ? h : 2;

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(expireHours),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // 👇 3. BỔ SUNG HÀM ParseUserAgent VÀO ĐÂY
        private string ParseUserAgent(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Unknown Device";
            
            if (userAgent.Contains("Windows")) return "Windows PC";
            if (userAgent.Contains("Macintosh")) return "MacBook";
            if (userAgent.Contains("iPhone")) return "iPhone";
            if (userAgent.Contains("Android")) return "Android Device";
            if (userAgent.Contains("Linux")) return "Linux PC";

            return userAgent.Length > 50 ? userAgent.Substring(0, 47) + "..." : userAgent;
        }

        // Test API
        [HttpGet("test-send")]
        [AllowAnonymous]
        public async Task<IActionResult> TestSend()
        {
            var email = "test-s38jmersr@srv1.mail-tester.com"; // Thay bằng email thật

            var html = "<h1>Test gửi mail từ UniMarket</h1><p>Đây là email test.</p>";

            try
            {
                await _emailSender.SendEmailAsync(email, "✅ UniMarket Test", html);
                return Ok(new { message = "Gửi email thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi gửi email.", detail = ex.Message });
            }
        }

        // ===== MODELS =====
        public class VerifyModel
        {
            public string Email { get; set; }
            public string Code { get; set; }
        }

        public class FacebookLoginModel
        {
            public string AccessToken { get; set; }
        }

        public class FacebookUser
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
        }

        public class GoogleLoginModel
        {
            public string IdToken { get; set; }
        }

        public class GoogleUser
        {
            public string Email { get; set; }
            public string Name { get; set; }
            public string Picture { get; set; }
            public string Aud { get; set; }
        }
    }
}