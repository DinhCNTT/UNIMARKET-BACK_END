using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UniMarket.Models;
using System.Threading.Tasks;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using UniMarket.DataAccess; // Đảm bảo import namespace chứa ApplicationDbContext

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _context; // 1. Khai báo Context

    // 2. Inject Context vào Constructor
    public AuthController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration,
        ApplicationDbContext context)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _configuration = configuration;
        _context = context;
    }

    // ... [Hàm Register giữ nguyên] ...
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterModel model)
    {
        // 1. Validate dữ liệu đầu vào
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // 2. Kiểm tra Email đã tồn tại chưa
        var existingUser = await _userManager.FindByEmailAsync(model.Email);
        if (existingUser != null) return BadRequest(new { message = "Email này đã được sử dụng." });

        // 3. Kiểm tra Số điện thoại đã tồn tại chưa
        var existingPhoneUser = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == model.PhoneNumber);
        if (existingPhoneUser != null) return BadRequest(new { message = "Số điện thoại này đã được sử dụng." });

        // 4. Kiểm tra mật khẩu xác nhận
        if (model.Password != model.ConfirmPassword) return BadRequest(new { message = "Mật khẩu xác nhận không khớp!" });

        // 5. Tạo đối tượng User
        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            PhoneNumber = model.PhoneNumber
        };

        // 6. Thực hiện tạo User trong DB
        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            // 7. Gán quyền mặc định "User"
            await _userManager.AddToRoleAsync(user, "User");

            // ============================================================
            // ✅ BỔ SUNG: LƯU THIẾT BỊ KHI ĐĂNG KÝ
            // ============================================================
            try
            {
                var userAgent = Request.Headers["User-Agent"].ToString();
                if (string.IsNullOrEmpty(userAgent)) userAgent = "Unknown Device (Registration)";

                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                if (string.IsNullOrEmpty(ipAddress)) ipAddress = "Unknown IP";

                var newDevice = new UserDevice
                {
                    UserId = user.Id,
                    DeviceName = ParseUserAgent(userAgent), // Gọi hàm phụ ở dưới
                    Location = ipAddress,
                    LastLogin = DateTime.UtcNow,
                    IsCurrent = false
                };

                _context.UserDevices.Add(newDevice);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi lưu thiết bị khi đăng ký: " + ex.Message);
            }
            // ============================================================

            return Ok(new { message = "Đăng ký thành công!" });
        }

        // 8. Trả về lỗi nếu tạo User thất bại
        return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    // 3. Cập nhật hàm Login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginModel model)
    {
        // 1. Tìm user theo Email
        var user = await _userManager.FindByEmailAsync(model.Email);

        if (user == null)
            return NotFound(new { message = "Tài khoản không tồn tại!" });

        // 2. Kiểm tra mật khẩu
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, model.Password);

        if (!isPasswordValid)
            return Unauthorized(new { message = "Mật khẩu không đúng!" });

        // 3. Kiểm tra khóa tài khoản (Lockout)
        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            return Unauthorized(new { message = "Tài khoản bị khóa." });

        // ============================================================
        // ✅ LOGIC LƯU THIẾT BỊ (CẬP NHẬT HOẶC TẠO MỚI)
        // ============================================================
        try
        {
            // A. Lấy thông tin từ Request
            var userAgentRaw = Request.Headers["User-Agent"].ToString();
            var deviceName = ParseUserAgent(userAgentRaw); // Hàm làm gọn tên thiết bị (cần có trong controller)
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrEmpty(ipAddress)) ipAddress = "Unknown IP";

            // B. Lấy danh sách thiết bị cũ của user
            var userDevices = await _context.UserDevices
                .Where(d => d.UserId == user.Id)
                .ToListAsync();

            // C. Reset trạng thái IsCurrent của tất cả thiết bị về false
            foreach (var d in userDevices)
            {
                d.IsCurrent = false;
            }

            // D. Kiểm tra thiết bị này đã từng đăng nhập chưa?
            var existingDevice = userDevices.FirstOrDefault(d => d.DeviceName == deviceName);

            if (existingDevice != null)
            {
                // -> NẾU ĐÃ CÓ: Cập nhật thời gian, IP và set lại là Current
                existingDevice.LastLogin = DateTime.UtcNow;
                existingDevice.Location = ipAddress;
                existingDevice.IsCurrent = true;

                _context.UserDevices.Update(existingDevice);
            }
            else
            {
                // -> NẾU CHƯA CÓ: Tạo mới hoàn toàn
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

            // E. Lưu thay đổi vào DB
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Ghi log lỗi nhưng không chặn việc đăng nhập của user
            Console.WriteLine("Lỗi lưu thiết bị: " + ex.Message);
        }
        // ============================================================
        // ✅ KẾT THÚC LOGIC LƯU THIẾT BỊ
        // ============================================================

        // 4. Tạo Token và trả về kết quả
        var roles = await _userManager.GetRolesAsync(user);
        var token = GenerateJwtToken(user, roles.FirstOrDefault() ?? "User");

        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            fullName = user.FullName,
            phoneNumber = user.PhoneNumber,
            role = roles.FirstOrDefault() ?? "User",
            token = token,
            avatarUrl = user.AvatarUrl,
            emailConfirmed = user.EmailConfirmed
        });
    }

    // 4. Hàm phụ để làm gọn tên thiết bị (User Agent rất dài)
    // Chỉ copy đoạn này nều trong file AuthController CHƯA CÓ hàm ParseUserAgent
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

    // ... [Hàm GenerateJwtToken giữ nguyên] ...
    private string GenerateJwtToken(ApplicationUser user, string role)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
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

    // ... [Hàm GetCurrentUser giữ nguyên] ...
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();
        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            fullName = user.FullName,
            avatarUrl = user.AvatarUrl,
            userName = user.UserName,
            phoneNumber = user.PhoneNumber
        });
    }

    public class LoginModel
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class RegisterModel
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }
}