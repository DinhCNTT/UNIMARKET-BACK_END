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
using Microsoft.AspNetCore.Authorization;
using UniMarket.Services.Recommendation;
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
        public async Task<IActionResult> GetPosts()
        {
            var posts = await _context.TinDangs
                .AsNoTracking()
                .Where(p => p.TrangThai == TrangThaiTinDang.DaDuyet)
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

                    // ⭐ THÊM VIDEO URL (Code 2)
                    p.VideoUrl,

                    // ⭐ Ảnh Tin Đăng (giữ nguyên logic Code 1)
                    Images = p.AnhTinDangs
                        .OrderBy(a => a.Order)
                        .Select(a =>
                            a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                ? a.DuongDan
                                : (a.DuongDan.StartsWith("/")
                                    ? a.DuongDan
                                    : $"/images/Posts/{a.DuongDan}")
                        ),

                    // ⭐ Thông tin liên quan
                    NguoiBan = p.NguoiBan.FullName,
                    TinhThanh = p.TinhThanh.TenTinhThanh,
                    QuanHuyen = p.QuanHuyen.TenQuanHuyen,
                    DanhMuc = p.DanhMuc.TenDanhMuc,
                    DanhMucCha = p.DanhMuc.DanhMucCha.TenDanhMucCha,

                    // ⭐ Đếm số lượt lưu (Saved)
                    SavedCount = p.TinDangYeuThichs.Count()
                })
                .ToListAsync();

            if (posts == null || !posts.Any())
                return NotFound("Không có tin đăng nào.");

            return Ok(posts);
        }


        // AI đề xuất tin đăng 
        [HttpGet("get-recommended-posts")]
        public async Task<IActionResult> GetRecommendedPosts(
        [FromServices] VideoRecommendationService recommendationService,
        [FromQuery] int limit = 20)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                // ---- Lấy danh sách ID tin đề xuất từ AI ----
                var recommendedIds = await recommendationService.GetForYouVideoIds(userId, new List<int>(), limit);

                if (recommendedIds == null || !recommendedIds.Any())
                {
                    return Ok(new List<object>());
                }

                // ---- Lấy chi tiết Tin Đăng ----
                var posts = await _context.TinDangs
                    .AsNoTracking()
                    .Where(p => recommendedIds.Contains(p.MaTinDang))
                    .Select(p => new
                    {
                        p.MaTinDang,
                        p.TieuDe,
                        p.MoTa,                // ⭐ Quan trọng: cần cho mô tả ngắn
                        p.Gia,
                        p.CoTheThoaThuan,
                        p.TinhTrang,
                        p.DiaChi,
                        p.MaTinhThanh,
                        p.MaQuanHuyen,
                        p.MaNguoiBan,
                        p.NgayDang,
                        p.TrangThai,

                        p.VideoUrl,            // ⭐ Quan trọng: thumbnail + video player

                        // ⭐ Ảnh – đồng bộ 100% logic từ GetPosts
                        Images = p.AnhTinDangs
                            .OrderBy(a => a.Order)
                            .Select(a =>
                                a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                    ? a.DuongDan
                                    : (a.DuongDan.StartsWith("/")
                                        ? a.DuongDan
                                        : $"/images/Posts/{a.DuongDan}")
                            ),

                        // ⭐ Thông tin người bán
                        NguoiBan = new
                        {
                            Id = p.NguoiBan.Id,
                            FullName = p.NguoiBan.FullName,
                            Avatar = p.NguoiBan.AvatarUrl,
                            PhoneNumber = p.NguoiBan.PhoneNumber
                        },

                        TinhThanh = p.TinhThanh.TenTinhThanh,
                        QuanHuyen = p.QuanHuyen.TenQuanHuyen,
                        DanhMuc = p.DanhMuc.TenDanhMuc,
                        DanhMucCha = p.DanhMuc.DanhMucCha.TenDanhMucCha,

                        SavedCount = p.TinDangYeuThichs.Count()
                    })
                    .ToListAsync();

                // ---- Sắp xếp đúng thứ tự AI gợi ý ----
                var sortedPosts = recommendedIds
                    .Join(posts,
                          id => id,
                          p => p.MaTinDang,
                          (id, p) => p)
                    .ToList();

                return Ok(sortedPosts);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] GetRecommendedPosts: {ex.Message}");
                return StatusCode(500, new
                {
                    message = "Lỗi server khi lấy tin đề xuất.",
                    error = ex.Message
                });
            }
        }


        [RequestSizeLimit(157286400)] // 150MB
        [HttpPost("add-post")]
        public async Task<IActionResult> AddPost(
            [FromForm] string title,
            [FromForm] string description,
            [FromForm] decimal price,
            [FromForm] string contactInfo,
            [FromForm] string condition,
            [FromForm] int province,
            [FromForm] int district,
            [FromForm] List<IFormFile> images, // Frontend gửi cả ảnh và video qua đây
            [FromForm] string userId,
            [FromForm] int categoryId,
            [FromForm] string categoryName, // Tham số này không thấy dùng, nhưng giữ nguyên
            [FromForm] bool canNegotiate)
        {
            try
            {
                // =================================
                // 1. KIỂM TRA DỮ LIỆU ĐẦU VÀO
                // =================================
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null) return BadRequest("Người bán không tồn tại!");

                if (!await _context.TinhThanhs.AnyAsync(t => t.MaTinhThanh == province))
                    return BadRequest("Tỉnh thành không hợp lệ!");

                if (!await _context.QuanHuyens.AnyAsync(q => q.MaQuanHuyen == district))
                    return BadRequest("Quận huyện không hợp lệ!");

                // ✅ SỬA LỖI 1: Thêm kiểm tra MaDanhMuc (Rất quan trọng)
                if (!await _context.DanhMucs.AnyAsync(c => c.MaDanhMuc == categoryId))
                    return BadRequest("Danh mục không hợp lệ!");

                // Kiểm tra giới hạn file (tổng)
                if (images != null && images.Count > 8)
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

                    if (imageFiles.Count > 7)
                        return BadRequest("Chỉ được phép tải lên tối đa 7 ảnh.");
                    if (videoFiles.Count > 1)
                        return BadRequest("Chỉ được phép tải lên tối đa 1 video.");
                }

                // =================================
                // 2. TẠO ĐỐI TƯỢNG TIN ĐĂNG
                // =================================
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
                    NgayDang = DateTime.UtcNow, // ✅ SỬA LỖI 3: Dùng UtcNow
                    TrangThai = TrangThaiTinDang.ChoDuyet,
                    MaDanhMuc = categoryId,
                    AnhTinDangs = new List<AnhTinDang>(),
                    VideoUrl = null // Sẽ được set bên dưới
                };

                // =================================
                // 3. UPLOAD FILE LÊN CLOUDINARY (SỬA LỖI 2)
                // =================================
                int order = 1;

                // Xử lý ảnh trước
                foreach (var image in imageFiles)
                {
                    // Dùng PhotoService (Cloudinary) thay vì lưu local
                    var result = await _photoService.UploadPhotoAsync(image);
                    if (result.Error != null)
                    {
                        Console.WriteLine("❌ Lỗi upload ảnh: " + result.Error.Message);
                        return BadRequest(new { message = "Lỗi upload ảnh", error = result.Error.Message });
                    }

                    post.AnhTinDangs.Add(new AnhTinDang
                    {
                        DuongDan = result.SecureUrl.ToString(), // Dùng URL của Cloudinary
                        LoaiMedia = MediaType.Image,
                        Order = order++,
                        TinDang = post
                    });
                }

                // Xử lý video sau
                foreach (var video in videoFiles)
                {
                    // Dùng PhotoService (Cloudinary) thay vì lưu local
                    var result = await _photoService.UploadVideoAsync(video);
                    if (result.Error != null)
                    {
                        Console.WriteLine("❌ Lỗi upload video: " + result.Error.Message);
                        return BadRequest(new { message = "Lỗi upload video", error = result.Error.Message });
                    }

                    var newVideo = new AnhTinDang
                    {
                        DuongDan = result.SecureUrl.ToString(), // Dùng URL của Cloudinary
                        LoaiMedia = MediaType.Video,
                        Order = order++,
                        TinDang = post
                    };
                    post.AnhTinDangs.Add(newVideo);

                    // Set VideoUrl cho tin đăng (lấy video đầu tiên làm đại diện)
                    if (string.IsNullOrEmpty(post.VideoUrl))
                    {
                        post.VideoUrl = newVideo.DuongDan;
                    }
                }

                // =================================
                // 4. LƯU VÀO DATABASE
                // =================================
                _context.TinDangs.Add(post);
                await _context.SaveChangesAsync(); // Lưu 1 lần duy nhất

                var responseMessage = $"Bài đăng đã được thêm thành công và đang chờ duyệt! " +
                                      $"(Đã tải lên: {imageFiles.Count} ảnh, {videoFiles.Count} video)";

                return Ok(new
                {
                    message = responseMessage,
                    imageCount = imageFiles.Count,
                    videoCount = videoFiles.Count,
                    newPostId = post.MaTinDang // Trả về ID của bài post mới
                });
            }
            catch (Exception ex)
            {
                // Thêm try-catch để bắt các lỗi 500 khác và log chi tiết
                Console.WriteLine("❌ LỖI KHÔNG XÁC ĐỊNH KHI ĐĂNG TIN (add-post):");
                Console.WriteLine("Message: " + ex.Message);
                if (ex.InnerException != null)
                    Console.WriteLine("InnerException: " + ex.InnerException.Message);

                return StatusCode(500, new { message = "Lỗi server khi thêm tin đăng", error = ex.Message });
            }
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
    [FromForm] string? imageOrderMap,
    [FromForm] string? videoOrderMap)
        {
            // =================================================================
            // 1. GHI LOG DỮ LIỆU ĐẦU VÀO (ĐỂ DEBUG)
            // =================================================================
            Console.WriteLine($"\n--- [START] CẬP NHẬT TIN ĐĂNG ID: {id} ---");
            Console.WriteLine($"Data (Text): title={title}, price={price}, province={province}, district={district}, categoryId={categoryId}");

            try
            {
                // Lấy tin đăng từ DB
                var post = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                if (post == null)
                {
                    Console.WriteLine($"[LỖI] Không tìm thấy tin đăng ID={id} để cập nhật.");
                    return NotFound(new { message = "Không tìm thấy tin đăng" });
                }

                // TODO: Kiểm tra quyền sở hữu nếu cần (post.MaNguoiBan != userId)

                Console.WriteLine($"[OK] Đã tìm thấy tin đăng. Bắt đầu cập nhật thông tin cơ bản...");

                // =================================================================
                // ✅ CẬP NHẬT THÔNG TIN CƠ BẢN (TEXT)
                // =================================================================
                post.TieuDe = title;
                post.MoTa = description;
                post.Gia = price;
                post.DiaChi = contactInfo;
                post.TinhTrang = condition;
                post.CoTheThoaThuan = canNegotiate;
                post.MaTinhThanh = province;
                post.MaQuanHuyen = district;
                post.MaDanhMuc = categoryId;
                post.NgayCapNhat = DateTime.UtcNow;

                // Reset trạng thái về chờ duyệt mỗi khi cập nhật
                post.TrangThai = TrangThaiTinDang.ChoDuyet;

                // 🔥🔥🔥 [FIX QUAN TRỌNG] 🔥🔥🔥
                // Lưu ngay lập tức các thay đổi về Text và Trạng thái.
                // Nếu không có dòng này, code chạy xuống dưới mà không có ảnh/video thay đổi 
                // thì nó sẽ kết thúc hàm mà KHÔNG HỀ lưu gì cả.
                await _context.SaveChangesAsync();
                Console.WriteLine($"[OK] Đã lưu thông tin cơ bản và reset trạng thái về Chờ Duyệt.");

                // =================================================================
                // 2. XỬ LÝ MEDIA (ẢNH/VIDEO)
                // =================================================================

                // Deserialize JSON parameters
                var idsToDeleteImage = (string.IsNullOrEmpty(oldImagesToDelete) || oldImagesToDelete == "null")
                    ? new List<int>()
                    : JsonConvert.DeserializeObject<List<int>>(oldImagesToDelete);

                var idsToDeleteVideo = (string.IsNullOrEmpty(oldVideosToDelete) || oldVideosToDelete == "null")
                    ? new List<int>()
                    : JsonConvert.DeserializeObject<List<int>>(oldVideosToDelete);

                var imgOrderMap = (string.IsNullOrEmpty(imageOrderMap) || imageOrderMap == "null")
                    ? new List<dynamic>()
                    : JsonConvert.DeserializeObject<List<dynamic>>(imageOrderMap);

                var vidOrderMap = (string.IsNullOrEmpty(videoOrderMap) || videoOrderMap == "null")
                    ? new List<dynamic>()
                    : JsonConvert.DeserializeObject<List<dynamic>>(videoOrderMap);

                var allIdsToDelete = idsToDeleteImage.Concat(idsToDeleteVideo).ToList();

                // -----------------------------------------------------------------
                // BƯỚC 1: XÓA MEDIA CŨ
                // -----------------------------------------------------------------
                if (allIdsToDelete.Any())
                {
                    var mediaToDelete = post.AnhTinDangs.Where(m => allIdsToDelete.Contains(m.MaAnh)).ToList();
                    Console.WriteLine($"[BƯỚC 1] Đang xóa {mediaToDelete.Count} media cũ...");
                    foreach (var media in mediaToDelete)
                    {
                        if (!string.IsNullOrEmpty(media.DuongDan) && media.DuongDan.StartsWith("http"))
                        {
                            await _photoService.DeletePhotoAsync(media.DuongDan); // Giả sử bạn có hàm này hoặc logic xóa Cloudinary cũ
                        }
                        _context.AnhTinDangs.Remove(media);
                    }
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"[BƯỚC 1] Đã xóa media cũ thành công.");
                }

                // -----------------------------------------------------------------
                // BƯỚC 2: UPLOAD ẢNH/VIDEO MỚI
                // -----------------------------------------------------------------
                Console.WriteLine($"[BƯỚC 2] Bắt đầu upload media mới...");
                var newlyUploadedImages = new List<AnhTinDang>();
                var newlyUploadedVideos = new List<AnhTinDang>();

                // Upload Ảnh
                if (newImages != null && newImages.Count > 0)
                {
                    foreach (var img in newImages)
                    {
                        var result = await _photoService.UploadPhotoAsync(img);
                        if (result.Error != null) return BadRequest(new { message = "Lỗi upload ảnh", error = result.Error.Message });

                        var newImage = new AnhTinDang
                        {
                            MaTinDang = post.MaTinDang,
                            DuongDan = result.SecureUrl.ToString(),
                            LoaiMedia = MediaType.Image,
                            Order = 0, // Tạm thời
                            TinDang = post
                        };
                        _context.AnhTinDangs.Add(newImage);
                        newlyUploadedImages.Add(newImage);
                    }
                }

                // Upload Video
                if (newVideos != null && newVideos.Count > 0)
                {
                    foreach (var vid in newVideos)
                    {
                        var result = await _photoService.UploadVideoAsync(vid);
                        if (result.Error != null) return BadRequest(new { message = "Lỗi upload video", error = result.Error.Message });

                        var newVideo = new AnhTinDang
                        {
                            MaTinDang = post.MaTinDang,
                            DuongDan = result.SecureUrl.ToString(),
                            LoaiMedia = MediaType.Video,
                            Order = 0, // Tạm thời
                            TinDang = post
                        };
                        _context.AnhTinDangs.Add(newVideo);
                        newlyUploadedVideos.Add(newVideo);
                    }
                }

                // Lưu media mới vào DB để có ID
                if (newlyUploadedImages.Any() || newlyUploadedVideos.Any())
                {
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"[BƯỚC 2] Đã upload và lưu media mới thành công.");
                }

                // -----------------------------------------------------------------
                // BƯỚC 3: LẤY LẠI DỮ LIỆU ĐẦY ĐỦ (Để tính toán thứ tự)
                // -----------------------------------------------------------------
                // Refresh lại post instance để lấy full danh sách ảnh mới nhất từ DB
                post = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                if (post == null) return StatusCode(500, new { message = "Lỗi server: Mất dữ liệu post sau khi upload." });

                var allMedia = post.AnhTinDangs.ToList();

                // -----------------------------------------------------------------
                // BƯỚC 4: TÍNH TOÁN THỨ TỰ MỚI
                // -----------------------------------------------------------------
                var finalOrderMap = new Dictionary<int, int>();

                // 4.1: Xử lý Images Order
                for (int i = 0; i < imgOrderMap.Count; i++)
                {
                    var orderItem = imgOrderMap[i];
                    var finalOrder = i + 1;
                    string? type = orderItem.type?.ToString();

                    if (type == "old")
                    {
                        if (int.TryParse(orderItem.id?.ToString(), out int mediaId)) finalOrderMap[mediaId] = finalOrder;
                    }
                    else if (type == "new")
                    {
                        if (int.TryParse(orderItem.fileIndex?.ToString(), out int fileIndex) && fileIndex >= 0 && fileIndex < newlyUploadedImages.Count)
                        {
                            finalOrderMap[newlyUploadedImages[fileIndex].MaAnh] = finalOrder;
                        }
                    }
                }

                // 4.2: Xử lý Videos Order
                int videoStartOrder = imgOrderMap.Count + 1;
                for (int i = 0; i < vidOrderMap.Count; i++)
                {
                    var orderItem = vidOrderMap[i];
                    var finalOrder = videoStartOrder + i;
                    string? type = orderItem.type?.ToString();

                    if (type == "old")
                    {
                        if (int.TryParse(orderItem.id?.ToString(), out int mediaId)) finalOrderMap[mediaId] = finalOrder;
                    }
                    else if (type == "new")
                    {
                        if (int.TryParse(orderItem.fileIndex?.ToString(), out int fileIndex) && fileIndex >= 0 && fileIndex < newlyUploadedVideos.Count)
                        {
                            finalOrderMap[newlyUploadedVideos[fileIndex].MaAnh] = finalOrder;
                        }
                    }
                }

                // -----------------------------------------------------------------
                // BƯỚC 5: CẬP NHẬT ORDER VÀO DB
                // -----------------------------------------------------------------
                bool hasOrderChanged = false;
                foreach (var media in allMedia)
                {
                    if (finalOrderMap.ContainsKey(media.MaAnh))
                    {
                        var newOrder = finalOrderMap[media.MaAnh];
                        if (media.Order != newOrder)
                        {
                            media.Order = newOrder;
                            hasOrderChanged = true;
                            _context.Entry(media).Property(x => x.Order).IsModified = true;
                        }
                    }
                }

                if (hasOrderChanged) await _context.SaveChangesAsync();

                // -----------------------------------------------------------------
                // BƯỚC 6: CẬP NHẬT VideoUrl (Thumbnail video)
                // -----------------------------------------------------------------
                var firstVideo = allMedia.Where(m => m.LoaiMedia == MediaType.Video).OrderBy(m => m.Order).FirstOrDefault();
                bool videoUrlChanged = false;

                if (firstVideo != null && firstVideo.DuongDan != post.VideoUrl)
                {
                    post.VideoUrl = firstVideo.DuongDan;
                    videoUrlChanged = true;
                }
                else if (firstVideo == null && !string.IsNullOrEmpty(post.VideoUrl))
                {
                    post.VideoUrl = null;
                    videoUrlChanged = true;
                }

                if (videoUrlChanged) await _context.SaveChangesAsync();

                // -----------------------------------------------------------------
                // BƯỚC 7: SIGNALR NOTIFICATION
                // -----------------------------------------------------------------
                var updatedPostSignalR = new
                {
                    MaTinDang = post.MaTinDang,
                    TieuDe = post.TieuDe,
                    Gia = post.Gia,
                    AnhDaiDien = post.AnhTinDangs?.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan ?? "",
                    VideoUrl = post.VideoUrl
                };
                await _hubContext.Clients.All.SendAsync("CapNhatTinDang", updatedPostSignalR);

                // -----------------------------------------------------------------
                // BƯỚC 8: TRẢ VỀ KẾT QUẢ
                // -----------------------------------------------------------------
                // Lấy bản ghi cuối cùng (Read-only cho nhanh)
                var finalPost = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                return Ok(new
                {
                    message = "Cập nhật thành công",
                    MaTinDang = finalPost.MaTinDang,
                    TotalMedia = finalPost.AnhTinDangs.Count,
                    HasOrderChanged = hasOrderChanged,
                    VideoUrlChanged = videoUrlChanged,
                    VideoUrl = finalPost.VideoUrl,
                    AnhTinDangs = finalPost.AnhTinDangs
                        .OrderBy(a => a.Order)
                        .Select(a => new {
                            a.MaAnh,
                            a.DuongDan,
                            a.Order,
                            a.LoaiMedia,
                            FileName = a.DuongDan.Split('/').LastOrDefault()
                        })
                        .ToList()
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n--- [ERROR 500] LỖI CẬP NHẬT TIN ĐĂNG ID: {id} ---");
                Console.WriteLine($"❌ Message: {ex.Message}");
                if (ex.InnerException != null) Console.WriteLine($"❌ InnerException: {ex.InnerException.Message}");

                return StatusCode(500, new
                {
                    message = "Lỗi server khi cập nhật tin đăng",
                    error = ex.Message,
                    details = ex.InnerException?.Message
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

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTinDang(int id)
        {
            var tinDang = await _context.TinDangs
                .Include(t => t.AnhTinDangs)
                .FirstOrDefaultAsync(t => t.MaTinDang == id);

            if (tinDang == null)
                return NotFound(new { message = "Không tìm thấy tin đăng" });

            // Xóa ảnh trên Cloudinary
            foreach (var img in tinDang.AnhTinDangs)
            {
                if (!string.IsNullOrEmpty(img.DuongDan) && img.DuongDan.StartsWith("http"))
                {
                    await DeleteCloudinaryPhotoByUrlAsync(img.DuongDan);
                }
            }

            // Xóa bảng phụ liên quan
            _context.AnhTinDangs.RemoveRange(_context.AnhTinDangs.Where(a => a.MaTinDang == id));
            _context.TinDangYeuThichs.RemoveRange(_context.TinDangYeuThichs.Where(t => t.MaTinDang == id));
            _context.VideoComments.RemoveRange(_context.VideoComments.Where(c => c.MaTinDang == id));
            _context.VideoLikes.RemoveRange(_context.VideoLikes.Where(l => l.MaTinDang == id));
            _context.VideoViews.RemoveRange(_context.VideoViews.Where(v => v.MaTinDang == id));
            _context.VideoTinDangSaves.RemoveRange(_context.VideoTinDangSaves.Where(v => v.MaTinDang == id));

            // ✅ SỬA: Không xóa chat, chỉ set flag IsPostDeleted
            var cuocTros = await _context.CuocTroChuyens.Where(c => c.MaTinDang == id).ToListAsync();
            foreach (var c in cuocTros)
            {
                c.IsPostDeleted = true;
                c.TieuDeTinDang += " (đã xóa)";  // Optional
            }

            // Notify qua SignalR
            await _hubContext.Clients.All.SendAsync("CapNhatTinDang", new
            {
                MaTinDang = id,
                IsDeleted = true
            });

            // Xóa TinDang
            _context.TinDangs.Remove(tinDang);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Xóa tin đăng thành công. Cuộc trò chuyện liên quan vẫn được giữ nguyên." });
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
                .Include(p => p.NguoiBan)
                .Select(p => new
                {
                    p.MaTinDang,
                    p.TieuDe,
                    p.MoTa,
                    p.Gia,
                    p.TrangThai,
                    p.NgayDang,
                    NguoiBan = p.NguoiBan.FullName,
                    Images = p.AnhTinDangs
                        .OrderBy(a => a.Order) // Đổi từ giảm dần sang tăng dần
                        .Select(a =>
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
                    Images = p.AnhTinDangs
                        .OrderBy(a => a.Order) // Sắp xếp theo Order
                        .Select(a =>
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
                    Images = p.AnhTinDangs
                        .OrderBy(a => a.Order) // Sắp xếp theo Order
                        .Select(a =>
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

            var postImages = post.AnhTinDangs
                .OrderBy(a => a.Order) // Sắp xếp theo Order
                .Select(a =>
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
        // Thêm method này vào TinDangController.cs

        [HttpGet("suggestions")]
        public async Task<IActionResult> GetSuggestions([FromQuery] string query, [FromQuery] int limit = 8)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Ok(new List<object>());
            }

            try
            {
                // Tìm kiếm tin đăng có tiêu đề chứa từ khóa (không phân biệt hoa thường)
                var suggestions = await _context.TinDangs
                    .Where(p => p.TrangThai == TrangThaiTinDang.DaDuyet &&
                               p.TieuDe.ToLower().Contains(query.ToLower()))
                    .Include(p => p.DanhMuc)
                        .ThenInclude(dm => dm.DanhMucCha)
                    .Select(p => new
                    {
                        p.MaTinDang,
                        TieuDe = p.TieuDe,
                        DanhMucCha = p.DanhMuc.DanhMucCha != null ? p.DanhMuc.DanhMucCha.TenDanhMucCha : p.DanhMuc.TenDanhMuc
                    })
                    .Take(limit)
                    .ToListAsync();

                // Loại bỏ duplicate titles để tránh hiển thị trùng lặp
                var uniqueSuggestions = suggestions
                    .GroupBy(s => s.TieuDe.ToLower())
                    .Select(group => group.First())
                    .Take(limit)
                    .ToList();

                return Ok(uniqueSuggestions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi lấy gợi ý: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi server khi lấy gợi ý" });
            }
        }
        [HttpPost("save-search-history")]
        [Authorize]
        public async Task<IActionResult> SaveSearchHistory([FromBody] SaveSearchHistoryRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Dữ liệu gửi lên không hợp lệ (request null)" });
            }

            if (string.IsNullOrWhiteSpace(request.Keyword))
            {
                return BadRequest(new { message = "Từ khóa tìm kiếm không được để trống" });
            }

            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                Console.WriteLine($"📦 userId: {userId}");
                Console.WriteLine($"📦 keyword: {request.Keyword}");

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "Người dùng chưa đăng nhập" });
                }

                var existingSearch = await _context.SearchHistories
                    .Where(sh => sh.UserId == userId &&
                                (sh.Keyword ?? "").ToLower() == request.Keyword.ToLower() &&
                                sh.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1))
                    .FirstOrDefaultAsync();

                if (existingSearch != null)
                {
                    existingSearch.CreatedAt = DateTimeOffset.UtcNow;
                }
                else
                {
                    var searchHistory = new SearchHistory
                    {
                        UserId = userId,
                        Keyword = request.Keyword.Trim(),
                        CreatedAt = DateTimeOffset.UtcNow
                    };

                    _context.SearchHistories.Add(searchHistory);
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = "Đã lưu lịch sử tìm kiếm" });
            }
            catch (Exception ex)
            {
                Console.WriteLine("🔥 Lỗi khi lưu lịch sử tìm kiếm:");
                Console.WriteLine(ex.ToString());
                return StatusCode(500, new { message = ex.Message });
            }
        }



        [HttpGet("search-history")]
        [Authorize]
        public async Task<IActionResult> GetSearchHistory([FromQuery] int limit = 10)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "Người dùng chưa đăng nhập" });
                }

                var searchHistory = await _context.SearchHistories
                    .Where(sh => sh.UserId == userId)
                    .OrderByDescending(sh => sh.CreatedAt)
                    .Take(limit)
                    .Select(sh => new
                    {
                        sh.Id,
                        sh.Keyword,
                        sh.CreatedAt
                    })
                    .ToListAsync();

                return Ok(searchHistory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi lấy lịch sử tìm kiếm: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi server khi lấy lịch sử tìm kiếm" });
            }
        }

        [HttpDelete("search-history/{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteSearchHistory(int id)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "Người dùng chưa đăng nhập" });
                }

                var searchHistory = await _context.SearchHistories
                    .Where(sh => sh.Id == id && sh.UserId == userId)
                    .FirstOrDefaultAsync();

                if (searchHistory == null)
                {
                    return NotFound(new { message = "Không tìm thấy lịch sử tìm kiếm" });
                }

                _context.SearchHistories.Remove(searchHistory);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Đã xóa lịch sử tìm kiếm" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi xóa lịch sử tìm kiếm: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi server khi xóa lịch sử tìm kiếm" });
            }
        }

        [HttpDelete("search-history")]
        [Authorize]
        public async Task<IActionResult> ClearSearchHistory()
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "Người dùng chưa đăng nhập" });
                }

                var userSearchHistories = await _context.SearchHistories
                    .Where(sh => sh.UserId == userId)
                    .ToListAsync();

                _context.SearchHistories.RemoveRange(userSearchHistories);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Đã xóa toàn bộ lịch sử tìm kiếm" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi xóa lịch sử tìm kiếm: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi server khi xóa lịch sử tìm kiếm" });
            }
        }

        // DTO class for request
        public class SaveSearchHistoryRequest
        {
            public string Keyword { get; set; } = null!;
        }
    }

}