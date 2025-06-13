using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;
using UniMarket.Services;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PhotoService _photoService;

        public ChatController(ApplicationDbContext context, PhotoService photoService)
        {
            _context = context;
            _photoService = photoService;
        }

        // POST api/chat/start
        [HttpPost("start")]
        public async Task<IActionResult> StartChat([FromBody] StartChatRequest request)
        {
            if (string.IsNullOrEmpty(request.MaNguoiDung1) || string.IsNullOrEmpty(request.MaNguoiDung2) || request.MaTinDang <= 0)
                return BadRequest("Thông tin không đầy đủ.");

            string GenerateChatId(string u1, string u2, int maTinDang)
            {
                var arr = new[] { u1, u2 };
                Array.Sort(arr);
                return $"{arr[0]}-{arr[1]}-{maTinDang}";
            }

            var maCuocTroChuyen = GenerateChatId(request.MaNguoiDung1, request.MaNguoiDung2, request.MaTinDang);

            var existingChat = await _context.CuocTroChuyens
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

            if (existingChat != null)
            {
                return Ok(new { MaCuocTroChuyen = existingChat.MaCuocTroChuyen });
            }

            var tinDang = await _context.TinDangs.Include(t => t.AnhTinDangs).FirstOrDefaultAsync(t => t.MaTinDang == request.MaTinDang);
            if (tinDang == null) return NotFound("Tin đăng không tồn tại.");

            var newChat = new CuocTroChuyen
            {
                MaCuocTroChuyen = maCuocTroChuyen,
                ThoiGianTao = DateTime.UtcNow,
                IsEmpty = true,
                MaTinDang = tinDang.MaTinDang,
                TieuDeTinDang = tinDang.TieuDe,
                AnhDaiDienTinDang = tinDang.AnhTinDangs?.FirstOrDefault()?.DuongDan ?? "",
                GiaTinDang = tinDang.Gia
            };

            _context.CuocTroChuyens.Add(newChat);

            _context.NguoiThamGias.AddRange(new[]
            {
                new NguoiThamGia { MaCuocTroChuyen = maCuocTroChuyen, MaNguoiDung = request.MaNguoiDung1 },
                new NguoiThamGia { MaCuocTroChuyen = maCuocTroChuyen, MaNguoiDung = request.MaNguoiDung2 }
            });

            await _context.SaveChangesAsync();

            return Ok(new { MaCuocTroChuyen = maCuocTroChuyen });
        }

        // GET api/chat/user/{userId}
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserConversations(string userId)
        {
            var userChats = await _context.CuocTroChuyens
                .Where(c => c.NguoiThamGias.Any(n => n.MaNguoiDung == userId))
                .Select(c => new
                {
                    c.MaCuocTroChuyen,
                    c.ThoiGianTao,
                    c.IsEmpty,
                    c.MaTinDang,
                    TinNhanCuoi = _context.TinNhans
                        .Where(t => t.MaCuocTroChuyen == c.MaCuocTroChuyen)
                        .OrderByDescending(t => t.ThoiGianGui)
                        .Select(t => new
                        {
                            NoiDung = t.NoiDung,
                            MaNguoiGui = t.MaNguoiGui,
                            LoaiTinNhan = t.Loai.ToString().ToLower()
                        })
                        .FirstOrDefault(),
                    MaNguoiConLai = c.NguoiThamGias
                        .Where(n => n.MaNguoiDung != userId)
                        .Select(n => n.MaNguoiDung)
                        .FirstOrDefault(),
                    TenNguoiConLai = c.NguoiThamGias
                        .Where(n => n.MaNguoiDung != userId)
                        .Select(n => n.NguoiDung.FullName)
                        .FirstOrDefault(),
                    TieuDeTinDang = c.TieuDeTinDang,
                    AnhDaiDienTinDang = c.AnhDaiDienTinDang,
                    GiaTinDang = c.GiaTinDang,
                    IsSeller = _context.TinDangs.Any(t => t.MaTinDang == c.MaTinDang && t.MaNguoiBan == userId),
                    HasUnreadMessages = _context.TinNhans
                        .Any(t => t.MaCuocTroChuyen == c.MaCuocTroChuyen && t.MaNguoiGui != userId && !t.DaXem)
                })
                .Where(c => !c.IsSeller || (c.IsSeller && !c.IsEmpty))
                .ToListAsync();

            return Ok(userChats);
        }

        // GET api/chat/history/{maCuocTroChuyen}?userId=xxx
        [HttpGet("history/{maCuocTroChuyen}")]
        public async Task<IActionResult> GetChatHistory(string maCuocTroChuyen, [FromQuery] string userId)
        {
            var messages = await _context.TinNhans
                .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen)
                .Where(t => !_context.TinNhanDaXoas.Any(x => x.TinNhanId == t.MaTinNhan && x.UserId == userId))
                .OrderBy(t => t.ThoiGianGui)
                .Select(t => new
                {
                    t.MaTinNhan,
                    t.MaCuocTroChuyen,
                    t.MaNguoiGui,
                    NoiDung = (t.Loai == LoaiTinNhan.Text) ? t.NoiDung : t.MediaUrl,
                    LoaiTinNhan = t.Loai.ToString().ToLower(),
                    ThoiGianGui = t.ThoiGianGui.ToString("O"),
                    t.DaXem,
                    t.ThoiGianXem
                })
                .ToListAsync();

            return Ok(messages);
        }


        [HttpGet("info/{maCuocTroChuyen}")]
        public async Task<IActionResult> GetChatInfo(string maCuocTroChuyen)
        {
            var chat = await _context.CuocTroChuyens
                .Where(c => c.MaCuocTroChuyen == maCuocTroChuyen)
                .Select(c => new
                {
                    c.MaTinDang,
                    c.TieuDeTinDang,
                    c.GiaTinDang,
                    c.AnhDaiDienTinDang
                }).FirstOrDefaultAsync();

            if (chat == null) return NotFound();

            return Ok(chat);
        }

        [HttpGet("unread-count/{userId}")]
        public async Task<IActionResult> GetUnreadCount(string userId, [FromQuery] List<string> hiddenChatIds)
        {
            var count = await _context.TinNhans
                .Where(t =>
                    t.MaNguoiGui != userId &&
                    !t.DaXem &&
                    !hiddenChatIds.Contains(t.MaCuocTroChuyen) &&
                    _context.NguoiThamGias.Any(n => n.MaCuocTroChuyen == t.MaCuocTroChuyen && n.MaNguoiDung == userId)
                )
                .CountAsync();

            return Ok(new { unreadCount = count });
        }

        [HttpPost("upload-media-chat")]
        public async Task<IActionResult> UploadMediaChat(IFormFile mediaFile)
        {
            if (mediaFile == null)
                return BadRequest("Không có file nào được chọn.");

            string fileExtension = Path.GetExtension(mediaFile.FileName).ToLower();
            if (!(fileExtension == ".jpg" || fileExtension == ".jpeg" || fileExtension == ".png" || fileExtension == ".mp4" || fileExtension == ".avi"))
            {
                return BadRequest("Chỉ hỗ trợ file ảnh (jpg, jpeg, png) và video (mp4, avi).");
            }

            try
            {
                var uploadResult = await _photoService.UploadFileToCloudinaryAsync(mediaFile, "doan-chat");
                if (uploadResult.Error != null)
                    return BadRequest(new { message = "Lỗi khi upload file lên Cloudinary", error = uploadResult.Error.Message });
                return Ok(new { url = uploadResult.SecureUrl.ToString(), publicId = uploadResult.PublicId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi upload media", error = ex.Message });
            }
        }

        // 🆕 API thu hồi tin nhắn text
        [HttpDelete("recall/{maTinNhan}")]
        public async Task<IActionResult> RecallMessage(int maTinNhan, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId không được để trống.");

            try
            {
                var tinNhan = await _context.TinNhans
                    .FirstOrDefaultAsync(t => t.MaTinNhan == maTinNhan);

                if (tinNhan == null)
                    return NotFound("Tin nhắn không tồn tại.");

                // Kiểm tra quyền thu hồi (chỉ người gửi mới được thu hồi)
                if (tinNhan.MaNguoiGui != userId)
                    return Forbid("Bạn không có quyền thu hồi tin nhắn này.");

                // Kiểm tra thời gian (chỉ được thu hồi trong vòng 5 phút)
                var timeDifference = DateTime.UtcNow - tinNhan.ThoiGianGui;
                if (timeDifference.TotalMinutes > 5)
                    return BadRequest("Chỉ có thể thu hồi tin nhắn trong vòng 5 phút sau khi gửi.");

                // Chỉ cho phép thu hồi tin nhắn text
                if (tinNhan.Loai != LoaiTinNhan.Text)
                    return BadRequest("Chỉ có thể thu hồi tin nhắn văn bản.");

                // Lưu thông tin cần thiết trước khi xóa
                var maCuocTroChuyen = tinNhan.MaCuocTroChuyen;

                // Xóa tin nhắn khỏi database
                _context.TinNhans.Remove(tinNhan);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Thu hồi tin nhắn thành công",
                    maTinNhan = maTinNhan,
                    maCuocTroChuyen = maCuocTroChuyen
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server khi thu hồi tin nhắn", error = ex.Message });
            }
        }

        // 🆕 API thu hồi ảnh/video
        [HttpDelete("recall-media/{maTinNhan}")]
        public async Task<IActionResult> RecallMedia(int maTinNhan, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId không được để trống.");

            try
            {
                var tinNhan = await _context.TinNhans
                    .FirstOrDefaultAsync(t => t.MaTinNhan == maTinNhan);

                if (tinNhan == null)
                    return NotFound("Tin nhắn không tồn tại.");

                // Kiểm tra quyền thu hồi (chỉ người gửi mới được thu hồi)
                if (tinNhan.MaNguoiGui != userId)
                    return Forbid("Bạn không có quyền thu hồi tin nhắn này.");

                // Kiểm tra thời gian (chỉ được thu hồi trong vòng 5 phút)
                var timeDifference = DateTime.UtcNow - tinNhan.ThoiGianGui;
                if (timeDifference.TotalMinutes > 5)
                    return BadRequest("Chỉ có thể thu hồi tin nhắn trong vòng 5 phút sau khi gửi.");

                // Chỉ cho phép thu hồi ảnh/video
                if (tinNhan.Loai != LoaiTinNhan.Image && tinNhan.Loai != LoaiTinNhan.Video)
                    return BadRequest("Chỉ có thể thu hồi tin nhắn ảnh hoặc video.");

                // Lưu thông tin cần thiết trước khi xóa
                var maCuocTroChuyen = tinNhan.MaCuocTroChuyen;
                var mediaUrl = tinNhan.NoiDung; // URL của ảnh/video được lưu trong NoiDung

                // Xóa ảnh/video khỏi Cloudinary
                if (!string.IsNullOrEmpty(mediaUrl))
                {
                    var resourceType = tinNhan.Loai == LoaiTinNhan.Image
                        ? CloudinaryDotNet.Actions.ResourceType.Image
                        : CloudinaryDotNet.Actions.ResourceType.Video;

                    // Extract publicId from Cloudinary URL
                    var publicId = ExtractPublicIdFromUrl(mediaUrl);

                    if (!string.IsNullOrEmpty(publicId))
                    {
                        var deleteResult = await _photoService.DeletePhotoAsync(publicId, resourceType);

                        if (deleteResult.Result != "ok")
                        {
                            // Log warning but continue with database deletion
                            Console.WriteLine($"Warning: Could not delete media from Cloudinary. Result: {deleteResult.Result}");
                        }
                    }
                }

                // Xóa tin nhắn khỏi database
                _context.TinNhans.Remove(tinNhan);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Thu hồi ảnh/video thành công",
                    maTinNhan = maTinNhan,
                    maCuocTroChuyen = maCuocTroChuyen
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi thu hồi ảnh/video", error = ex.Message });
            }
        }

        // Helper method để extract publicId từ Cloudinary URL
        private string ExtractPublicIdFromUrl(string cloudinaryUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(cloudinaryUrl))
                    return null;

                // Cloudinary URL format: https://res.cloudinary.com/{cloud_name}/{resource_type}/upload/v{version}/{folder}/{public_id}.{format}
                var uri = new Uri(cloudinaryUrl);
                var path = uri.AbsolutePath;

                // Remove file extension
                var lastDotIndex = path.LastIndexOf('.');
                if (lastDotIndex > 0)
                {
                    path = path.Substring(0, lastDotIndex);
                }

                // Extract public_id (includes folder path)
                var uploadIndex = path.IndexOf("/upload/");
                if (uploadIndex >= 0)
                {
                    var afterUpload = path.Substring(uploadIndex + "/upload/".Length);
                    // Remove version if exists (v1234567890/)
                    var versionPattern = @"^v\d+/";
                    var match = System.Text.RegularExpressions.Regex.Match(afterUpload, versionPattern);
                    if (match.Success)
                    {
                        afterUpload = afterUpload.Substring(match.Length);
                    }
                    return afterUpload;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting publicId from URL {cloudinaryUrl}: {ex.Message}");
                return null;
            }
        }
        [HttpDelete("delete-for-me/{maTinNhan}")]
        public async Task<IActionResult> DeleteMessageForMe(int maTinNhan, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId không được để trống.");

            var tinNhan = await _context.TinNhans.FindAsync(maTinNhan);
            if (tinNhan == null)
                return NotFound("Tin nhắn không tồn tại.");

            // Kiểm tra đã xóa chưa
            var daXoa = await _context.TinNhanDaXoas
                .AnyAsync(x => x.TinNhanId == maTinNhan && x.UserId == userId);
            if (daXoa)
                return Ok(new { message = "Tin nhắn đã được xóa trước đó." });

            // Thêm bản ghi xóa
            var tinNhanDaXoa = new TinNhanDaXoa
            {
                TinNhanId = maTinNhan,
                UserId = userId
            };
            _context.TinNhanDaXoas.Add(tinNhanDaXoa);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa tin nhắn khỏi phía bạn." });
        }
        // Xóa tất cả tin nhắn phía tôi trong một cuộc trò chuyện
        [HttpDelete("delete-conversation-for-me/{maCuocTroChuyen}")]
        public async Task<IActionResult> DeleteConversationForMe(string maCuocTroChuyen, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId không được để trống.");

            // Lấy tất cả tin nhắn thuộc cuộc trò chuyện này mà user chưa xóa
            var tinNhanIds = await _context.TinNhans
                .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen)
                .Select(t => t.MaTinNhan)
                .ToListAsync();

            var daXoaIds = await _context.TinNhanDaXoas
                .Where(x => x.UserId == userId && tinNhanIds.Contains(x.TinNhanId))
                .Select(x => x.TinNhanId)
                .ToListAsync();

            var chuaXoaIds = tinNhanIds.Except(daXoaIds).ToList();

            var tinNhanDaXoaList = chuaXoaIds.Select(id => new TinNhanDaXoa
            {
                TinNhanId = id,
                UserId = userId
            }).ToList();

            if (tinNhanDaXoaList.Count > 0)
            {
                _context.TinNhanDaXoas.AddRange(tinNhanDaXoaList);
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "Đã xóa toàn bộ tin nhắn khỏi phía bạn." });
        }
    }
}