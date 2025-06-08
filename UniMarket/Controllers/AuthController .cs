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

[Route("api/[controller]")]
[ApiController]

public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _configuration;

    public AuthController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IConfiguration configuration)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Kiểm tra email đã tồn tại chưa
        var existingUser = await _userManager.FindByEmailAsync(model.Email);
        if (existingUser != null)
            return BadRequest(new { message = "Email này đã được sử dụng. Vui lòng chọn email khác." });

        // Kiểm tra số điện thoại đã tồn tại chưa
        var existingPhoneUser = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == model.PhoneNumber);
        if (existingPhoneUser != null)
            return BadRequest(new { message = "Số điện thoại này đã được sử dụng. Vui lòng chọn số khác." });

        // Kiểm tra xác nhận mật khẩu
        if (model.Password != model.ConfirmPassword)
            return BadRequest(new { message = "Mật khẩu xác nhận không khớp!" });

        // Tạo tài khoản mới
        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            PhoneNumber = model.PhoneNumber // ✅ Thêm số điện thoại vào database
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        // Gán vai trò mặc định là "User"
        await _userManager.AddToRoleAsync(user, "User");

        return Ok(new { message = "Đăng ký thành công! Bạn đã được gán vai trò User." });
    }


    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);

        if (user == null)
            return NotFound(new { message = "Tài khoản không tồn tại!" });

        var isPasswordValid = await _userManager.CheckPasswordAsync(user, model.Password);

        if (!isPasswordValid)
            return Unauthorized(new { message = "Mật khẩu không đúng!" });

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            return Unauthorized(new { message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên." });

        var roles = await _userManager.GetRolesAsync(user);
        var token = GenerateJwtToken(user, roles.FirstOrDefault() ?? "User");

        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            fullName = user.FullName,
            role = roles.FirstOrDefault() ?? "User",
            token = token,
            avatarUrl = user.AvatarUrl,
            emailConfirmed = user.EmailConfirmed
        });
    }


    // ✅ Hàm tạo JWT Token có chứa UserId
    private string GenerateJwtToken(ApplicationUser user, string role)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? throw new ArgumentNullException("Jwt:Key không được để trống"));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);

        var claims = new[]
    {
    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    new Claim(ClaimTypes.NameIdentifier, user.Id),        // ✅ GIỮ LẠI
    new Claim(ClaimTypes.Role, role),                     // ✅ GIỮ LẠI
    new Claim(ClaimTypes.Email, user.Email),              // ✅ Optional
    new Claim(ClaimTypes.Name, user.FullName ?? "")       // ✅ Optional
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



    // Model đăng nhập
    public class LoginModel
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    // Model đăng ký
    public class RegisterModel
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
