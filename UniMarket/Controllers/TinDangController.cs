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
using System.Text.Json;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Newtonsoft.Json;
using UniMarket.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using UniMarket.Services.Recommendation; 
using UniMarket.Services.PriceAnalysis;
using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Bson.Serialization; 
namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TinDangController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PhotoService _photoService;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly VideoRecommendationService _recommendationService;
        private readonly UniMarket.Services.PriceAnalysis.PriceAnalysisService _priceService;
        private readonly UniMarket.Services.TinDangDetailService _mongoService;
        private readonly string _imagesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "categories");

       
        public TinDangController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            PhotoService photoService,
            IWebHostEnvironment env,
            IHubContext<ChatHub> hubContext,
            VideoRecommendationService recommendationService,
            UniMarket.Services.PriceAnalysis.PriceAnalysisService priceService,
            UniMarket.Services.TinDangDetailService mongoService)
        {
            _context = context;
            _userManager = userManager;
            _photoService = photoService;
            _env = env;
            _hubContext = hubContext;
            _recommendationService = recommendationService;
            _priceService = priceService;
            _mongoService = mongoService; 
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
                    ThongTinChiTiet = p.ThongTinChiTiet,
                    p.VideoUrl,
                    Images = p.AnhTinDangs
                        .OrderBy(a => a.Order)
                        .Select(a =>
                            a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                ? a.DuongDan
                                : (a.DuongDan.StartsWith("/")
                                    ? a.DuongDan
                                    : $"/images/Posts/{a.DuongDan}")
                        ),
                    NguoiBan = p.NguoiBan.FullName,
                    TinhThanh = p.TinhThanh.TenTinhThanh,
                    QuanHuyen = p.QuanHuyen.TenQuanHuyen,
                    DanhMuc = p.DanhMuc.TenDanhMuc,
                    DanhMucCha = p.DanhMuc.DanhMucCha.TenDanhMucCha,

                    // Lưu ý: Ở API thường này bạn đang đếm TinDangYeuThich
                    SavedCount = p.TinDangYeuThichs.Count()
                })
                .ToListAsync();

            if (posts == null || !posts.Any())
                return NotFound("Không có tin đăng nào.");

            return Ok(posts);
        }

        // =============================================================
        // ✅ 3. API ĐỀ XUẤT (Đã sửa đổi và tối ưu)
        // =============================================================
        [HttpGet("get-recommended-posts")]
        [AllowAnonymous]
        public async Task<IActionResult> GetRecommendedPosts([FromQuery] int limit = 20)
        {
            try
            {
                var userId = User.Identity != null && User.Identity.IsAuthenticated
                    ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                    : null;

                // -------------------------------------------------------------
                // A. LẤY DANH SÁCH ID ĐỀ XUẤT TỪ AI
                // -------------------------------------------------------------
                // Sử dụng _recommendationService đã được inject ở Constructor
                // Sử dụng tên hàm mới là GetRecommendedPostIds
                var recommendedIds = await _recommendationService.GetRecommendedPostIds(
                    userId,
                    new List<int>(), // Danh sách ID đã xem (nếu có)
                    limit,
                    isVideoOnly: false // 👈 QUAN TRỌNG: Truyền false để lấy cả Tin đăng ảnh và Video
                );

                if (recommendedIds == null || !recommendedIds.Any())
                {
                    return Ok(new List<object>());
                }

                // -------------------------------------------------------------
                // B. LẤY CHI TIẾT TIN ĐĂNG (Bulk Query)
                // -------------------------------------------------------------
                var postsData = await _context.TinDangs
                    .AsNoTracking()
                    .Where(p => recommendedIds.Contains(p.MaTinDang))
                    .Include(p => p.NguoiBan)
                    .Include(p => p.AnhTinDangs)
                    .Include(p => p.TinhThanh)
                    .Include(p => p.QuanHuyen)
                    .Include(p => p.DanhMuc).ThenInclude(d => d.DanhMucCha)
                    .ToListAsync();

                var foundIds = postsData.Select(p => p.MaTinDang).ToList();

                // -------------------------------------------------------------
                // C. LẤY SỐ LIỆU TƯƠNG TÁC (Tách biệt hoàn toàn)
                // -------------------------------------------------------------

                // 1. Đếm Video Save (Lưu video để xem lại)
                var videoSaveCounts = await _context.VideoTinDangSaves
                    .Where(x => foundIds.Contains(x.MaTinDang))
                    .GroupBy(x => x.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // 2. Đếm Favorite Post (Yêu thích tin đăng)
                var postFavCounts = await _context.TinDangYeuThichs
                    .Where(x => foundIds.Contains(x.MaTinDang))
                    .GroupBy(x => x.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // 3. Đếm Like Video
                var likeCounts = await _context.VideoLikes
                    .Where(x => foundIds.Contains(x.MaTinDang))
                    .GroupBy(x => x.MaTinDang)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count);

                // -------------------------------------------------------------
                // D. LẤY TRẠNG THÁI USER (Nếu đã đăng nhập)
                // -------------------------------------------------------------
                var userVideoSavedIds = new HashSet<int>();
                var userPostFavoritedIds = new HashSet<int>();
                var userLikedIds = new HashSet<int>();

                if (!string.IsNullOrEmpty(userId))
                {
                    var savedVideos = await _context.VideoTinDangSaves
                        .Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                        .Select(s => s.MaTinDang).ToListAsync();
                    userVideoSavedIds = new HashSet<int>(savedVideos);

                    var favPosts = await _context.TinDangYeuThichs
                        .Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                        .Select(s => s.MaTinDang).ToListAsync();
                    userPostFavoritedIds = new HashSet<int>(favPosts);

                    var likes = await _context.VideoLikes
                        .Where(l => l.UserId == userId && foundIds.Contains(l.MaTinDang))
                        .Select(l => l.MaTinDang).ToListAsync();
                    userLikedIds = new HashSet<int>(likes);
                }

                // -------------------------------------------------------------
                // E. GHÉP DỮ LIỆU & MAPPING (Giữ thứ tự AI)
                // -------------------------------------------------------------
                var result = recommendedIds
                    .Join(postsData, id => id, p => p.MaTinDang, (id, p) => p) // Join để giữ thứ tự sort của AI
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
                        p.TrangThai,
                        p.VideoUrl,
                        p.SoLuotXem,

                        // Xử lý Ảnh (Logic từ code cũ của bạn)
                        Images = p.AnhTinDangs != null
                            ? p.AnhTinDangs.OrderBy(a => a.Order)
                                .Select(a => a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                    ? a.DuongDan
                                    : (a.DuongDan.StartsWith("/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}"))
                                .ToList()
                            : new List<string>(),

                        // Thông tin người bán
                        NguoiBan = p.NguoiBan != null ? new
                        {
                            Id = p.NguoiBan.Id,
                            FullName = p.NguoiBan.FullName,
                            Avatar = p.NguoiBan.AvatarUrl,
                            PhoneNumber = p.NguoiBan.PhoneNumber
                        } : null,

                        TinhThanh = p.TinhThanh?.TenTinhThanh,
                        QuanHuyen = p.QuanHuyen?.TenQuanHuyen,
                        DanhMuc = p.DanhMuc?.TenDanhMuc,
                        DanhMucCha = p.DanhMuc?.DanhMucCha?.TenDanhMucCha,

                        // ⭐ SỐ LIỆU TƯƠNG TÁC (ĐÃ TÁCH BIỆT)
                        SoNguoiLuuVideo = videoSaveCounts.GetValueOrDefault(p.MaTinDang, 0),
                        SoLuotYeuThich = postFavCounts.GetValueOrDefault(p.MaTinDang, 0),
                        SoLuotLike = likeCounts.GetValueOrDefault(p.MaTinDang, 0),

                        // ⭐ TRẠNG THÁI USER (ĐÃ TÁCH BIỆT)
                        IsSaved = userVideoSavedIds.Contains(p.MaTinDang),       // Bookmark
                        IsFavorited = userPostFavoritedIds.Contains(p.MaTinDang),// Tim/Giỏ hàng
                        IsLiked = userLikedIds.Contains(p.MaTinDang)             // Like
                    })
                    .ToList();

                return Ok(result);
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
        [FromForm] List<IFormFile> images, 
        [FromForm] string userId,
        [FromForm] int categoryId,
        [FromForm] string categoryName,
        [FromForm] bool canNegotiate,
        [FromForm] string? thongTinChiTiet) 
        {
            // Sử dụng Transaction để đảm bảo tính toàn vẹn dữ liệu (SQL + Mongo)
            using var transaction = _context.Database.BeginTransaction();
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

                // Kiểm tra danh mục
                if (!await _context.DanhMucs.AnyAsync(c => c.MaDanhMuc == categoryId))
                    return BadRequest("Danh mục không hợp lệ!");

                // Kiểm tra số lượng file
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

                        if (isVideo) videoFiles.Add(file);
                        else imageFiles.Add(file);
                    }

                    if (imageFiles.Count > 7) return BadRequest("Chỉ được phép tải lên tối đa 7 ảnh.");
                    if (videoFiles.Count > 1) return BadRequest("Chỉ được phép tải lên tối đa 1 video.");
                }

                // =================================
                // 2. TẠO ĐỐI TƯỢNG TIN ĐĂNG (SQL SERVER)
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
                    NgayDang = DateTime.UtcNow, 
                    TrangThai = TrangThaiTinDang.ChoDuyet,
                    MaDanhMuc = categoryId,
                    AnhTinDangs = new List<AnhTinDang>(),
                    VideoUrl = null 
                };

                // =================================
                // 3. UPLOAD FILE LÊN CLOUDINARY
                // =================================
                int order = 1;

                // Xử lý ảnh
                foreach (var image in imageFiles)
                {
                    var result = await _photoService.UploadPhotoAsync(image);
                    if (result.Error != null) return BadRequest(new { message = "Lỗi upload ảnh", error = result.Error.Message });

                    post.AnhTinDangs.Add(new AnhTinDang
                    {
                        DuongDan = result.SecureUrl.ToString(),
                        LoaiMedia = MediaType.Image,
                        Order = order++,
                        TinDang = post
                    });
                }

                // Xử lý video
                foreach (var video in videoFiles)
                {
                    var result = await _photoService.UploadVideoAsync(video);
                    if (result.Error != null) return BadRequest(new { message = "Lỗi upload video", error = result.Error.Message });

                    var newVideo = new AnhTinDang
                    {
                        DuongDan = result.SecureUrl.ToString(),
                        LoaiMedia = MediaType.Video,
                        Order = order++,
                        TinDang = post
                    };
                    post.AnhTinDangs.Add(newVideo);

                    // Set VideoUrl đại diện
                    if (string.IsNullOrEmpty(post.VideoUrl))
                    {
                        post.VideoUrl = newVideo.DuongDan;
                    }
                }

                // =================================
                // 4. LƯU VÀO SQL SERVER
                // =================================
                _context.TinDangs.Add(post);
                await _context.SaveChangesAsync(); // Lúc này post.MaTinDang đã được sinh ra

                // =================================
                // 5. LƯU CHI TIẾT VÀO MONGODB
                // =================================
                if (!string.IsNullOrEmpty(thongTinChiTiet))
                {
                    try
                    {
                        // Parse chuỗi JSON từ Frontend thành BsonDocument
                        var bsonDoc = MongoDB.Bson.BsonDocument.Parse(thongTinChiTiet);

                        var mongoDetail = new UniMarket.Models.Mongo.TinDangDetail
                        {
                            MaTinDang = post.MaTinDang, // Liên kết ID từ SQL
                            ChiTiet = bsonDoc
                        };

                        await _mongoService.CreateAsync(mongoDetail);
                    }
                    catch (Exception mongoEx)
                    {
                        // Nếu lỗi format JSON hoặc lỗi kết nối Mongo -> Rollback SQL
                        throw new Exception("Lỗi lưu chi tiết MongoDB: " + mongoEx.Message);
                    }
                }

                // =================================
                // 6. COMMIT TRANSACTION (HOÀN TẤT)
                // =================================
                await transaction.CommitAsync();

                var responseMessage = $"Bài đăng đã được thêm thành công và đang chờ duyệt! " +
                                      $"(Đã tải lên: {imageFiles.Count} ảnh, {videoFiles.Count} video)";

                return Ok(new
                {
                    message = responseMessage,
                    imageCount = imageFiles.Count,
                    videoCount = videoFiles.Count,
                    newPostId = post.MaTinDang
                });
            }
            catch (Exception ex)
            {
                // Rollback lại toàn bộ thao tác SQL nếu có bất kỳ lỗi gì xảy ra
                await transaction.RollbackAsync();

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
        // =========================================================================
        // 1. API CẬP NHẬT TIN ĐĂNG (PUT) - HYBRID (SQL + MONGODB)
        // =========================================================================
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
    [FromForm] string? videoOrderMap,
    [FromForm] string? thongTinChiTiet) // JSON từ Frontend
        {
            Console.WriteLine($"\n--- [START] CẬP NHẬT TIN ĐĂNG ID: {id} ---");

            // ✅ SỬ DỤNG TRANSACTION: Đảm bảo SQL và MongoDB cùng thành công hoặc cùng thất bại
            using var transaction = _context.Database.BeginTransaction();
            try
            {
                // =========================================================
                // BƯỚC 1: KIỂM TRA TỒN TẠI (SQL SERVER)
                // =========================================================
                var post = await _context.TinDangs
                    .Include(td => td.AnhTinDangs)
                    .FirstOrDefaultAsync(td => td.MaTinDang == id);

                if (post == null)
                {
                    return NotFound(new { message = "Không tìm thấy tin đăng" });
                }

                // =========================================================
                // BƯỚC 2: CẬP NHẬT THÔNG TIN CƠ BẢN (SQL SERVER)
                // =========================================================
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
                post.TrangThai = TrangThaiTinDang.ChoDuyet; // Reset về chờ duyệt

                // Lưu tạm vào SQL để lock dòng dữ liệu
                await _context.SaveChangesAsync();

                // =========================================================
                // BƯỚC 3: CẬP NHẬT THÔNG TIN CHI TIẾT (MONGODB)
                // 🔥 FIX: XÓA CŨ -> TẠO MỚI (Tránh lỗi lồng nhau và dữ liệu rác)
                // =========================================================
                if (!string.IsNullOrEmpty(thongTinChiTiet))
                {
                    try
                    {
                        // 1. Chuẩn bị dữ liệu SẠCH từ Frontend
                        // Parse JSON: {"Hang": "Samsung", "DongMay": "S24"...}
                        var bsonDoc = MongoDB.Bson.BsonDocument.Parse(thongTinChiTiet);

                        // 2. Tạo object MỚI TINH (Clean Object)
                        // Cấu trúc mong muốn: { MaTinDang: 20, ChiTiet: { ...DATA... } }
                        var cleanDetail = new UniMarket.Models.Mongo.TinDangDetail
                        {
                            MaTinDang = id,
                            ChiTiet = bsonDoc
                        };

                        // 3. Kiểm tra xem tin này đã có trong Mongo chưa
                        var existingDetail = await _mongoService.GetByMaTinDangAsync(id);

                        if (existingDetail != null)
                        {
                            // 🛑 QUAN TRỌNG: XÓA BẢN GHI CŨ BỊ LỖI
                            // Thay vì Update (dễ bị merge/lồng nhau), ta xóa luôn bản ghi cũ
                            // Bạn cần đảm bảo Service có hàm RemoveAsync hoặc DeleteAsync
                            await _mongoService.DeleteByIdAsync(existingDetail.Id);
                            Console.WriteLine("[MONGO] Đã xóa dữ liệu cũ (để tránh lỗi lồng nhau/zombie data).");
                        }

                        // 4. TẠO MỚI LẠI TỪ ĐẦU
                        await _mongoService.CreateAsync(cleanDetail);
                        Console.WriteLine("[MONGO] Đã tạo mới chi tiết chuẩn.");
                    }
                    catch (Exception ex)
                    {
                        // Ghi log lỗi nhưng ném ra để transaction SQL bên ngoài biết đường rollback
                        Console.WriteLine($"[MONGO ERROR] {ex.Message}");
                        // Nếu service của bạn chưa có hàm RemoveAsync, hãy thêm vào Service:
                        // public async Task RemoveAsync(string id) => await _collection.DeleteOneAsync(x => x.Id == id);
                        throw new Exception($"Lỗi cập nhật MongoDB: {ex.Message}");
                    }
                }

                // =========================================================
                // BƯỚC 4: XỬ LÝ MEDIA (XÓA CŨ - THÊM MỚI - SẮP XẾP)
                // =========================================================

                // 4.1 Parse JSON maps từ Frontend
                var idsToDeleteImage = (string.IsNullOrEmpty(oldImagesToDelete) || oldImagesToDelete == "null")
                    ? new List<int>() : JsonConvert.DeserializeObject<List<int>>(oldImagesToDelete);
                var idsToDeleteVideo = (string.IsNullOrEmpty(oldVideosToDelete) || oldVideosToDelete == "null")
                    ? new List<int>() : JsonConvert.DeserializeObject<List<int>>(oldVideosToDelete);

                var imgOrderMapList = (string.IsNullOrEmpty(imageOrderMap) || imageOrderMap == "null")
                    ? new List<dynamic>() : JsonConvert.DeserializeObject<List<dynamic>>(imageOrderMap);
                var vidOrderMapList = (string.IsNullOrEmpty(videoOrderMap) || videoOrderMap == "null")
                    ? new List<dynamic>() : JsonConvert.DeserializeObject<List<dynamic>>(videoOrderMap);

                var allIdsToDelete = idsToDeleteImage.Concat(idsToDeleteVideo).ToList();

                // 4.2 Xóa Media cũ (Cloudinary + DB)
                if (allIdsToDelete.Any())
                {
                    var mediaToDelete = post.AnhTinDangs.Where(m => allIdsToDelete.Contains(m.MaAnh)).ToList();
                    foreach (var media in mediaToDelete)
                    {
                        // Xóa trên Cloudinary nếu là link online
                        if (!string.IsNullOrEmpty(media.DuongDan) && media.DuongDan.StartsWith("http"))
                        {
                            await DeleteCloudinaryPhotoByUrlAsync(media.DuongDan);
                        }
                        // Xóa trong DB
                        _context.AnhTinDangs.Remove(media);
                    }
                    await _context.SaveChangesAsync();
                }

                // 4.3 Upload Media mới
                var newlyUploadedImages = new List<AnhTinDang>();
                var newlyUploadedVideos = new List<AnhTinDang>();

                // Upload ảnh mới
                if (newImages != null)
                {
                    foreach (var img in newImages)
                    {
                        var result = await _photoService.UploadPhotoAsync(img);
                        if (result.Error != null) throw new Exception("Lỗi upload ảnh: " + result.Error.Message);

                        var newImg = new AnhTinDang { MaTinDang = id, DuongDan = result.SecureUrl.ToString(), LoaiMedia = MediaType.Image, Order = 0 };
                        _context.AnhTinDangs.Add(newImg);
                        newlyUploadedImages.Add(newImg);
                    }
                }

                // Upload video mới
                if (newVideos != null)
                {
                    foreach (var vid in newVideos)
                    {
                        var result = await _photoService.UploadVideoAsync(vid);
                        if (result.Error != null) throw new Exception("Lỗi upload video: " + result.Error.Message);

                        var newVid = new AnhTinDang { MaTinDang = id, DuongDan = result.SecureUrl.ToString(), LoaiMedia = MediaType.Video, Order = 0 };
                        _context.AnhTinDangs.Add(newVid);
                        newlyUploadedVideos.Add(newVid);
                    }
                }

                // Lưu tạm để sinh ID cho ảnh/video mới (cần ID để sắp xếp)
                if (newlyUploadedImages.Any() || newlyUploadedVideos.Any())
                {
                    await _context.SaveChangesAsync();
                }

                // 4.4 Sắp xếp lại thứ tự (Re-order logic)
                var allMedia = await _context.AnhTinDangs.Where(a => a.MaTinDang == id).ToListAsync();
                var finalOrderMap = new Dictionary<int, int>();

                // Map Order Ảnh
                for (int i = 0; i < imgOrderMapList.Count; i++)
                {
                    var item = imgOrderMapList[i];
                    string type = item.type;

                    int mid = 0;
                    int fidx = 0;

                    if (type == "old" && int.TryParse(item.id.ToString(), out mid))
                        finalOrderMap[mid] = i + 1;
                    else if (type == "new" && int.TryParse(item.fileIndex.ToString(), out fidx) && fidx < newlyUploadedImages.Count)
                        finalOrderMap[newlyUploadedImages[fidx].MaAnh] = i + 1;
                }

                // Map Order Video
                int videoStartOrder = imgOrderMapList.Count + 1;
                for (int i = 0; i < vidOrderMapList.Count; i++)
                {
                    var item = vidOrderMapList[i];
                    string type = item.type;

                    int mid = 0;
                    int fidx = 0;

                    if (type == "old" && int.TryParse(item.id.ToString(), out mid))
                        finalOrderMap[mid] = videoStartOrder + i;
                    else if (type == "new" && int.TryParse(item.fileIndex.ToString(), out fidx) && fidx < newlyUploadedVideos.Count)
                        finalOrderMap[newlyUploadedVideos[fidx].MaAnh] = videoStartOrder + i;
                }

                // Apply Order vào DB
                foreach (var media in allMedia)
                {
                    if (finalOrderMap.ContainsKey(media.MaAnh))
                    {
                        if (media.Order != finalOrderMap[media.MaAnh])
                        {
                            media.Order = finalOrderMap[media.MaAnh];
                            _context.Entry(media).Property(x => x.Order).IsModified = true;
                        }
                    }
                }

                // Cập nhật Thumbnail Video
                var firstVideo = allMedia.Where(m => m.LoaiMedia == MediaType.Video).OrderBy(m => m.Order).FirstOrDefault();
                post.VideoUrl = firstVideo?.DuongDan;

                await _context.SaveChangesAsync();

                // =========================================================
                // BƯỚC 5: GỬI THÔNG BÁO SIGNALR & HOÀN TẤT
                // =========================================================
                var updatedPostSignalR = new
                {
                    MaTinDang = post.MaTinDang,
                    TieuDe = post.TieuDe,
                    Gia = post.Gia,
                    AnhDaiDien = allMedia.OrderBy(a => a.Order).FirstOrDefault()?.DuongDan ?? "",
                    VideoUrl = post.VideoUrl
                };
                await _hubContext.Clients.All.SendAsync("CapNhatTinDang", updatedPostSignalR);

                await transaction.CommitAsync();

                return Ok(new
                {
                    message = "Cập nhật thành công",
                    MaTinDang = post.MaTinDang,
                    VideoUrl = post.VideoUrl
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[ERROR] Lỗi PutTinDang: {ex.Message}");
                if (ex.InnerException != null) Console.WriteLine($"Inner: {ex.InnerException.Message}");
                return StatusCode(500, new { message = "Lỗi server khi cập nhật", error = ex.Message });
            }
        }

        // =========================================================================
        // 2. API LẤY CHI TIẾT ĐỂ HIỂN THỊ HOẶC SỬA (GET)
        // =========================================================================
        [HttpGet("get-post/{id}")]
        public async Task<IActionResult> GetPostById(int id)
        {
            // BƯỚC 1: Lấy tin từ SQL Server
            var post = await _context.TinDangs
                .Include(p => p.AnhTinDangs)  // Lấy ảnh
                .Include(p => p.DanhMuc)      // Lấy danh mục
                .FirstOrDefaultAsync(p => p.MaTinDang == id);

            if (post == null)
            {
                return NotFound(new { message = "Không tìm thấy tin đăng với mã này." });
            }

            // BƯỚC 2: Lấy chi tiết từ MongoDB (nếu có)
            try
            {
                var mongoDetail = await _mongoService.GetByMaTinDangAsync(id);

                if (mongoDetail != null && mongoDetail.ChiTiet != null)
                {
                    // Xóa trường _id của Mongo để JSON đẹp hơn
                    if (mongoDetail.ChiTiet.Contains("_id"))
                        mongoDetail.ChiTiet.Remove("_id");

                    // Convert BsonDocument sang Object .NET để trả về JSON
                    var dotNetObj = MongoDB.Bson.BsonTypeMapper.MapToDotNetValue(mongoDetail.ChiTiet);

                    // Gán vào thuộc tính tạm [NotMapped] để trả về Frontend
                    post.ChiTietObj = dotNetObj;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Lỗi lấy chi tiết MongoDB: {ex.Message}");
                // Không return lỗi, vẫn trả về dữ liệu cơ bản để người dùng có thể sửa phần khác
            }

            return Ok(post);
        }

        // =========================================================================
        // 3. HÀM HỖ TRỢ: XÓA ẢNH CLOUDINARY
        // =========================================================================
        public async Task<bool> DeleteCloudinaryPhotoByUrlAsync(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return false;

            try
            {
                var uri = new Uri(imageUrl);
                var segments = uri.Segments;

                // Tìm vị trí "upload/" trong URL
                int uploadIndex = segments.ToList().FindIndex(s => s.Equals("upload/", StringComparison.OrdinalIgnoreCase));
                if (uploadIndex < 0) uploadIndex = segments.ToList().FindIndex(s => s.StartsWith("upload", StringComparison.OrdinalIgnoreCase));

                if (uploadIndex >= 0 && uploadIndex + 2 < segments.Length)
                {
                    // Trích xuất Public ID
                    var pathSegments = segments.Skip(uploadIndex + 2);
                    var publicIdPath = string.Join("", pathSegments).Trim('/');
                    var publicId = Path.ChangeExtension(publicIdPath, null).Replace("\\", "/");

                    // Xác định loại file (Ảnh hay Video)
                    var lowerUrl = imageUrl.ToLower();
                    ResourceType resourceType = ResourceType.Image;

                    if (lowerUrl.Contains("/video/") || lowerUrl.EndsWith(".mp4") || lowerUrl.EndsWith(".mov"))
                        resourceType = ResourceType.Video;

                    // Gọi service xóa
                    var deletionResult = await _photoService.DeletePhotoAsync(publicId, resourceType);
                    return deletionResult.Result == "ok";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi xóa Cloudinary: " + ex.Message);
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
            // =======================================================
            // BƯỚC 1: LẤY TIN ĐĂNG CHÍNH TỪ SQL SERVER
            // =======================================================
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

            // =======================================================
            // BƯỚC 2: LẤY CHI TIẾT KỸ THUẬT TỪ MONGODB (MỚI)
            // =======================================================
            object? chiTietMongoObj = null;
            try
            {
                // Gọi service MongoDB để tìm chi tiết theo MaTinDang
                var mongoResult = await _mongoService.GetByMaTinDangAsync(id);

                if (mongoResult != null && mongoResult.ChiTiet != null)
                {
                    // Xóa trường _id của Mongo để JSON trả về sạch đẹp
                    if (mongoResult.ChiTiet.Contains("_id"))
                        mongoResult.ChiTiet.Remove("_id");

                    // Convert BsonDocument sang Object .NET thuần để Frontend đọc được
                    chiTietMongoObj = MongoDB.Bson.BsonTypeMapper.MapToDotNetValue(mongoResult.ChiTiet);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi lấy dữ liệu MongoDB: {ex.Message}");
                // Không return lỗi, vẫn trả về tin đăng nhưng thiếu chi tiết (để app không crash)
            }

            // =======================================================
            // BƯỚC 3: LẤY CÁC TIN TƯƠNG TỰ (TỪ SQL)
            // =======================================================

            // 3.1 Tương tự theo Danh mục
            var similarPostsByCategory = await _context.TinDangs
                .Where(p => p.MaDanhMuc == post.MaDanhMuc && p.MaTinDang != post.MaTinDang && p.TrangThai == TrangThaiTinDang.DaDuyet)
                .Include(p => p.AnhTinDangs)
                .Include(p => p.NguoiBan)
                .Include(p => p.TinhThanh)
                .Include(p => p.QuanHuyen)
                .OrderByDescending(p => p.NgayDang) // Nên sắp xếp tin mới nhất
                .Take(8) // Giới hạn số lượng để tối ưu query
                .Select(p => new
                {
                    p.MaTinDang,
                    p.TieuDe,
                    p.MoTa,
                    p.Gia,
                    p.TinhTrang,
                    p.DiaChi,
                    p.NgayDang,
                    p.VideoUrl,
                    Images = p.AnhTinDangs
                        .OrderBy(a => a.Order)
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

            // 3.2 Tương tự theo Người bán
            var similarPostsBySeller = await _context.TinDangs
                .Where(p => p.MaNguoiBan == post.MaNguoiBan && p.MaTinDang != post.MaTinDang && p.TrangThai == TrangThaiTinDang.DaDuyet)
                .Include(p => p.AnhTinDangs)
                .Include(p => p.NguoiBan)
                .Include(p => p.TinhThanh)
                .Include(p => p.QuanHuyen)
                .OrderByDescending(p => p.NgayDang)
                .Take(8)
                .Select(p => new
                {
                    p.MaTinDang,
                    p.TieuDe,
                    p.MoTa,
                    p.Gia,
                    p.TinhTrang,
                    p.DiaChi,
                    p.NgayDang,
                    p.VideoUrl,
                    Images = p.AnhTinDangs
                        .OrderBy(a => a.Order)
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

            // =======================================================
            // BƯỚC 4: XỬ LÝ ẢNH CỦA TIN CHÍNH
            // =======================================================
            var postImages = post.AnhTinDangs
                .OrderBy(a => a.Order)
                .Select(a =>
                    (a.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase) || a.DuongDan.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                    ? a.DuongDan
                    : (a.DuongDan.StartsWith("/images/Posts/") ? a.DuongDan : $"/images/Posts/{a.DuongDan}")
                ).ToList();

            // =======================================================
            // BƯỚC 5: TRẢ VỀ KẾT QUẢ
            // =======================================================
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
                    post.CoTheThoaThuan,

                    // 👇👇👇 QUAN TRỌNG: Gắn dữ liệu MongoDB vào đây 👇👇👇
                    ChiTietObj = chiTietMongoObj,
                    // 👆 Frontend sẽ gọi: data.Post.chiTietObj.Hang, data.Post.chiTietObj.MauSac ...

                    Images = postImages,
                    NguoiBan = post.NguoiBan.FullName,
                    MaNguoiBan = post.NguoiBan.Id,
                    PhoneNumber = post.NguoiBan.PhoneNumber,
                    TinhThanh = post.TinhThanh.TenTinhThanh,
                    Avatar = post.NguoiBan.AvatarUrl,
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
        [HttpGet("market-price-analysis/{id}")]
        public async Task<IActionResult> GetMarketPriceAnalysis(int id)
        {
            try
            {
                var result = await _priceService.AnalyzePriceAsync(id);

                // Nếu AI trả về false (do không đủ dữ liệu), trả về null cho FE ẩn đi
                if (!result.IsSuccess) return Ok(null);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server", error = ex.Message });
            }
        }
    }

}