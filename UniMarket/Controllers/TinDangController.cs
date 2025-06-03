using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;
using Microsoft.AspNetCore.Http;
using System.IO;
using UniMarket.DTO;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using UniMarket.Services;
using System.Text.Json; // ✅ thêm using
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Newtonsoft.Json;
using UniMarket.Hubs;
using Microsoft.AspNetCore.SignalR;
namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TinDangController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly string _imagesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "categories");
        private readonly PhotoService _photoService; // ✅ thêm
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ChatHub> _hubContext;
        public TinDangController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, PhotoService photoService, IWebHostEnvironment env, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _photoService = photoService;
            _env = env;
            _hubContext = hubContext;
        }

        [HttpGet("get-posts")]
        public IActionResult GetPosts()
        {
            var posts = _context.TinDangs
                .Where(p => p.TrangThai == TrangThaiTinDang.DaDuyet)
                .Include(p => p.NguoiBan)
                .Include(p => p.TinhThanh)
                .Include(p => p.QuanHuyen)
                .Include(p => p.AnhTinDangs)
                .Include(p => p.DanhMuc)
                    .ThenInclude(dm => dm.DanhMucCha)
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
                        a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? a.DuongDan
                            : (a.DuongDan.StartsWith("/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}")
                    ).ToList(),
                    NguoiBan = p.NguoiBan.FullName,
                    TinhThanh = p.TinhThanh.TenTinhThanh,
                    QuanHuyen = p.QuanHuyen.TenQuanHuyen,
                    DanhMuc = p.DanhMuc.TenDanhMuc,
                    DanhMucCha = p.DanhMuc.DanhMucCha.TenDanhMucCha
                })
                .ToList();

            if (posts == null || !posts.Any())
                return NotFound("Không có tin đăng nào.");

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
            [FromForm] List<IFormFile> images,
            [FromForm] string userId,
            [FromForm] int categoryId,
            [FromForm] string categoryName,
            [FromForm] bool canNegotiate)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return BadRequest("Người bán không tồn tại!");

            if (!await _context.TinhThanhs.AnyAsync(t => t.MaTinhThanh == province))
                return BadRequest("Tỉnh thành không hợp lệ!");

            if (!await _context.QuanHuyens.AnyAsync(q => q.MaQuanHuyen == district))
                return BadRequest("Quận huyện không hợp lệ!");

            // Kiểm tra giới hạn file
            if (images != null && images.Count > 8) // 7 ảnh + 1 video tối đa
                return BadRequest("Chỉ được phép tải lên tối đa 7 ảnh và 1 video.");

            // Phân loại ảnh và video
            var imageFiles = new List<IFormFile>();
            var videoFiles = new List<IFormFile>();

            if (images != null)
            {
                foreach (var file in images)
                {
                    var extension = Path.GetExtension(file.FileName).ToLower();
                    var isVideo = extension == ".mp4" || extension == ".mov" || extension == ".avi" ||
                                 extension == ".wmv" || extension == ".flv" || extension == ".webm";

                    if (isVideo)
                        videoFiles.Add(file);
                    else
                        imageFiles.Add(file);
                }

                // Kiểm tra giới hạn cụ thể
                if (imageFiles.Count > 7)
                    return BadRequest("Chỉ được phép tải lên tối đa 7 ảnh.");

                if (videoFiles.Count > 1)
                    return BadRequest("Chỉ được phép tải lên tối đa 1 video.");
            }

            var post = new TinDang
            {
                TieuDe = title,
                MoTa = description,
                Gia = price,
                CoTheThoaThuan = canNegotiate,
                TinhTrang = condition,
                DiaChi = contactInfo,
                MaTinhThanh = province,
                MaQuanHuyen = district,
                MaNguoiBan = userId,
                NgayDang = DateTime.Now,
                TrangThai = TrangThaiTinDang.ChoDuyet,
                MaDanhMuc = categoryId,
                AnhTinDangs = new List<AnhTinDang>()
            };

            if (images != null && images.Count > 0)
            {
                var tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "temp-uploads");
                if (!Directory.Exists(tempFolder))
                    Directory.CreateDirectory(tempFolder);

                int order = 1; // Thứ tự hiển thị

                // Xử lý ảnh trước
                foreach (var image in imageFiles)
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                    var filePath = Path.Combine(tempFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await image.CopyToAsync(stream);
                    }

                    post.AnhTinDangs.Add(new AnhTinDang
                    {
                        DuongDan = $"/images/temp-uploads/{fileName}",
                        LoaiMedia = MediaType.Image,
                        Order = order++,
                        TinDang = post
                    });
                }

                // Xử lý video sau
                foreach (var video in videoFiles)
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(video.FileName);
                    var filePath = Path.Combine(tempFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await video.CopyToAsync(stream);
                    }

                    post.AnhTinDangs.Add(new AnhTinDang
                    {
                        DuongDan = $"/images/temp-uploads/{fileName}",
                        LoaiMedia = MediaType.Video,
                        Order = order++,
                        TinDang = post
                    });
                }
            }

            _context.TinDangs.Add(post);
            await _context.SaveChangesAsync();

            // Trả về thông tin chi tiết về số lượng file đã upload
            var responseMessage = $"Bài đăng đã được thêm thành công và đang chờ duyệt! " +
                                 $"(Đã tải lên: {imageFiles.Count} ảnh, {videoFiles.Count} video)";

            return Ok(new
            {
                message = responseMessage,
                imageCount = imageFiles.Count,
                videoCount = videoFiles.Count
            });
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
        public async Task<IActionResult> PutTinDang(
            int id,
            [FromForm] string title,
            [FromForm] string description,
            [FromForm] decimal price,
            [FromForm] string contactInfo,
            [FromForm] string condition,
            [FromForm] bool canNegotiate,
            [FromForm] int province,
            [FromForm] int district,
            [FromForm] int categoryId,
            [FromForm] string userId,
            [FromForm] List<IFormFile>? newImages,
            [FromForm] List<IFormFile>? newVideos,
            [FromForm] string? oldImagesToDelete,
            [FromForm] string? oldVideosToDelete,
            [FromForm] string? oldImageOrder,
            [FromForm] string? oldVideoOrder)
        {
            try
            {
                var post = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                if (post == null)
                    return NotFound(new { message = "Không tìm thấy tin đăng" });

                // Update basic information
                post.TieuDe = title;
                post.MoTa = description;
                post.Gia = price;
                post.DiaChi = contactInfo;
                post.TinhTrang = condition;
                post.CoTheThoaThuan = canNegotiate;
                post.MaTinhThanh = province;
                post.MaQuanHuyen = district;
                post.MaDanhMuc = categoryId;
                post.NgayCapNhat = DateTime.Now;

                // Parse JSON from frontend
                var idsToDeleteImage = string.IsNullOrEmpty(oldImagesToDelete) ? new List<int>() : JsonConvert.DeserializeObject<List<int>>(oldImagesToDelete);
                var idsToDeleteVideo = string.IsNullOrEmpty(oldVideosToDelete) ? new List<int>() : JsonConvert.DeserializeObject<List<int>>(oldVideosToDelete);
                var imageOrder = string.IsNullOrEmpty(oldImageOrder) ? new List<int>() : JsonConvert.DeserializeObject<List<int>>(oldImageOrder);
                var videoOrder = string.IsNullOrEmpty(oldVideoOrder) ? new List<int>() : JsonConvert.DeserializeObject<List<int>>(oldVideoOrder);

                var oldMediaList = post.AnhTinDangs.ToList();

                // Delete old media from Cloudinary
                foreach (var media in oldMediaList)
                {
                    if (idsToDeleteImage.Contains(media.MaAnh) || idsToDeleteVideo.Contains(media.MaAnh))
                    {
                        if (!string.IsNullOrEmpty(media.DuongDan) && media.DuongDan.StartsWith("http"))
                        {
                            await DeleteCloudinaryPhotoByUrlAsync(media.DuongDan);
                        }
                        _context.AnhTinDangs.Remove(media);
                    }
                }

                // Filter and reorder remaining media
                var remainingMedia = oldMediaList
                    .Where(m => imageOrder.Contains(m.MaAnh) || videoOrder.Contains(m.MaAnh))
                    .OrderBy(m =>
                        imageOrder.Contains(m.MaAnh) ? imageOrder.IndexOf(m.MaAnh)
                        : videoOrder.Contains(m.MaAnh) ? 100 + videoOrder.IndexOf(m.MaAnh)
                        : 999)
                    .ToList();

                post.AnhTinDangs = remainingMedia;

                int currentOrder = remainingMedia.Count > 0 ? remainingMedia.Max(a => a.Order) + 1 : 1;

                // Upload new images
                if (newImages != null)
                {
                    foreach (var img in newImages)
                    {
                        var result = await _photoService.UploadPhotoAsync(img);
                        if (result.Error != null)
                            return BadRequest(new { message = "Lỗi upload ảnh", error = result.Error.Message });

                        post.AnhTinDangs.Add(new AnhTinDang
                        {
                            DuongDan = result.SecureUrl.ToString(),
                            LoaiMedia = MediaType.Image,
                            Order = currentOrder++,
                            TinDang = post
                        });
                    }
                }

                // Upload new videos
                if (newVideos != null)
                {
                    foreach (var vid in newVideos)
                    {
                        var result = await _photoService.UploadVideoAsync(vid);
                        if (result.Error != null)
                            return BadRequest(new { message = "Lỗi upload video", error = result.Error.Message });

                        post.AnhTinDangs.Add(new AnhTinDang
                        {
                            DuongDan = result.SecureUrl.ToString(),
                            LoaiMedia = MediaType.Video,
                            Order = currentOrder++,
                            TinDang = post
                        });
                    }
                }

                // Save changes to DB
                await _context.SaveChangesAsync();

                // Send SignalR notification to all connected clients
                var updatedPost = new
                {
                    MaTinDang = post.MaTinDang,
                    TieuDe = post.TieuDe,
                    Gia = post.Gia,
                    AnhDaiDien = post.AnhTinDangs?.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan ?? ""
                };
                await _hubContext.Clients.All.SendAsync("CapNhatTinDang", updatedPost);

                return Ok(new
                {
                    message = "Cập nhật thành công",
                    post.MaTinDang,
                    AnhTinDangs = post.AnhTinDangs.Select(a => new { a.MaAnh, a.DuongDan, a.Order })
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ LỖI CẬP NHẬT TIN:");
                Console.WriteLine("Message: " + ex.Message);
                Console.WriteLine("StackTrace: " + ex.StackTrace);

                return StatusCode(500, new
                {
                    message = "Lỗi server",
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
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

        // Hàm xóa ảnh/video trên Cloudinary từ URL publicId
        public async Task<bool> DeleteCloudinaryPhotoByUrlAsync(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
                return false;

            try
            {
                var uri = new Uri(imageUrl);
                var segments = uri.Segments;

                // Tìm vị trí "upload/" hoặc "upload"
                int uploadIndex = segments.ToList().FindIndex(s => s.Equals("upload/", StringComparison.OrdinalIgnoreCase));
                if (uploadIndex < 0)
                {
                    uploadIndex = segments.ToList().FindIndex(s => s.StartsWith("upload", StringComparison.OrdinalIgnoreCase));
                }

                if (uploadIndex >= 0 && uploadIndex + 2 < segments.Length)
                {
                    // Lấy phần publicId (bỏ "upload/" và version "v123456/")
                    var pathSegments = segments.Skip(uploadIndex + 2);
                    var publicIdPath = string.Join("", pathSegments).Trim('/');

                    // Bỏ phần mở rộng file (vd: .png, .mp4, .mov...)
                    var publicId = Path.ChangeExtension(publicIdPath, null).Replace("\\", "/");

                    // Xác định loại tài nguyên
                    var lowerUrl = imageUrl.ToLower();
                    ResourceType resourceType = ResourceType.Image; // Mặc định là ảnh

                    if (lowerUrl.Contains("/video/") || lowerUrl.EndsWith(".mp4") || lowerUrl.EndsWith(".mov") ||
                        lowerUrl.EndsWith(".avi") || lowerUrl.EndsWith(".webm") || lowerUrl.EndsWith(".ogg"))
                    {
                        resourceType = ResourceType.Video;
                    }

                    // Gọi service xóa với loại tài nguyên chính xác
                    var deletionResult = await _photoService.DeletePhotoAsync(publicId, resourceType);

                    return deletionResult.Result == "ok";
                }
            }
            catch (Exception ex)
            {
                // Log hoặc xử lý lỗi nếu cần
                Console.WriteLine("Lỗi khi xóa ảnh/video trên Cloudinary: " + ex.Message);
            }

            return false;
        }

        // DELETE api/tindang/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTinDang(int id)
        {
            var tinDang = await _context.TinDangs
                .Include(t => t.AnhTinDangs)
                .FirstOrDefaultAsync(t => t.MaTinDang == id);

            if (tinDang == null)
                return NotFound(new { message = "Không tìm thấy tin đăng" });

            // Xóa ảnh/video trên Cloudinary hoặc file tạm trên server
            foreach (var img in tinDang.AnhTinDangs)
            {
                var imagePath = img.DuongDan;

                if (!string.IsNullOrEmpty(imagePath) && imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    await DeleteCloudinaryPhotoByUrlAsync(imagePath);
                }
                else if (!string.IsNullOrEmpty(imagePath) && imagePath.Contains("images/temp-uploads"))
                {
                    var trimmedPath = imagePath.TrimStart('/');
                    var localFilePath = Path.Combine(_env.WebRootPath, trimmedPath.Replace("/", Path.DirectorySeparatorChar.ToString()));

                    if (System.IO.File.Exists(localFilePath))
                    {
                        System.IO.File.Delete(localFilePath);
                        Console.WriteLine($"Đã xóa file: {localFilePath}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ File không tồn tại: {localFilePath}");
                    }
                }
            }

            // Xóa tin đăng và ảnh/video liên quan (EF Cascade Delete)
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
                .Include(p => p.NguoiBan) // Lấy tên người bán
                .Select(p => new
                {
                    p.MaTinDang,
                    p.TieuDe,
                    p.MoTa,
                    p.Gia,
                    p.TrangThai,
                    p.NgayDang,
                    NguoiBan = p.NguoiBan.FullName,
                    Images = p.AnhTinDangs.Select(a =>
                        a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? a.DuongDan
                            : (a.DuongDan.StartsWith("/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}")
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

        [HttpGet("user-info/{userId}")]
        public async Task<IActionResult> GetUserInfo(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy người dùng" });
            }

            return Ok(new
            {
                user.Id,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber
            });
        }


        [HttpGet("get-post-and-similar/{id}")]
        public async Task<IActionResult> GetPostAndSimilarPosts(int id)
        {
            // Lấy chi tiết tin đăng theo ID
            var post = await _context.TinDangs
                .Include(p => p.AnhTinDangs)
                .Include(p => p.NguoiBan)
                .Include(p => p.TinhThanh)
                .Include(p => p.QuanHuyen)
                .FirstOrDefaultAsync(p => p.MaTinDang == id && p.TrangThai == TrangThaiTinDang.DaDuyet);

            if (post == null)
            {
                return NotFound(new { message = "Không tìm thấy tin đăng này hoặc tin đăng chưa được duyệt." });
            }

            // Lấy các tin đăng tương tự theo danh mục con
            var similarPostsByCategory = await _context.TinDangs
                .Where(p => p.MaDanhMuc == post.MaDanhMuc && p.MaTinDang != post.MaTinDang && p.TrangThai == TrangThaiTinDang.DaDuyet)
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
                    Images = p.AnhTinDangs.Select(a =>
                        (a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase) || a.DuongDan.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                        ? a.DuongDan
                        : (a.DuongDan.StartsWith("/images/Posts/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}")
                    ).ToList(),
                    NguoiBan = p.NguoiBan.FullName,
                    PhoneNumber = p.NguoiBan.PhoneNumber,
                    TinhThanh = p.TinhThanh.TenTinhThanh,
                    QuanHuyen = p.QuanHuyen.TenQuanHuyen
                })
                .ToListAsync();

            // Lấy các tin đăng từ cùng người bán
            var similarPostsBySeller = await _context.TinDangs
                .Where(p => p.MaNguoiBan == post.MaNguoiBan && p.MaTinDang != post.MaTinDang && p.TrangThai == TrangThaiTinDang.DaDuyet)
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
                    Images = p.AnhTinDangs.Select(a =>
                        (a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase) || a.DuongDan.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                        ? a.DuongDan
                        : (a.DuongDan.StartsWith("/images/Posts/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}")
                    ).ToList(),
                    NguoiBan = p.NguoiBan.FullName,
                    PhoneNumber = p.NguoiBan.PhoneNumber,
                    TinhThanh = p.TinhThanh.TenTinhThanh,
                    QuanHuyen = p.QuanHuyen.TenQuanHuyen
                })
                .ToListAsync();

            var postImages = post.AnhTinDangs.Select(a =>
                (a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase) || a.DuongDan.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                ? a.DuongDan
                : (a.DuongDan.StartsWith("/images/Posts/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}")
            ).ToList();

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
                    Images = postImages,
                    NguoiBan = post.NguoiBan.FullName,
                    MaNguoiBan = post.NguoiBan.Id,
                    PhoneNumber = post.NguoiBan.PhoneNumber,
                    TinhThanh = post.TinhThanh.TenTinhThanh,
                    QuanHuyen = post.QuanHuyen.TenQuanHuyen,
                    NgayDang = post.NgayDang,
                    NgayCapNhat = post.NgayCapNhat
                },
                SimilarPostsByCategory = similarPostsByCategory,
                SimilarPostsBySeller = similarPostsBySeller
            });
        }

    }

}
