using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMarket.Models;
using Microsoft.AspNetCore.Cors;
using System.Linq;
using System.Threading.Tasks;
using static AuthController;
using UniMarket.DataAccess;
using UniMarket.Services;
using UniMarket.DTO;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Caching.Memory; // Using cho Cache
using System.Collections.Generic;        // Using cho List<>
using System.IO;                       // Using cho Path, File, Directory
using System;                         // Using cho DateTime, Guid, Exception

namespace UniMarket.Controllers
{
    [Route("api/admin")]
    [ApiController]
    
    public class AdminController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly PhotoService _photoService;
        private readonly IMemoryCache _memoryCache; // Khai báo MemoryCache

        // Định nghĩa cache key cố định
        private const string CategoryCacheKey = "categories-with-icon";

        // Thêm IMemoryCache vào constructor
        public AdminController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ApplicationDbContext context, PhotoService photoService, IMemoryCache memoryCache)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _photoService = photoService;
            _memoryCache = memoryCache; // Gán cache
        }


        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _userManager.Users.ToListAsync();
            var userList = new List<object>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                bool isLocked = user.LockoutEnd != null && user.LockoutEnd > DateTime.Now;

                userList.Add(new
                {
                    user.Id,
                    FullName = user.FullName ?? "Không có",
                    user.UserName,
                    user.Email,
                    user.PhoneNumber,
                    Role = roles.Any() ? string.Join(", ", roles) : "Chưa có",
                    isLocked = isLocked // Trả về trạng thái khóa
                });
            }

            return Ok(userList);
        }

        [HttpPost("add-employee")]
        public async Task<IActionResult> AddEmployee([FromBody] RegisterModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true,
                FullName = model.FullName,  // Thêm dòng này
                PhoneNumber = model.PhoneNumber  // Thêm dòng này
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            await _userManager.AddToRoleAsync(user, SD.Role_Employee);
            return Ok(new { message = "Nhân viên đã được thêm thành công!" });
        }

        [HttpDelete("delete-user/{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound("Không tìm thấy người dùng!");

            var isAdmin = await _userManager.IsInRoleAsync(user, SD.Role_Admin);
            if (isAdmin)
                return BadRequest("Không thể xóa tài khoản Admin!");

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok("Xóa người dùng thành công!");
        }

        [HttpPost("toggle-lock/{id}")]
        public async Task<IActionResult> ToggleUserLock(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound("Không tìm thấy người dùng!");

            bool isLocked = user.LockoutEnd != null && user.LockoutEnd > DateTime.Now;

            if (isLocked)
            {
                user.LockoutEnd = null;
            }
            else
            {
                user.LockoutEnd = DateTime.Now.AddYears(100);
            }

            await _userManager.UpdateAsync(user);
            await _userManager.UpdateSecurityStampAsync(user);

            return Ok(new
            {
                message = isLocked ? "Tài khoản đã được mở khóa!" : "Tài khoản đã bị khóa!",
                isLocked = !isLocked
            });
        }

        [HttpPost("change-role")]
        public async Task<IActionResult> ChangeUserRole([FromBody] ChangeRoleModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
                return NotFound("Không tìm thấy người dùng!");

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

            await _userManager.AddToRoleAsync(user, model.NewRole);

            return Ok(new { message = $"Vai trò của {user.Email} đã được thay đổi thành {model.NewRole}." });
        }

        /// <summary>
        /// ✅ API gộp thêm nhân viên mới hoặc cập nhật vai trò
        /// </summary>
        [HttpPost("add-or-update-employee")]
        public async Task<IActionResult> AddOrUpdateEmployee([FromBody] EmployeeRoleModel model)
        {
            // --- 🔍 LOGGING BẮT ĐẦU ---
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine($"[API LOG] Bắt đầu cập nhật User: {model.Email}");
            Console.WriteLine($"[API LOG] Role yêu cầu: '{model.Role}'");

            if (!ModelState.IsValid)
            {
                Console.WriteLine("[API LOG] ❌ Lỗi ModelState");
                return BadRequest(ModelState);
            }

            // 1. Kiểm tra Role
            var roleName = model.Role?.Trim();
            var roleExists = await _roleManager.RoleExistsAsync(roleName);
            Console.WriteLine($"[API LOG] Kiểm tra Role '{roleName}' có tồn tại không? -> {roleExists}");

            if (!roleExists)
            {
                Console.WriteLine("[API LOG] ❌ Role không tồn tại trong DB!");
                return BadRequest(new { message = $"Lỗi: Vai trò '{roleName}' không tồn tại trong hệ thống (Bảng AspNetRoles)!" });
            }

            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user == null)
            {
                Console.WriteLine("[API LOG] -> Tạo User mới");
                // ... (Code tạo user mới - giữ nguyên logic của bạn)
                user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    EmailConfirmed = true,
                    FullName = model.FullName,
                    PhoneNumber = model.PhoneNumber
                };
                var createResult = await _userManager.CreateAsync(user, model.Password);
                if (!createResult.Succeeded) return BadRequest(createResult.Errors);
            }
            else
            {
                Console.WriteLine("[API LOG] -> Cập nhật User cũ");
                user.FullName = model.FullName;
                user.PhoneNumber = model.PhoneNumber;

                // Update Pass nếu có...
                if (!string.IsNullOrEmpty(model.Password)) { /* ... logic đổi pass ... */ }

                await _userManager.UpdateAsync(user);
            }

            // 2. XỬ LÝ ROLE (Quan trọng nhất)
            var currentRoles = await _userManager.GetRolesAsync(user);
            Console.WriteLine($"[API LOG] Role hiện tại của user: {string.Join(", ", currentRoles)}");

            // Nếu Role mới KHÁC Role cũ thì mới làm
            if (!currentRoles.Contains(roleName))
            {
                Console.WriteLine($"[API LOG] -> Tiến hành xóa Role cũ và thêm Role '{roleName}'");

                var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                {
                    Console.WriteLine("[API LOG] ❌ Lỗi khi xóa Role cũ");
                    return BadRequest(new { message = "Lỗi hệ thống: Không thể xóa vai trò cũ." });
                }

                var addResult = await _userManager.AddToRoleAsync(user, roleName);
                if (!addResult.Succeeded)
                {
                    Console.WriteLine("[API LOG] ❌ Lỗi khi thêm Role mới");
                    return BadRequest(new { message = $"Lỗi hệ thống: Không thể gán vai trò {roleName}." });
                }
            }
            else
            {
                Console.WriteLine("[API LOG] -> Role mới giống Role cũ, không cần đổi.");
            }

            Console.WriteLine("[API LOG] ✅ HOÀN TẤT CẬP NHẬT!");
            Console.WriteLine("--------------------------------------------------");

            return Ok(new { success = true, message = "Cập nhật thành công!" });
        }


        [HttpGet("employees")]
        public async Task<IActionResult> GetEmployees()
        {
            var users = await _userManager.Users.ToListAsync();
            var employeeList = new List<object>();
            int count = 1; // Tạo mã NV001, NV002...

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Contains("Employee"))
                {
                    bool isLocked = user.LockoutEnd != null && user.LockoutEnd > DateTime.Now;
                    employeeList.Add(new
                    {
                        EmployeeCode = $"NV{count:D3}", // Mã NV001, NV002,...
                        UserId = user.Id, // ID thực tế để gửi API
                        FullName = user.FullName ?? "Không có",
                        user.Email,
                        user.PhoneNumber,
                        Role = roles.FirstOrDefault() ?? "Chưa có",
                        isLocked
                    });
                    count++;
                }
            }
            return Ok(employeeList);
        }

        [HttpGet("get-parent-categories")]
        public async Task<IActionResult> GetParentCategories()
        {
            var parentCategories = await _context.DanhMucChas
                .Select(d => new
                {
                    d.MaDanhMucCha,
                    d.TenDanhMucCha,
                    d.AnhDanhMucCha,
                    d.Icon // Thêm icon vào query
                })
                .ToListAsync();

            var baseUrl = $"{Request.Scheme}://{Request.Host}"; // Lấy base URL của server

            var result = parentCategories.Select(c => new
            {
                c.MaDanhMucCha,
                c.TenDanhMucCha,
                AnhDanhMucCha = string.IsNullOrEmpty(c.AnhDanhMucCha)
                    ? $"{baseUrl}/uploads/default-image.jpg"
                    : (c.AnhDanhMucCha.StartsWith("http") ? c.AnhDanhMucCha : $"{baseUrl}/{c.AnhDanhMucCha}"),
                Icon = string.IsNullOrEmpty(c.Icon)
                    ? $"{baseUrl}/uploads/default-icon.jpg" // Nếu không có icon, dùng icon mặc định
                    : (c.Icon.StartsWith("http") ? c.Icon : $"{baseUrl}/{c.Icon}")
            }).ToList();

            return Ok(result);
        }

        [HttpPost("add-category")]
        public async Task<IActionResult> AddCategory(
        [FromForm] string tenDanhMuc,
        [FromForm] int maDanhMucCha)
        {
            try
            {
                // ✅ 1. Kiểm tra dữ liệu đầu vào
                if (maDanhMucCha <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Danh mục cha không hợp lệ!"
                    });
                }

                if (string.IsNullOrWhiteSpace(tenDanhMuc))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Tên danh mục không được để trống!"
                    });
                }

                // ✅ 2. Kiểm tra danh mục cha có tồn tại hay không
                var parentCategory = await _context.DanhMucChas.FindAsync(maDanhMucCha);
                if (parentCategory == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Danh mục cha không tồn tại!"
                    });
                }

                // ✅ 3. Chuẩn hóa tên và kiểm tra trùng lặp
                var normalizedName = tenDanhMuc.Trim().ToLower();
                bool isDuplicate = await _context.DanhMucs.AnyAsync(dm =>
                    dm.MaDanhMucCha == maDanhMucCha &&
                    dm.TenDanhMuc.Trim().ToLower() == normalizedName);

                if (isDuplicate)
                {
                    return Conflict(new
                    {
                        success = false,
                        message = $"Đã tồn tại danh mục con '{tenDanhMuc.Trim()}' trong danh mục cha này!"
                    });
                }

                // ✅ 4. Thêm mới danh mục
                var danhMucMoi = new DanhMuc
                {
                    TenDanhMuc = tenDanhMuc.Trim(),
                    MaDanhMucCha = maDanhMucCha
                };

                _context.DanhMucs.Add(danhMucMoi);
                await _context.SaveChangesAsync();

                // ✅ 5. Xóa cache khi thêm mới để tránh dữ liệu cũ
                _memoryCache.Remove(CategoryCacheKey);

                // ✅ 6. Trả về kết quả thành công
                return Ok(new
                {
                    success = true,
                    message = "Thêm danh mục thành công!"
                });
            }
            catch (Exception ex)
            {
                // ✅ 7. Xử lý lỗi hệ thống
                return StatusCode(500, new
                {
                    success = false,
                    message = "Lỗi server: " + ex.Message
                });
            }
        }


        [HttpPost("add-parent-category")]
        public async Task<IActionResult> AddParentCategory(
            [FromForm] CategoryCreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TenDanhMucCha))
                return BadRequest("Tên danh mục không được để trống!");

            bool exists = await _context.DanhMucChas.AnyAsync(d => d.TenDanhMucCha == request.TenDanhMucCha);
            if (exists)
                return BadRequest("Danh mục cha đã tồn tại!");

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/categories");
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string? imageUrl = null;
            if (request.AnhDanhMucCha != null)
            {
                var imageFileName = $"{Guid.NewGuid()}_{Path.GetFileName(request.AnhDanhMucCha.FileName)}";
                var imagePath = Path.Combine(folderPath, imageFileName);
                using (var stream = new FileStream(imagePath, FileMode.Create))
                {
                    await request.AnhDanhMucCha.CopyToAsync(stream);
                }
                imageUrl = $"/images/categories/{imageFileName}";
            }

            string? iconUrl = null;
            if (request.Icon != null)
            {
                var iconFileName = $"{Guid.NewGuid()}_{Path.GetFileName(request.Icon.FileName)}";
                var iconPath = Path.Combine(folderPath, iconFileName);
                using (var stream = new FileStream(iconPath, FileMode.Create))
                {
                    await request.Icon.CopyToAsync(stream);
                }
                iconUrl = $"/images/categories/{iconFileName}";
            }

            var newCategory = new DanhMucCha
            {
                TenDanhMucCha = request.TenDanhMucCha,
                AnhDanhMucCha = imageUrl,
                Icon = iconUrl
            };

            _context.DanhMucChas.Add(newCategory);
            await _context.SaveChangesAsync();

            // Xóa cache khi thêm mới
            _memoryCache.Remove(CategoryCacheKey);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            return Ok(new
            {
                Message = "Thêm danh mục cha thành công!",
                AnhDanhMucCha = imageUrl != null ? $"{baseUrl}{imageUrl}" : null,
                Icon = iconUrl != null ? $"{baseUrl}{iconUrl}" : null
            });
        }

        //hàm update danh mục con
        [HttpPut("update-category/{id}")]
        public async Task<IActionResult> UpdateCategory(int id, [FromBody] UpdateCategoryModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.TenDanhMuc))
            {
                return BadRequest("Thông tin danh mục không hợp lệ.");
            }

            var danhMuc = await _context.DanhMucs.FindAsync(id);
            if (danhMuc == null)
            {
                return NotFound("Danh mục không tồn tại.");
            }

            danhMuc.TenDanhMuc = model.TenDanhMuc;
            danhMuc.MaDanhMucCha = model.DanhMucChaId;

            await _context.SaveChangesAsync();

            // Xóa cache khi cập nhật
            _memoryCache.Remove(CategoryCacheKey);

            return Ok(new { message = "Cập nhật danh mục thành công!" });
        }
        // xóa danh mục con
        [HttpDelete("delete-category/{id}")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            var category = await _context.DanhMucs.FindAsync(id);
            if (category == null)
            {
                return NotFound(new { message = "Danh mục không tồn tại" });
            }

            bool hasSubCategories = await _context.DanhMucs.AnyAsync(d => d.MaDanhMucCha == id);
            if (hasSubCategories)
            {
                return BadRequest(new { message = "Không thể xóa danh mục này vì có danh mục con liên quan!" });
            }

            _context.DanhMucs.Remove(category);

            try
            {
                await _context.SaveChangesAsync();

                // Xóa cache khi xóa
                _memoryCache.Remove(CategoryCacheKey);

                return Ok(new { message = "Xóa danh mục thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi xóa danh mục!", error = ex.Message });
            }
        }
        // lấy danh sách danh mục con
        [HttpGet("get-categories")]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.DanhMucs
                .Join(_context.DanhMucChas,
                      dm => dm.MaDanhMucCha,
                      dmc => dmc.MaDanhMucCha,
                      (dm, dmc) => new {
                          dm.MaDanhMuc,
                          dm.TenDanhMuc,
                          dm.MaDanhMucCha,
                          TenDanhMucCha = dmc.TenDanhMucCha
                      })
                .ToListAsync();

            return Ok(categories);
        }
        // cập nhập danh mục cha 
        [HttpPut("update-parent-category/{id}")]
        public async Task<IActionResult> UpdateParentCategory(
            int id,
            [FromForm] CategoryCreateRequest request)
        {
            var category = await _context.DanhMucChas.FindAsync(id);
            if (category == null)
            {
                return NotFound(new { message = "Danh mục cha không tồn tại!" });
            }

            if (string.IsNullOrWhiteSpace(request.TenDanhMucCha))
            {
                return BadRequest(new { message = "Tên danh mục không được để trống!" });
            }

            category.TenDanhMucCha = request.TenDanhMucCha;

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/categories");
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string baseUrl = $"{Request.Scheme}://{Request.Host}";

            if (request.AnhDanhMucCha != null)
            {
                string imageFileName = $"{Guid.NewGuid()}_{Path.GetFileName(request.AnhDanhMucCha.FileName)}";
                string imagePath = Path.Combine(folderPath, imageFileName);
                using (var stream = new FileStream(imagePath, FileMode.Create))
                {
                    await request.AnhDanhMucCha.CopyToAsync(stream);
                }
                category.AnhDanhMucCha = $"/images/categories/{imageFileName}";
            }

            if (request.Icon != null)
            {
                string iconFileName = $"{Guid.NewGuid()}_{Path.GetFileName(request.Icon.FileName)}";
                string iconPath = Path.Combine(folderPath, iconFileName);
                using (var stream = new FileStream(iconPath, FileMode.Create))
                {
                    await request.Icon.CopyToAsync(stream);
                }
                category.Icon = $"/images/categories/{iconFileName}";
            }

            try
            {
                await _context.SaveChangesAsync();

                // Xóa cache khi cập nhật
                _memoryCache.Remove(CategoryCacheKey);

                return Ok(new
                {
                    message = "Cập nhật danh mục cha thành công!",
                    AnhDanhMucCha = category.AnhDanhMucCha != null ? $"{baseUrl}{category.AnhDanhMucCha}" : null,
                    Icon = category.Icon != null ? $"{baseUrl}{category.Icon}" : null
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi cập nhật danh mục!", error = ex.Message });
            }
        }
        // xóa danh mục cha 
        [HttpDelete("delete-parent-category/{id}")]
        public async Task<IActionResult> DeleteParentCategory(int id)
        {
            var category = await _context.DanhMucChas.FindAsync(id);
            if (category == null)
            {
                return NotFound(new { message = "Danh mục cha không tồn tại!" });
            }

            bool hasSubCategories = await _context.DanhMucs.AnyAsync(d => d.MaDanhMucCha == id);
            if (hasSubCategories)
            {
                return BadRequest(new { message = "Không thể xóa danh mục cha vì có danh mục con liên quan!" });
            }

            if (!string.IsNullOrEmpty(category.AnhDanhMucCha))
            {
                var imageFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", category.AnhDanhMucCha.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(imageFilePath))
                {
                    System.IO.File.Delete(imageFilePath);
                }
            }
            if (!string.IsNullOrEmpty(category.Icon))
            {
                var iconFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", category.Icon.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(iconFilePath))
                {
                    System.IO.File.Delete(iconFilePath);
                }
            }

            _context.DanhMucChas.Remove(category);

            try
            {
                await _context.SaveChangesAsync();

                // Xóa cache khi xóa
                _memoryCache.Remove(CategoryCacheKey);

                return Ok(new { message = "Xóa danh mục cha thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi xóa danh mục!", error = ex.Message });
            }
        }


        [HttpPost("approve-post/{id}")]
        public async Task<IActionResult> ApprovePost(int id)
        {
            var post = await _context.TinDangs
                .Include(p => p.AnhTinDangs) // Include để kiểm tra VideoUrl
                .FirstOrDefaultAsync(p => p.MaTinDang == id);

            if (post == null)
                return NotFound("Tin đăng không tồn tại!");

            if (post.TrangThai == TrangThaiTinDang.DaDuyet)
                return BadRequest("Tin đăng này đã được duyệt rồi.");

            if (post.TrangThai == TrangThaiTinDang.TuChoi)
                return BadRequest("Tin đăng này đã bị từ chối, không thể duyệt lại.");

            // Vì file đã upload lên Cloudinary khi đăng,
            // chúng ta không cần xử lý file nữa.

            // Chỉ kiểm tra lại VideoUrl cho chắc chắn
            if (string.IsNullOrEmpty(post.VideoUrl))
            {
                var firstVideo = post.AnhTinDangs
                    .FirstOrDefault(m => m.LoaiMedia == MediaType.Video);

                if (firstVideo != null)
                {
                    // DuongDan này đã là link Cloudinary
                    post.VideoUrl = firstVideo.DuongDan;
                }
            }

            // Cập nhật trạng thái
            post.TrangThai = TrangThaiTinDang.DaDuyet;
            _context.TinDangs.Update(post);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tin đăng đã được duyệt thành công!" });
        }


        [HttpPost("reject-post/{id}")]
        public async Task<IActionResult> RejectPost(int id)
        {
            var post = await _context.TinDangs
                // Không cần Include AnhTinDangs nữa
                .FirstOrDefaultAsync(p => p.MaTinDang == id);

            if (post == null)
                return NotFound("Tin đăng không tồn tại!");

            if (post.TrangThai == TrangThaiTinDang.TuChoi)
                return BadRequest("Tin đăng này đã bị từ chối rồi.");

            // ❌ KHÔNG XÓA KHỎI CLOUDINARY
            // ❌ KHÔNG XÓA KHỎI BẢNG AnhTinDangs

            // ✅ CHỈ CẬP NHẬT TRẠNG THÁI VÀ LÊN LỊCH
            post.TrangThai = TrangThaiTinDang.TuChoi;
            post.NgayHenXoa = DateTime.UtcNow.AddDays(3); // Hẹn xóa sau 3 ngày
            post.VideoUrl = null;

            _context.TinDangs.Update(post);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tin đăng đã bị từ chối và sẽ được xóa media sau 3 ngày." });
        }


        public class UpdateCategoryModel
        {
            public string TenDanhMuc { get; set; }
            public int DanhMucChaId { get; set; }
        }

        // Model thay đổi vai trò
        public class ChangeRoleModel
        {
            public string UserId { get; set; }
            public string NewRole { get; set; }
        }

        // Model cho API gộp thêm nhân viên và thay đổi vai trò
        public class EmployeeRoleModel
        {
            public string FullName { get; set; }
            public string Email { get; set; }
            public string PhoneNumber { get; set; }
            public string Role { get; set; }
            public string Password { get; set; }
        }

    }
}