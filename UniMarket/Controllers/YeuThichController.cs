using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class YeuThichController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public YeuThichController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("luu/{maTinDang}")]
        public async Task<IActionResult> LuuTinDang(int maTinDang)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
                return Unauthorized(new { message = "Bạn chưa đăng nhập" });

            // Lấy thông tin user
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return Unauthorized(new { message = "Không tìm thấy tài khoản" });

            // Kiểm tra số điện thoại của người dùng
            if (string.IsNullOrEmpty(user.PhoneNumber))
                return BadRequest(new { message = "Bạn chưa nhập số điện thoại" });

            // Kiểm tra xác minh email
            if (!user.EmailConfirmed)
                return BadRequest(new { message = "Bạn chưa xác minh gmail" });

            // Kiểm tra xem tin đăng có tồn tại không
            var tinDang = await _context.TinDangs
                                        .FirstOrDefaultAsync(t => t.MaTinDang == maTinDang);
            if (tinDang == null)
                return BadRequest(new { message = "Tin đăng không tồn tại" });

            // Kiểm tra nếu tin đăng đã được lưu
            var daLuu = await _context.TinDangYeuThichs
                                       .AnyAsync(x => x.MaTinDang == maTinDang && x.MaNguoiDung == userId);
            if (daLuu)
                return BadRequest(new { message = "Tin đăng này đã được lưu trong danh sách yêu thích của bạn." });

            // Thêm tin đăng vào danh sách yêu thích
            _context.TinDangYeuThichs.Add(new TinDangYeuThich { MaTinDang = maTinDang, MaNguoiDung = userId });

            // Lưu vào DB
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã lưu tin đăng vào danh sách yêu thích." });
        }

        [HttpDelete("xoa/{maTinDang}")]
        public async Task<IActionResult> XoaTinDang(int maTinDang)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
                return Unauthorized(new { message = "Bạn chưa đăng nhập" });

            // Lấy thông tin user
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return Unauthorized(new { message = "Không tìm thấy tài khoản" });

            // Kiểm tra số điện thoại của người dùng
            if (string.IsNullOrEmpty(user.PhoneNumber))
                return BadRequest(new { message = "Bạn chưa nhập số điện thoại" });

            // Kiểm tra xác minh email
            if (!user.EmailConfirmed)
                return BadRequest(new { message = "Bạn chưa xác minh gmail" });

            var tin = await _context.TinDangYeuThichs
                                    .FirstOrDefaultAsync(x => x.MaTinDang == maTinDang && x.MaNguoiDung == userId);
            if (tin == null)
                return NotFound(new { message = "Tin đăng không có trong danh sách yêu thích của bạn." });

            // Xóa tin đăng khỏi danh sách yêu thích
            _context.TinDangYeuThichs.Remove(tin);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xoá tin đăng khỏi mục yêu thích." });
        }

        [HttpGet("danh-sach")]
        public async Task<IActionResult> LayDanhSach()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
                return Unauthorized(new { message = "Chưa đăng nhập" });

            var danhSach = await _context.TinDangYeuThichs
                .Where(x => x.MaNguoiDung == userId)
                .Include(x => x.TinDang)
                    .ThenInclude(td => td.AnhTinDangs)
                .Include(x => x.TinDang)
                    .ThenInclude(td => td.QuanHuyen)
                .Include(x => x.TinDang)
                    .ThenInclude(td => td.TinhThanh)
                .Select(x => new
                {
                    MaTinDang = x.TinDang.MaTinDang,
                    Images = x.TinDang.AnhTinDangs.Select(a =>
                        a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? a.DuongDan
                            : $"http://localhost:5133{a.DuongDan}"
    ).ToList(),
                    x.TinDang.TieuDe,
                    x.TinDang.Gia,
                    x.TinDang.DiaChi,
                    QuanHuyen = x.TinDang.QuanHuyen != null ? x.TinDang.QuanHuyen.TenQuanHuyen : null,
                    TinhThanh = x.TinDang.TinhThanh != null ? x.TinDang.TinhThanh.TenTinhThanh : null,
                    SavedCount = _context.TinDangYeuThichs.Count(y => y.MaTinDang == x.TinDang.MaTinDang) // Thêm dòng này
                })
                .ToListAsync();

            if (!danhSach.Any())
                return NotFound(new { message = "Không có tin đăng yêu thích nào" });

            return Ok(danhSach);
        }

    }

}
