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
                    Images = p.AnhTinDangs
        .OrderBy(a => a.Order)
        .Select(a =>
            a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? a.DuongDan
                : (a.DuongDan.StartsWith("/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}")
        ).ToList(),
                    NguoiBan = p.NguoiBan.FullName,
                    TinhThanh = p.TinhThanh.TenTinhThanh,
                    QuanHuyen = p.QuanHuyen.TenQuanHuyen,
                    DanhMuc = p.DanhMuc.TenDanhMuc,
                    DanhMucCha = p.DanhMuc.DanhMucCha.TenDanhMucCha,

                    // ✅ Thêm dòng này để đếm số lượt lưu (like/favorite)
                    SavedCount = _context.TinDangYeuThichs.Count(y => y.MaTinDang == p.MaTinDang)
                })

                .ToList();

            if (posts == null || !posts.Any())
                return NotFound("Không có tin đăng nào.");

            return Ok(posts);
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
    [FromForm] string userId, // ✅ Bổ sung: Bạn có trường userId trong form
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
            Console.WriteLine($"Data (Files): newImages={newImages?.Count ?? 0}, newVideos={newVideos?.Count ?? 0}");
            Console.WriteLine($"Data (JSON Raw): oldImagesToDelete = {oldImagesToDelete ?? "null"}");
            Console.WriteLine($"Data (JSON Raw): oldVideosToDelete = {oldVideosToDelete ?? "null"}");
            Console.WriteLine($"Data (JSON Raw): imageOrderMap = {imageOrderMap ?? "null"}");
            Console.WriteLine($"Data (JSON Raw): videoOrderMap = {videoOrderMap ?? "null"}");
            // =================================================================

            try
            {
                var post = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                if (post == null)
                {
                    Console.WriteLine($"[LỖI] Không tìm thấy tin đăng ID={id} để cập nhật.");
                    return NotFound(new { message = "Không tìm thấy tin đăng" });
                }

                // TODO: Kiểm tra xem userId có phải là chủ sở hữu của bài đăng không (nếu cần)
                // if (post.MaNguoiBan != userId)
                // {
                //     return Unauthorized(new { message = "Bạn không có quyền cập nhật tin đăng này" });
                // }

                Console.WriteLine($"[OK] Đã tìm thấy tin đăng. Bắt đầu cập nhật thông tin cơ bản...");

                // Update thông tin cơ bản
                post.TieuDe = title;
                post.MoTa = description;
                post.Gia = price;
                post.DiaChi = contactInfo;
                post.TinhTrang = condition;
                post.CoTheThoaThuan = canNegotiate;
                post.MaTinhThanh = province;
                post.MaQuanHuyen = district;
                post.MaDanhMuc = categoryId;
                post.NgayCapNhat = DateTime.UtcNow; // ✅ Dùng UtcNow cho nhất quán
                post.TrangThai = TrangThaiTinDang.ChoDuyet; // ✅ Set lại trạng thái chờ duyệt

                // =================================================================
                // 2. DESERIALIZE JSON (FIX #1 - An toàn)
                // =================================================================
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

                Console.WriteLine($"[JSON] IDs to delete images: [{string.Join(", ", idsToDeleteImage)}]");
                Console.WriteLine($"[JSON] IDs to delete videos: [{string.Join(", ", idsToDeleteVideo)}]");
                Console.WriteLine($"[JSON] Image order map count: {imgOrderMap.Count}");
                Console.WriteLine($"[JSON] Video order map count: {vidOrderMap.Count}");

                var allIdsToDelete = idsToDeleteImage.Concat(idsToDeleteVideo).ToList();

                // =================================================================
                // BƯỚC 1: XÓA MEDIA CŨ
                // =================================================================
                if (allIdsToDelete.Any())
                {
                    var mediaToDelete = post.AnhTinDangs.Where(m => allIdsToDelete.Contains(m.MaAnh)).ToList();
                    Console.WriteLine($"[BƯỚC 1] Đang xóa {mediaToDelete.Count} media cũ...");
                    foreach (var media in mediaToDelete)
                    {
                        Console.WriteLine($"    -> 🗑️ Đang xóa media ID={media.MaAnh}, URL={media.DuongDan}");
                        if (!string.IsNullOrEmpty(media.DuongDan) && media.DuongDan.StartsWith("http"))
                        {
                            await DeleteCloudinaryPhotoByUrlAsync(media.DuongDan);
                        }
                        _context.AnhTinDangs.Remove(media);
                    }
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"[BƯỚC 1] Đã xóa media cũ thành công.");
                }

                // =================================================================
                // BƯỚC 2: UPLOAD ẢNH/VIDEO MỚI
                // =================================================================
                Console.WriteLine($"[BƯỚC 2] Bắt đầu upload media mới...");
                var newlyUploadedImages = new List<AnhTinDang>();
                var newlyUploadedVideos = new List<AnhTinDang>();

                if (newImages != null && newImages.Count > 0)
                {
                    Console.WriteLine($"    -> 📸 Đang upload {newImages.Count} ảnh mới");
                    for (int i = 0; i < newImages.Count; i++)
                    {
                        var img = newImages[i];
                        var result = await _photoService.UploadPhotoAsync(img);
                        if (result.Error != null)
                        {
                            Console.WriteLine($"    -> ❌ Lỗi upload ảnh: {result.Error.Message}");
                            return BadRequest(new { message = "Lỗi upload ảnh", error = result.Error.Message });
                        }

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

                if (newVideos != null && newVideos.Count > 0)
                {
                    Console.WriteLine($"    -> 🎥 Đang upload {newVideos.Count} video mới");
                    for (int i = 0; i < newVideos.Count; i++)
                    {
                        var vid = newVideos[i];
                        var result = await _photoService.UploadVideoAsync(vid);
                        if (result.Error != null)
                        {
                            Console.WriteLine($"    -> ❌ Lỗi upload video: {result.Error.Message}");
                            return BadRequest(new { message = "Lỗi upload video", error = result.Error.Message });
                        }

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

                // Lưu để có ID cho các media mới
                if (newlyUploadedImages.Any() || newlyUploadedVideos.Any())
                {
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"[BƯỚC 2] Đã upload và lưu media mới thành công.");
                }
                else
                {
                    Console.WriteLine($"[BƯỚC 2] Không có media mới nào được upload.");
                }


                // =================================================================
                // BƯỚC 3: LẤY LẠI DỮ LIỆU ĐẦY ĐỦ (FIX #3 - An toàn)
                // =================================================================
                Console.WriteLine($"[BƯỚC 3] Lấy lại dữ liệu đầy đủ từ DB...");
                post = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                // ✅ THÊM KIỂM TRA NULL (FIX #3)
                if (post == null)
                {
                    Console.WriteLine($"[LỖI] Không tìm thấy post ID={id} sau khi cập nhật media.");
                    return StatusCode(500, new { message = "Lỗi server: Bài đăng không tồn tại sau khi cập nhật." });
                }

                var allMedia = post.AnhTinDangs.ToList();
                Console.WriteLine($"[BƯỚC 3] Tổng cộng {allMedia.Count} media (cũ + mới) trong DB.");

                // =================================================================
                // BƯỚC 4: TÍNH TOÁN THỨ TỰ MỚI (FIX #2 - An toàn)
                // =================================================================
                Console.WriteLine($"[BƯỚC 4] Bắt đầu tính toán thứ tự mới...");
                var finalOrderMap = new Dictionary<int, int>();

                // 4.1: Xử lý images dựa trên imageOrderMap
                for (int i = 0; i < imgOrderMap.Count; i++)
                {
                    var orderItem = imgOrderMap[i];
                    var finalOrder = i + 1; // Vị trí cuối cùng bắt đầu từ 1

                    string? type = orderItem.type?.ToString();
                    if (string.IsNullOrEmpty(type))
                    {
                        Console.WriteLine($"    -> ⚠️ Bỏ qua image item #{i} vì 'type' bị null hoặc rỗng.");
                        continue;
                    }

                    if (type == "old")
                    {
                        if (int.TryParse(orderItem.id?.ToString(), out int mediaId))
                        {
                            finalOrderMap[mediaId] = finalOrder;
                            Console.WriteLine($"    -> 📸 Ảnh cũ ID={mediaId} -> Order={finalOrder}");
                        }
                        else
                        {
                            Console.WriteLine($"    -> ⚠️ Bỏ qua ảnh cũ vì 'id' không hợp lệ: {orderItem.id}");
                        }
                    }
                    else if (type == "new")
                    {
                        if (int.TryParse(orderItem.fileIndex?.ToString(), out int fileIndex))
                        {
                            if (fileIndex >= 0 && fileIndex < newlyUploadedImages.Count)
                            {
                                var newImage = newlyUploadedImages[fileIndex];
                                finalOrderMap[newImage.MaAnh] = finalOrder;
                                Console.WriteLine($"    -> 📸 Ảnh mới #{fileIndex} (ID={newImage.MaAnh}) -> Order={finalOrder}");
                            }
                            else
                            {
                                Console.WriteLine($"    -> ⚠️ Bỏ qua ảnh mới vì 'fileIndex'={fileIndex} nằm ngoài phạm vi mảng ({newlyUploadedImages.Count}).");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"    -> ⚠️ Bỏ qua ảnh mới vì 'fileIndex' không hợp lệ: {orderItem.fileIndex}");
                        }
                    }
                }

                // 4.2: Xử lý videos dựa trên videoOrderMap
                int videoStartOrder = imgOrderMap.Count + 1;
                for (int i = 0; i < vidOrderMap.Count; i++)
                {
                    var orderItem = vidOrderMap[i];
                    var finalOrder = videoStartOrder + i;

                    string? type = orderItem.type?.ToString();
                    if (string.IsNullOrEmpty(type))
                    {
                        Console.WriteLine($"    -> ⚠️ Bỏ qua video item #{i} vì 'type' bị null hoặc rỗng.");
                        continue;
                    }

                    if (type == "old")
                    {
                        if (int.TryParse(orderItem.id?.ToString(), out int mediaId))
                        {
                            finalOrderMap[mediaId] = finalOrder;
                            Console.WriteLine($"    -> 🎥 Video cũ ID={mediaId} -> Order={finalOrder}");
                        }
                        else
                        {
                            Console.WriteLine($"    -> ⚠️ Bỏ qua video cũ vì 'id' không hợp lệ: {orderItem.id}");
                        }
                    }
                    else if (type == "new")
                    {
                        if (int.TryParse(orderItem.fileIndex?.ToString(), out int fileIndex))
                        {
                            if (fileIndex >= 0 && fileIndex < newlyUploadedVideos.Count)
                            {
                                var newVideo = newlyUploadedVideos[fileIndex];
                                finalOrderMap[newVideo.MaAnh] = finalOrder;
                                Console.WriteLine($"    -> 🎥 Video mới #{fileIndex} (ID={newVideo.MaAnh}) -> Order={finalOrder}");
                            }
                            else
                            {
                                Console.WriteLine($"    -> ⚠️ Bỏ qua video mới vì 'fileIndex'={fileIndex} nằm ngoài phạm vi mảng ({newlyUploadedVideos.Count}).");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"    -> ⚠️ Bỏ qua video mới vì 'fileIndex' không hợp lệ: {orderItem.fileIndex}");
                        }
                    }
                }

                // =================================================================
                // BƯỚC 5: CẬP NHẬT THỨ TỰ CHO TẤT CẢ MEDIA
                // =================================================================
                Console.WriteLine($"[BƯỚC 5] Bắt đầu cập nhật Order trong DB...");
                bool hasOrderChanged = false;
                foreach (var media in allMedia)
                {
                    if (finalOrderMap.ContainsKey(media.MaAnh))
                    {
                        var newOrder = finalOrderMap[media.MaAnh];
                        if (media.Order != newOrder)
                        {
                            Console.WriteLine($"    -> 🔄 Cập nhật Order cho media ID={media.MaAnh}: {media.Order} -> {newOrder}");
                            media.Order = newOrder;
                            hasOrderChanged = true;
                            _context.Entry(media).Property(x => x.Order).IsModified = true;
                        }
                    }
                }

                if (hasOrderChanged)
                {
                    Console.WriteLine($"[BƯỚC 5] Đang lưu thay đổi thứ tự...");
                    await _context.SaveChangesAsync();
                }
                else
                {
                    Console.WriteLine($"[BƯỚC 5] Không có thay đổi thứ tự nào.");
                }

                // =================================================================
                // BƯỚC 6: CẬP NHẬT VideoUrl (Ảnh bìa cho video list)
                // =================================================================
                var firstVideo = allMedia
                    .Where(m => m.LoaiMedia == MediaType.Video)
                    .OrderBy(m => m.Order)
                    .FirstOrDefault();

                bool videoUrlChanged = false;
                if (firstVideo != null && firstVideo.DuongDan != post.VideoUrl)
                {
                    Console.WriteLine($"[BƯỚC 6] Cập nhật VideoUrl: {post.VideoUrl} -> {firstVideo.DuongDan}");
                    post.VideoUrl = firstVideo.DuongDan;
                    _context.Entry(post).Property(x => x.VideoUrl).IsModified = true;
                    videoUrlChanged = true;
                }
                else if (firstVideo == null && !string.IsNullOrEmpty(post.VideoUrl))
                {
                    Console.WriteLine($"[BƯỚC 6] Xóa VideoUrl vì không còn video nào.");
                    post.VideoUrl = null;
                    _context.Entry(post).Property(x => x.VideoUrl).IsModified = true;
                    videoUrlChanged = true;
                }

                // Lưu thay đổi VideoUrl
                if (videoUrlChanged)
                {
                    await _context.SaveChangesAsync();
                    Console.WriteLine("[BƯỚC 6] Đã cập nhật VideoUrl thành công.");
                }

                // =================================================================
                // BƯỚC 7: SIGNALR NOTIFICATION
                // =================================================================
                var updatedPostSignalR = new
                {
                    MaTinDang = post.MaTinDang,
                    TieuDe = post.TieuDe,
                    Gia = post.Gia,
                    AnhDaiDien = post.AnhTinDangs?.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan ?? "",
                    VideoUrl = post.VideoUrl
                };
                Console.WriteLine($"[BƯỚC 7] Gửi SignalR 'CapNhatTinDang' cho MaTinDang={updatedPostSignalR.MaTinDang}");
                await _hubContext.Clients.All.SendAsync("CapNhatTinDang", updatedPostSignalR);

                // =================================================================
                // BƯỚC 8: TRẢ VỀ RESPONSE
                // =================================================================

                // Lấy lại dữ liệu cuối cùng để trả về (đảm bảo 100% chính xác)
                var finalPost = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .AsNoTracking() // Dùng AsNoTracking để đọc cho nhanh
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                Console.WriteLine($"--- [SUCCESS] CẬP NHẬT TIN ĐĂNG ID: {id} THÀNH CÔNG ---");

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
                // =================================================================
                // XỬ LÝ LỖI 500 (GHI LOG CHI TIẾT NHẤT CÓ THỂ)
                // =================================================================
                Console.WriteLine($"\n--- [ERROR 500] LỖI CẬP NHẬT TIN ĐĂNG ID: {id} ---");

                // In lỗi chính
                Console.WriteLine($"❌ Message: {ex.Message}");

                // In lỗi bên trong (thường là lỗi gốc rễ)
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"❌ InnerException Message: {ex.InnerException.Message}");
                    Console.WriteLine($"❌ InnerException StackTrace: {ex.InnerException.StackTrace}");
                }

                // In StackTrace (cho biết lỗi ở file nào, dòng nào)
                Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");
                Console.WriteLine("--------------------------------------------------\n");

                return StatusCode(500, new
                {
                    message = "Lỗi server khi cập nhật tin đăng",
                    error = ex.Message,
                    details = ex.InnerException?.Message,
                    stackTrace = ex.StackTrace // Gửi cả stack trace về client (chỉ khi dev)
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
