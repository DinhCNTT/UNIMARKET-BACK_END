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
    [FromForm] string? imageOrderMap,
    [FromForm] string? videoOrderMap)
        {
            try
            {
                var post = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                if (post == null)
                    return NotFound(new { message = "Không tìm thấy tin đăng" });

                Console.WriteLine($"🔄 Đang cập nhật tin đăng ID={id}, tiêu đề={title}, giá={price}");

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
                post.NgayCapNhat = DateTime.Now;

                // Deserialize JSON từ frontend
                var idsToDeleteImage = string.IsNullOrEmpty(oldImagesToDelete) ? new List<int>() : JsonConvert.DeserializeObject<List<int>>(oldImagesToDelete);
                var idsToDeleteVideo = string.IsNullOrEmpty(oldVideosToDelete) ? new List<int>() : JsonConvert.DeserializeObject<List<int>>(oldVideosToDelete);

                // **MỚI: Deserialize order map từ frontend**
                var imgOrderMap = string.IsNullOrEmpty(imageOrderMap) ? new List<dynamic>() : JsonConvert.DeserializeObject<List<dynamic>>(imageOrderMap);
                var vidOrderMap = string.IsNullOrEmpty(videoOrderMap) ? new List<dynamic>() : JsonConvert.DeserializeObject<List<dynamic>>(videoOrderMap);

                Console.WriteLine($"📋 IDs to delete images: [{string.Join(", ", idsToDeleteImage)}]");
                Console.WriteLine($"📋 IDs to delete videos: [{string.Join(", ", idsToDeleteVideo)}]");
                Console.WriteLine($"📋 Image order map: {imageOrderMap}");
                Console.WriteLine($"📋 Video order map: {videoOrderMap}");

                var allIdsToDelete = idsToDeleteImage.Concat(idsToDeleteVideo).ToList();

                // **BƯỚC 1: XÓA MEDIA CŨ**
                var mediaToDelete = post.AnhTinDangs.Where(m => allIdsToDelete.Contains(m.MaAnh)).ToList();
                foreach (var media in mediaToDelete)
                {
                    Console.WriteLine($"🗑️ Đang xóa media ID={media.MaAnh}, URL={media.DuongDan}");
                    if (!string.IsNullOrEmpty(media.DuongDan) && media.DuongDan.StartsWith("http"))
                    {
                        await DeleteCloudinaryPhotoByUrlAsync(media.DuongDan);
                    }
                    _context.AnhTinDangs.Remove(media);
                }

                // **LƯU THAY ĐỔI XÓA TRƯỚC**
                await _context.SaveChangesAsync();

                // **BƯỚC 2: UPLOAD ẢNH/VIDEO MỚI TRƯỚC**
                var newlyUploadedImages = new List<AnhTinDang>();
                var newlyUploadedVideos = new List<AnhTinDang>();

                if (newImages != null && newImages.Count > 0)
                {
                    Console.WriteLine($"📸 Đang upload {newImages.Count} ảnh mới");
                    for (int i = 0; i < newImages.Count; i++)
                    {
                        var img = newImages[i];
                        var result = await _photoService.UploadPhotoAsync(img);
                        if (result.Error != null)
                        {
                            Console.WriteLine("❌ Lỗi upload ảnh: " + result.Error.Message);
                            return BadRequest(new { message = "Lỗi upload ảnh", error = result.Error.Message });
                        }

                        var newImage = new AnhTinDang
                        {
                            MaTinDang = post.MaTinDang,
                            DuongDan = result.SecureUrl.ToString(),
                            LoaiMedia = MediaType.Image,
                            Order = 0, // Tạm thời set = 0, sẽ cập nhật sau
                            TinDang = post
                        };

                        _context.AnhTinDangs.Add(newImage);
                        newlyUploadedImages.Add(newImage);
                        Console.WriteLine($"✅ Đã upload ảnh mới #{i + 1}");
                    }
                }

                if (newVideos != null && newVideos.Count > 0)
                {
                    Console.WriteLine($"🎥 Đang upload {newVideos.Count} video mới");
                    for (int i = 0; i < newVideos.Count; i++)
                    {
                        var vid = newVideos[i];
                        var result = await _photoService.UploadVideoAsync(vid);
                        if (result.Error != null)
                        {
                            Console.WriteLine("❌ Lỗi upload video: " + result.Error.Message);
                            return BadRequest(new { message = "Lỗi upload video", error = result.Error.Message });
                        }

                        var newVideo = new AnhTinDang
                        {
                            MaTinDang = post.MaTinDang,
                            DuongDan = result.SecureUrl.ToString(),
                            LoaiMedia = MediaType.Video,
                            Order = 0, // Tạm thời set = 0, sẽ cập nhật sau
                            TinDang = post
                        };

                        _context.AnhTinDangs.Add(newVideo);
                        newlyUploadedVideos.Add(newVideo);
                        Console.WriteLine($"✅ Đã upload video mới #{i + 1}");
                    }
                }

                // Lưu để có ID cho các media mới
                await _context.SaveChangesAsync();

                // **BƯỚC 3: LẤY LẠI DỮ LIỆU ĐẦY ĐỦ**
                post = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                var allMedia = post.AnhTinDangs.ToList();
                Console.WriteLine($"📊 Tổng cộng {allMedia.Count} media (bao gồm cả mới)");

                // **BƯỚC 4: TÍNH TOÁN THỨ TỰ MỚI DỰA TRÊN ORDER MAP**

                // Tạo dictionary để map mediaId -> finalOrder
                var finalOrderMap = new Dictionary<int, int>();

                // **4.1: Xử lý images dựa trên imageOrderMap**
                for (int i = 0; i < imgOrderMap.Count; i++)
                {
                    var orderItem = imgOrderMap[i];
                    var type = orderItem.type?.ToString();
                    var finalOrder = i + 1; // Vị trí cuối cùng bắt đầu từ 1

                    if (type == "old")
                    {
                        var mediaId = Convert.ToInt32(orderItem.id);
                        finalOrderMap[mediaId] = finalOrder;
                        Console.WriteLine($"📸 Ảnh cũ ID={mediaId} -> Order={finalOrder}");
                    }
                    else if (type == "new")
                    {
                        var fileIndex = Convert.ToInt32(orderItem.fileIndex);
                        if (fileIndex < newlyUploadedImages.Count)
                        {
                            var newImage = newlyUploadedImages[fileIndex];
                            finalOrderMap[newImage.MaAnh] = finalOrder;
                            Console.WriteLine($"📸 Ảnh mới #{fileIndex} (ID={newImage.MaAnh}) -> Order={finalOrder}");
                        }
                    }
                }

                // **4.2: Xử lý videos dựa trên videoOrderMap**
                // Video bắt đầu từ order sau tất cả images
                int videoStartOrder = imgOrderMap.Count + 1;

                for (int i = 0; i < vidOrderMap.Count; i++)
                {
                    var orderItem = vidOrderMap[i];
                    var type = orderItem.type?.ToString();
                    var finalOrder = videoStartOrder + i;

                    if (type == "old")
                    {
                        var mediaId = Convert.ToInt32(orderItem.id);
                        finalOrderMap[mediaId] = finalOrder;
                        Console.WriteLine($"🎥 Video cũ ID={mediaId} -> Order={finalOrder}");
                    }
                    else if (type == "new")
                    {
                        var fileIndex = Convert.ToInt32(orderItem.fileIndex);
                        if (fileIndex < newlyUploadedVideos.Count)
                        {
                            var newVideo = newlyUploadedVideos[fileIndex];
                            finalOrderMap[newVideo.MaAnh] = finalOrder;
                            Console.WriteLine($"🎥 Video mới #{fileIndex} (ID={newVideo.MaAnh}) -> Order={finalOrder}");
                        }
                    }
                }

                // **BƯỚC 5: CẬP NHẬT THỨ TỰ CHO TẤT CẢ MEDIA**
                bool hasOrderChanged = false;
                foreach (var media in allMedia)
                {
                    if (finalOrderMap.ContainsKey(media.MaAnh))
                    {
                        var newOrder = finalOrderMap[media.MaAnh];
                        if (media.Order != newOrder)
                        {
                            Console.WriteLine($"🔄 Cập nhật Order cho media ID={media.MaAnh}: {media.Order} -> {newOrder}");
                            media.Order = newOrder;
                            hasOrderChanged = true;
                            _context.Entry(media).Property(x => x.Order).IsModified = true;
                        }
                    }
                }

                // **BƯỚC 6: LƯU THAY ĐỔI THỨ TỰ CUỐI CÙNG**
                if (hasOrderChanged)
                {
                    Console.WriteLine("💾 Đang lưu thay đổi thứ tự cuối cùng...");
                    await _context.SaveChangesAsync();
                }

                // **BƯỚC 7: SIGNALR NOTIFICATION**
                var updatedPost = new
                {
                    MaTinDang = post.MaTinDang,
                    TieuDe = post.TieuDe,
                    Gia = post.Gia,
                    AnhDaiDien = post.AnhTinDangs?.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan ?? ""
                };

                Console.WriteLine($"[SignalR] Đang gửi CapNhatTinDang cho MaTinDang={updatedPost.MaTinDang}");
                await _hubContext.Clients.All.SendAsync("CapNhatTinDang", updatedPost);

                // **BƯỚC 8: LẤY DỮ LIỆU MỚI NHẤT ĐỂ TRẢ VỀ**
                var finalPost = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                return Ok(new
                {
                    message = "Cập nhật thành công",
                    MaTinDang = finalPost.MaTinDang,
                    TotalMedia = finalPost.AnhTinDangs.Count,
                    HasOrderChanged = hasOrderChanged,
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
                Console.WriteLine("❌ LỖI CẬP NHẬT TIN:");
                Console.WriteLine("Message: " + ex.Message);
                if (ex.InnerException != null)
                    Console.WriteLine("InnerException: " + ex.InnerException.Message);
                Console.WriteLine("StackTrace: " + ex.StackTrace);

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

        // DELETE api/tindang/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTinDang(int id)
        {
            var tinDang = await _context.TinDangs
                .Include(t => t.AnhTinDangs)
                .FirstOrDefaultAsync(t => t.MaTinDang == id);

            if (tinDang == null)
                return NotFound(new { message = "Không tìm thấy tin đăng" });

            // Xóa ảnh trên Cloudinary hoặc trong thư mục tạm
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
                        System.IO.File.Delete(localFilePath);
                }
            }

            // Xóa bảng phụ có khóa ngoại tới TinDang
            _context.AnhTinDangs.RemoveRange(_context.AnhTinDangs.Where(a => a.MaTinDang == id));
            _context.TinDangYeuThichs.RemoveRange(_context.TinDangYeuThichs.Where(t => t.MaTinDang == id));
            _context.VideoComments.RemoveRange(_context.VideoComments.Where(c => c.MaTinDang == id));
            _context.VideoLikes.RemoveRange(_context.VideoLikes.Where(l => l.MaTinDang == id));
            _context.VideoViews.RemoveRange(_context.VideoViews.Where(v => v.MaTinDang == id));

            // Xóa các cuộc trò chuyện liên quan tới TinDang
            var cuocTros = await _context.CuocTroChuyens.Where(c => c.MaTinDang == id).ToListAsync();
            foreach (var c in cuocTros)
            {
                _context.TinNhans.RemoveRange(_context.TinNhans.Where(t => t.MaCuocTroChuyen == c.MaCuocTroChuyen));
                _context.NguoiThamGias.RemoveRange(_context.NguoiThamGias.Where(n => n.MaCuocTroChuyen == c.MaCuocTroChuyen));
            }
            _context.CuocTroChuyens.RemoveRange(cuocTros);

            // Cuối cùng: Xóa TinDang
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

    }

}
