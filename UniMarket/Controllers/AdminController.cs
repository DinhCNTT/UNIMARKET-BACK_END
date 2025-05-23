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

namespace UniMarket.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [EnableCors("_myAllowSpecificOrigins")] // Áp dụng CORS cho controller này
    public class AdminController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context; // ✅ Định nghĩa biến _context
        private readonly PhotoService _photoService;


        public AdminController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ApplicationDbContext context, PhotoService photoService) // 🔥 Thêm ApplicationDbContext vào DI
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context; // ✅ Gán _context
            _photoService = photoService;
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
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var roleExists = await _roleManager.RoleExistsAsync(model.Role);
            if (!roleExists)
                return BadRequest("Vai trò không hợp lệ!");

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    EmailConfirmed = true,
                    FullName = model.FullName,
                    PhoneNumber = model.PhoneNumber
                };

                var result = await _userManager.CreateAsync(user, model.Password);
                if (!result.Succeeded)
                    return BadRequest(result.Errors);
            }
            else
            {
                user.FullName = model.FullName;
                user.PhoneNumber = model.PhoneNumber;

                if (!string.IsNullOrEmpty(model.Password))
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var passwordResult = await _userManager.ResetPasswordAsync(user, token, model.Password);

                    if (!passwordResult.Succeeded)
                        return BadRequest(passwordResult.Errors);
                }

                await _userManager.UpdateAsync(user);
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, model.Role);

            return Ok(new { message = $"Nhân viên {user.Email} đã được cập nhật với vai trò {model.Role}." });
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
            if (maDanhMucCha == 0)
            {
                return BadRequest("Danh mục con bắt buộc phải có danh mục cha!");
            }

            var parentCategory = await _context.DanhMucChas.FindAsync(maDanhMucCha);
            if (parentCategory == null)
            {
                return BadRequest("Mã danh mục cha không hợp lệ!");
            }

            // **Lưu vào Database**
            var danhMucMoi = new DanhMuc
            {
                TenDanhMuc = tenDanhMuc,
                MaDanhMucCha = maDanhMucCha
            };

            _context.DanhMucs.Add(danhMucMoi);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Danh mục đã được thêm thành công!" });
        }



        [HttpPost("add-parent-category")]
        public async Task<IActionResult> AddParentCategory(
    [FromForm] string tenDanhMucCha,
    [FromForm] IFormFile? anhDanhMucCha,
    [FromForm] IFormFile? icon)
        {
            if (string.IsNullOrWhiteSpace(tenDanhMucCha))
                return BadRequest("Tên danh mục không được để trống!");

            bool exists = await _context.DanhMucChas.AnyAsync(d => d.TenDanhMucCha == tenDanhMucCha);
            if (exists)
                return BadRequest("Danh mục cha đã tồn tại!");

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/categories");
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string? imageUrl = null;
            if (anhDanhMucCha != null)
            {
                var imageFileName = $"{Guid.NewGuid()}_{Path.GetFileName(anhDanhMucCha.FileName)}";
                var imagePath = Path.Combine(folderPath, imageFileName);

                using (var stream = new FileStream(imagePath, FileMode.Create))
                {
                    await anhDanhMucCha.CopyToAsync(stream);
                }

                imageUrl = $"/images/categories/{imageFileName}";
            }

            string? iconUrl = null;
            if (icon != null)
            {
                var iconFileName = $"{Guid.NewGuid()}_{Path.GetFileName(icon.FileName)}";
                var iconPath = Path.Combine(folderPath, iconFileName);

                using (var stream = new FileStream(iconPath, FileMode.Create))
                {
                    await icon.CopyToAsync(stream);
                }

                iconUrl = $"/images/categories/{iconFileName}";
            }

            var newCategory = new DanhMucCha
            {
                TenDanhMucCha = tenDanhMucCha,
                AnhDanhMucCha = imageUrl,
                Icon = iconUrl
            };

            _context.DanhMucChas.Add(newCategory);
            await _context.SaveChangesAsync();

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

            // Kiểm tra xem danh mục con có chứa sản phẩm hoặc danh mục con khác không
            bool hasSubCategories = await _context.DanhMucs.AnyAsync(d => d.MaDanhMucCha == id);
            if (hasSubCategories)
            {
                return BadRequest(new { message = "Không thể xóa danh mục này vì có danh mục con liên quan!" });
            }

            _context.DanhMucs.Remove(category);

            try
            {
                await _context.SaveChangesAsync();
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
        public async Task<IActionResult> UpdateParentCategory(int id, [FromForm] string tenDanhMucCha, [FromForm] IFormFile? anhDanhMuc, [FromForm] IFormFile? icon)
        {
            var category = await _context.DanhMucChas.FindAsync(id);
            if (category == null)
            {
                return NotFound(new { message = "Danh mục cha không tồn tại!" });
            }

            if (string.IsNullOrWhiteSpace(tenDanhMucCha))
            {
                return BadRequest(new { message = "Tên danh mục không được để trống!" });
            }

            category.TenDanhMucCha = tenDanhMucCha;

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/categories");
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string baseUrl = $"{Request.Scheme}://{Request.Host}";

            // Xử lý cập nhật ảnh
            if (anhDanhMuc != null)
            {
                string imageFileName = $"{Guid.NewGuid()}_{Path.GetFileName(anhDanhMuc.FileName)}";
                string imagePath = Path.Combine(folderPath, imageFileName);

                using (var stream = new FileStream(imagePath, FileMode.Create))
                {
                    await anhDanhMuc.CopyToAsync(stream);
                }

                category.AnhDanhMucCha = $"/images/categories/{imageFileName}";
            }

            // Xử lý cập nhật icon
            if (icon != null)
            {
                string iconFileName = $"{Guid.NewGuid()}_{Path.GetFileName(icon.FileName)}";
                string iconPath = Path.Combine(folderPath, iconFileName);

                using (var stream = new FileStream(iconPath, FileMode.Create))
                {
                    await icon.CopyToAsync(stream);
                }

                category.Icon = $"/images/categories/{iconFileName}";
            }

            try
            {
                await _context.SaveChangesAsync();
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

            // Xóa ảnh
            if (!string.IsNullOrEmpty(category.AnhDanhMucCha))
            {
                var imageFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", category.AnhDanhMucCha.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(imageFilePath))
                {
                    System.IO.File.Delete(imageFilePath);
                }
            }

            // Xóa icon
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
                .Include(p => p.AnhTinDangs)
                .FirstOrDefaultAsync(p => p.MaTinDang == id);

            if (post == null)
                return NotFound("Tin đăng không tồn tại!");

            if (post.TrangThai == TrangThaiTinDang.DaDuyet)
                return BadRequest("Tin đăng này đã được duyệt rồi.");

            foreach (var media in post.AnhTinDangs)
            {
                if (!media.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(media.DuongDan);
                    var localFilePath = Path.Combine("wwwroot", "images", "temp-uploads", fileName);

                    if (!System.IO.File.Exists(localFilePath))
                        continue;

                    byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(localFilePath);
                    using var memoryStream = new MemoryStream(fileBytes);

                    var formFile = new FormFile(memoryStream, 0, memoryStream.Length, null, fileName);

                    // Phân biệt ảnh/video dựa vào đuôi file
                    string ext = Path.GetExtension(fileName).ToLower();
                    if (ext == ".mp4" || ext == ".mov" || ext == ".avi")
                    {
                        var uploadResult = await _photoService.UploadVideoAsync(formFile);
                        if (uploadResult.Error != null)
                            return BadRequest(new { message = "Lỗi khi upload video lên Cloudinary", error = uploadResult.Error.Message });

                        media.DuongDan = uploadResult.SecureUrl.ToString();
                    }
                    else
                    {
                        var uploadResult = await _photoService.UploadPhotoAsync(formFile);
                        if (uploadResult.Error != null)
                            return BadRequest(new { message = "Lỗi khi upload ảnh lên Cloudinary", error = uploadResult.Error.Message });

                        media.DuongDan = uploadResult.SecureUrl.ToString();
                    }

                    System.IO.File.Delete(localFilePath);
                }
            }

            post.TrangThai = TrangThaiTinDang.DaDuyet;
            _context.TinDangs.Update(post);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tin đăng đã được duyệt và media đã được lưu trên Cloudinary!" });
        }

        [HttpPost("reject-post/{id}")]
        public async Task<IActionResult> RejectPost(int id)
        {
            var post = await _context.TinDangs
                .Include(p => p.AnhTinDangs)
                .FirstOrDefaultAsync(p => p.MaTinDang == id);

            if (post == null)
                return NotFound("Tin đăng không tồn tại!");

            if (post.TrangThai == TrangThaiTinDang.TuChoi)
                return BadRequest("Tin đăng này đã bị từ chối rồi.");

            foreach (var media in post.AnhTinDangs)
            {
                if (string.IsNullOrEmpty(media.DuongDan))
                    continue;

                if (!media.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(media.DuongDan);
                    var localFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "temp-uploads", fileName);

                    if (System.IO.File.Exists(localFilePath))
                    {
                        byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(localFilePath);
                        using var memoryStream = new MemoryStream(fileBytes);
                        var formFile = new FormFile(memoryStream, 0, memoryStream.Length, null, fileName);

                        string ext = Path.GetExtension(fileName).ToLower();
                        if (ext == ".mp4" || ext == ".mov" || ext == ".avi")
                        {
                            var uploadResult = await _photoService.UploadVideoAsync(formFile);
                            if (uploadResult.Error != null)
                                return BadRequest(new { message = "Lỗi khi upload video lên Cloudinary", error = uploadResult.Error.Message });

                            media.DuongDan = uploadResult.SecureUrl.ToString();
                        }
                        else
                        {
                            var uploadResult = await _photoService.UploadPhotoAsync(formFile);
                            if (uploadResult.Error != null)
                                return BadRequest(new { message = "Lỗi khi upload ảnh lên Cloudinary", error = uploadResult.Error.Message });

                            media.DuongDan = uploadResult.SecureUrl.ToString();
                        }

                        System.IO.File.Delete(localFilePath);
                    }
                }
            }

            post.TrangThai = TrangThaiTinDang.TuChoi;
            _context.TinDangs.Update(post);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tin đăng đã bị từ chối, media đã được lưu trên Cloudinary và ảnh temp đã xóa!" });
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
