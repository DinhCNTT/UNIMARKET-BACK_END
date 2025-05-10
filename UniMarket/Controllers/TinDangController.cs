using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;
using Microsoft.AspNetCore.Http;
using System.IO;
using UniMarket.DTO;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TinDangController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly string _imagesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "categories");

        public TinDangController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet("get-posts")]
        public IActionResult GetPosts()
        {
            var posts = _context.TinDangs
                .Where(p => p.TrangThai == TrangThaiTinDang.DaDuyet) // Chỉ lấy tin đã duyệt
                .Include(p => p.NguoiBan)
                .Include(p => p.TinhThanh)
                .Include(p => p.QuanHuyen)
                .Include(p => p.AnhTinDangs)
                .Include(p => p.DanhMuc) // Bao gồm thông tin danh mục
                    .ThenInclude(dm => dm.DanhMucCha) // Bao gồm thông tin danh mục cha
                .Select(p => new
                {
                    p.MaTinDang,
                    p.TieuDe,
                    p.MoTa,
                    p.Gia,
                    p.CoTheThoaThuan,
                    p.TinhTrang,
                    p.DiaChi,
                    p.MaTinhThanh,
                    p.MaQuanHuyen,
                    p.MaNguoiBan,
                    p.NgayDang,
                    Images = p.AnhTinDangs.Select(a =>
                        a.DuongDan.StartsWith("/images/Posts/")
                            ? a.DuongDan
                            : $"/images/Posts/{a.DuongDan}"
                    ).ToList(),
                    NguoiBan = p.NguoiBan.FullName,
                    TinhThanh = p.TinhThanh.TenTinhThanh,
                    QuanHuyen = p.QuanHuyen.TenQuanHuyen,
                    DanhMuc = p.DanhMuc.TenDanhMuc, // Thêm tên danh mục con
                    DanhMucCha = p.DanhMuc.DanhMucCha.TenDanhMucCha // Thêm tên danh mục cha
                })
                .ToList();

            if (posts == null || !posts.Any())
            {
                return NotFound("Không có tin đăng nào.");
            }

            return Ok(posts);
        }



        [HttpPost("add-post")]
        public async Task<IActionResult> AddPost(
    [FromForm] string title,
    [FromForm] string description,
    [FromForm] decimal price,
    [FromForm] string contactInfo,
    [FromForm] string condition,
    [FromForm] int province,
    [FromForm] int district,
    [FromForm] IFormFile image,
    [FromForm] string userId,
    [FromForm] int categoryId,  // Nhận categoryId từ frontend
    [FromForm] string categoryName, // Nhận categoryName từ frontend
    [FromForm] bool canNegotiate) // Nhận trường "Có thể thương lượng"
        {
            // Kiểm tra xem người bán có tồn tại không
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return BadRequest("Người bán không tồn tại!");
            }

            // Kiểm tra xem tỉnh và quận có tồn tại không
            var provinceExists = await _context.TinhThanhs.AnyAsync(t => t.MaTinhThanh == province);
            if (!provinceExists)
            {
                return BadRequest("Tỉnh thành không hợp lệ!");
            }

            var districtExists = await _context.QuanHuyens.AnyAsync(q => q.MaQuanHuyen == district);
            if (!districtExists)
            {
                return BadRequest("Quận huyện không hợp lệ!");
            }

            // Tạo bài đăng
            var post = new TinDang
            {
                TieuDe = title,
                MoTa = description,
                Gia = price,
                CoTheThoaThuan = canNegotiate, // Gán giá trị "Có thể thương lượng"
                TinhTrang = condition,
                DiaChi = contactInfo,
                MaTinhThanh = province,
                MaQuanHuyen = district,
                MaNguoiBan = userId, // Gắn mã người bán
                NgayDang = DateTime.Now,
                TrangThai = TrangThaiTinDang.ChoDuyet, // Mặc định là Chờ duyệt
                MaDanhMuc = categoryId // Gắn mã danh mục
            };

            // Lưu ảnh nếu có
            if (image != null)
            {
                string uploadPath = Path.Combine("wwwroot", "images", "Posts");

                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                }

                var filePath = Path.Combine(uploadPath, image.FileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }

                var postImage = new AnhTinDang
                {
                    DuongDan = $"/images/Posts/{image.FileName}",
                    TinDang = post
                };

                post.AnhTinDangs = new List<AnhTinDang> { postImage };
            }

            // Lưu bài đăng vào cơ sở dữ liệu
            _context.TinDangs.Add(post);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Bài đăng đã được thêm thành công!" });
        }

        [HttpGet("get-posts-admin")]
        public IActionResult getpotsadmin()
        {
            var posts = _context.TinDangs
                .Include(p => p.NguoiBan)
                .Include(p => p.TinhThanh) // Bao gồm thông tin tỉnh thành
                .Include(p => p.QuanHuyen) // Bao gồm thông tin quận huyện
                .Include(p => p.AnhTinDangs) // Bao gồm thông tin hình ảnh (nếu có bảng AnhTinDang)
                .Select(p => new
                {
                    p.MaTinDang,
                    p.TieuDe,
                    p.TrangThai,
                    NguoiBan = p.NguoiBan.FullName,
                    p.Gia,  // Thêm giá
                    p.MoTa, // Thêm mô tả
                    HinhAnh = p.AnhTinDangs.Select(a => a.DuongDan), // Lấy đường dẫn hình ảnh từ bảng AnhTinDang
                    p.NgayDang,
                    TinhThanh = p.TinhThanh.TenTinhThanh, // Lấy tên tỉnh thành
                    QuanHuyen = p.QuanHuyen.TenQuanHuyen // Lấy tên quận huyện
                })
                .ToList();

            if (posts == null || !posts.Any())
            {
                return NotFound("Không có tin đăng nào.");
            }

            return Ok(posts);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutTinDang(int id, [FromForm] TinDang tinDang, IFormFile? image)
        {
            var existingTinDang = await _context.TinDangs
                .Include(td => td.AnhTinDangs)
                .FirstOrDefaultAsync(td => td.MaTinDang == id);

            if (existingTinDang == null)
            {
                return NotFound(new { message = "Không tìm thấy tin đăng" });
            }

            // Cập nhật các thông tin của tin đăng
            existingTinDang.TieuDe = tinDang.TieuDe;
            existingTinDang.MoTa = tinDang.MoTa;
            existingTinDang.Gia = tinDang.Gia;
            existingTinDang.CoTheThoaThuan = tinDang.CoTheThoaThuan;
            existingTinDang.TinhTrang = tinDang.TinhTrang;
            existingTinDang.DiaChi = tinDang.DiaChi;
            existingTinDang.NgayCapNhat = DateTime.Now;

            // Xử lý ảnh mới nếu có
            if (image != null)
            {
                // Xóa ảnh cũ khỏi danh sách
                if (existingTinDang.AnhTinDangs != null && existingTinDang.AnhTinDangs.Count > 0)
                {
                    existingTinDang.AnhTinDangs.Clear();
                }

                // Tạo tên file ngẫu nhiên
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                var filePath = Path.Combine("wwwroot/images/Posts", fileName);

                // Tạo folder nếu chưa có
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                // Lưu ảnh mới vào thư mục
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }

                var postImage = new AnhTinDang
                {
                    DuongDan = $"/images/Posts/{fileName}",   // Chuẩn đường dẫn FE
                    TinDang = existingTinDang
                };

                existingTinDang.AnhTinDangs ??= new List<AnhTinDang>();
                existingTinDang.AnhTinDangs.Add(postImage);
            }

            try
            {
                // Lưu thay đổi vào cơ sở dữ liệu
                await _context.SaveChangesAsync();

                // Trả về thông báo thành công và danh sách ảnh của tin đăng
                return Ok(new { message = "Cập nhật tin đăng thành công", AnhTinDang = existingTinDang.AnhTinDangs });
            }
            catch (Exception ex)
            {
                // Trả về lỗi nếu có bất kỳ ngoại lệ nào xảy ra
                return StatusCode(500, new { message = "Lỗi khi cập nhật tin đăng", error = ex.Message });
            }
        }



        [HttpGet("get-post/{id}")]
        public async Task<IActionResult> GetPostById(int id)
        {
            // Tìm tin đăng trong cơ sở dữ liệu theo ID và bao gồm thông tin về danh mục
            var post = await _context.TinDangs
                .Include(p => p.AnhTinDangs)  // Bao gồm các ảnh tin đăng nếu có
                .Include(p => p.DanhMuc)      // Bao gồm thông tin danh mục
                .FirstOrDefaultAsync(p => p.MaTinDang == id);  // Lọc theo ID tin đăng

            if (post == null)
            {
                return NotFound(new { message = "Không tìm thấy tin đăng với mã này." });
            }

            // Trả về thông tin tin đăng dưới dạng JSON, bao gồm cả mã danh mục
            return Ok(post);
        }



        // DELETE: api/tindang/{id} (Xóa tin đăng)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTinDang(int id)
        {
            var tinDang = await _context.TinDangs.FindAsync(id);
            if (tinDang == null)
            {
                return NotFound(new { message = "Không tìm thấy tin đăng" });
            }

            _context.TinDangs.Remove(tinDang);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Xóa tin đăng thành công" });
        }

        [HttpGet("xemtruoc/{id}")]
        public async Task<ActionResult<TinDang>> XemTruocTinDang(int id)
        {
            var tinDang = await _context.TinDangs
                .Include(td => td.DanhMuc)
                .Include(td => td.NguoiBan)
                .FirstOrDefaultAsync(td => td.MaTinDang == id);

            if (tinDang == null)
            {
                return NotFound(new { message = "Không tìm thấy tin đăng" });
            }

            // Trả về tin đăng dưới dạng xem trước (trạng thái chưa duyệt)
            tinDang.TrangThai = TrangThaiTinDang.ChoDuyet;
            return Ok(tinDang);
        }


        [HttpGet("user/{userId}")]
        public IActionResult GetPostsByUser(string userId)
        {
            var posts = _context.TinDangs
                .Where(p => p.MaNguoiBan == userId)
                .Include(p => p.AnhTinDangs)
                .Include(p => p.NguoiBan) // ✅ Thêm dòng này để lấy tên người bán
                .Select(p => new
                {
                    p.MaTinDang,
                    p.TieuDe,
                    p.MoTa,
                    p.Gia,
                    p.TrangThai,
                    p.NgayDang,
                    NguoiBan = p.NguoiBan.FullName, // ✅ Trả về tên người bán
                    Images = p.AnhTinDangs.Select(a =>
                        a.DuongDan.StartsWith("/images/Posts/")
                            ? a.DuongDan
                            : $"/images/Posts/{a.DuongDan}"
                    ).ToList()
                })
                .ToList();

            return Ok(posts);
        }
        // GET: api/tindang/tinhthanh
        [HttpGet("tinhthanh")]
        public async Task<ActionResult<IEnumerable<TinhThanhDTO>>> GetTinhThanhs()
        {
            var tinhThanhs = await _context.TinhThanhs
                .Include(tt => tt.QuanHuyens)  // Load danh sách quận/huyện
                .Select(tt => new TinhThanhDTO
                {
                    MaTinhThanh = tt.MaTinhThanh,
                    TenTinhThanh = tt.TenTinhThanh,
                    QuanHuyens = tt.QuanHuyens.Select(qh => new QuanHuyenDTO
                    {
                        MaQuanHuyen = qh.MaQuanHuyen,
                        TenQuanHuyen = qh.TenQuanHuyen
                    }).ToList()
                })
                .ToListAsync();

            if (!tinhThanhs.Any())
            {
                return NotFound(new { message = "Không có tỉnh thành nào trong cơ sở dữ liệu" });
            }

            return Ok(tinhThanhs);
        }

        // GET: api/tindang/tinhthanh/{maTinhThanh}/quanhuynh
        [HttpGet("tinhthanh/{maTinhThanh}/quanhuynh")]
        public async Task<ActionResult<IEnumerable<QuanHuyenDTO>>> GetQuanHuyensByTinhThanh(int maTinhThanh)
        {
            var quanHuyens = await _context.QuanHuyens
                .Where(qh => qh.MaTinhThanh == maTinhThanh)
                .Select(qh => new QuanHuyenDTO
                {
                    MaQuanHuyen = qh.MaQuanHuyen,
                    TenQuanHuyen = qh.TenQuanHuyen
                })
                .ToListAsync();

            if (!quanHuyens.Any())
            {
                return NotFound(new { message = "Không tìm thấy quận/huyện cho tỉnh/thành này." });
            }

            return Ok(quanHuyens);
        }


        [HttpGet("get-post-and-similar/{id}")]
        public async Task<IActionResult> GetPostAndSimilarPosts(int id)
        {
            // Lấy chi tiết tin đăng theo ID
            var post = await _context.TinDangs
                .Include(p => p.AnhTinDangs)
                .Include(p => p.NguoiBan) // Bao gồm thông tin người bán
                .Include(p => p.TinhThanh)
                .Include(p => p.QuanHuyen)
                .FirstOrDefaultAsync(p => p.MaTinDang == id && p.TrangThai == TrangThaiTinDang.DaDuyet); // Thêm điều kiện chỉ lấy tin đã duyệt

            if (post == null)
            {
                return NotFound(new { message = "Không tìm thấy tin đăng này hoặc tin đăng chưa được duyệt." });
            }

            // Lấy các tin đăng tương tự theo danh mục con
            var similarPosts = await _context.TinDangs
                .Where(p => p.MaDanhMuc == post.MaDanhMuc && p.MaTinDang != post.MaTinDang && p.TrangThai == TrangThaiTinDang.DaDuyet) // Chỉ lấy tin đã duyệt
                .Include(p => p.AnhTinDangs)
                .Include(p => p.NguoiBan)
                .Include(p => p.TinhThanh)
                .Include(p => p.QuanHuyen)
                .Select(p => new
                {
                    p.MaTinDang,
                    p.TieuDe,
                    p.MoTa,
                    p.Gia,
                    p.TinhTrang,
                    p.DiaChi,
                    Images = p.AnhTinDangs.Select(a => a.DuongDan.StartsWith("/images/Posts/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}").ToList(),
                    NguoiBan = p.NguoiBan.FullName,
                    PhoneNumber = p.NguoiBan.PhoneNumber, // Thêm số điện thoại người bán
                    TinhThanh = p.TinhThanh.TenTinhThanh,
                    QuanHuyen = p.QuanHuyen.TenQuanHuyen
                })
                .ToListAsync();

            return Ok(new
            {
                Post = new
                {
                    post.MaTinDang,
                    post.TieuDe,
                    post.MoTa,
                    post.Gia,
                    post.TinhTrang,
                    post.DiaChi,
                    Images = post.AnhTinDangs.Select(a => a.DuongDan.StartsWith("/images/Posts/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}").ToList(),
                    NguoiBan = post.NguoiBan.FullName,
                    PhoneNumber = post.NguoiBan.PhoneNumber, // Thêm số điện thoại người bán
                    TinhThanh = post.TinhThanh.TenTinhThanh,
                    QuanHuyen = post.QuanHuyen.TenQuanHuyen,
                    NgayDang = post.NgayDang, // Thêm ngày đăng
                    NgayCapNhat = post.NgayCapNhat // Thêm ngày cập nhật
                },
                SimilarPosts = similarPosts
            });
        }
    }

}
