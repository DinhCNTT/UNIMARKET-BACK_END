using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;
using UniMarket.Services;
using UniMarket.Hubs;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PhotoService _photoService;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly UserPresenceService _presenceService; // Thêm dòng này

        public ChatController(ApplicationDbContext context, PhotoService photoService, IHubContext<ChatHub> hubContext, UserPresenceService presenceService)
        {
            _context = context;
            _photoService = photoService;
            _hubContext = hubContext;
            _presenceService = presenceService;
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartChat([FromBody] StartChatRequest request)
        {
            if (string.IsNullOrEmpty(request.MaNguoiDung1) || string.IsNullOrEmpty(request.MaNguoiDung2) || request.MaTinDang <= 0)
                return BadRequest("Thông tin không đầy đủ.");

            var isBlocked = await _context.BlockedUsers
                .AnyAsync(b => (b.BlockerId == request.MaNguoiDung1 && b.BlockedId == request.MaNguoiDung2) ||
                               (b.BlockerId == request.MaNguoiDung2 && b.BlockedId == request.MaNguoiDung1));
            if (isBlocked)
                return Forbid("Không thể bắt đầu cuộc trò chuyện vì một trong hai người đã chặn người kia.");

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
                // 🔧 KIỂM TRA TRẠNG THÁI UserChatState của người dùng hiện tại
                var userChatState = await _context.UserChatStates
                    .FirstOrDefaultAsync(ucs => ucs.UserId == request.MaNguoiDung1 &&
                                              ucs.ChatId == maCuocTroChuyen);

                // Nếu người dùng đã xóa cuộc trò chuyện này, reset trạng thái
                if (userChatState?.IsDeleted == true)
                {
                    userChatState.IsDeleted = false;
                    userChatState.IsHidden = false;
                    userChatState.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

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

        // Cập nhật API GetUserConversations để tích hợp UserChatState
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
                .Where(t => !_context.TinNhanDaXoas.Any(x => x.TinNhanId == t.MaTinNhan && x.UserId == userId))
                .OrderByDescending(t => t.ThoiGianGui)
                .Select(t => new
                {
                    NoiDung = t.NoiDung,
                    MaNguoiGui = t.MaNguoiGui,
                    LoaiTinNhan = t.Loai.ToString().ToLower(),
                    ThoiGianGui = t.ThoiGianGui  // ✅ THÊM: Thời gian tin nhắn cuối
                })
                .FirstOrDefault(),
            // ✅ THÊM: Thời gian cập nhật thực tế dựa trên tin nhắn cuối
            ThoiGianCapNhat = _context.TinNhans
                .Where(t => t.MaCuocTroChuyen == c.MaCuocTroChuyen)
                .Where(t => !_context.TinNhanDaXoas.Any(x => x.TinNhanId == t.MaTinNhan && x.UserId == userId))
                .Max(t => (DateTime?)t.ThoiGianGui) ?? c.ThoiGianTao,
            MaNguoiConLai = c.NguoiThamGias
                .Where(n => n.MaNguoiDung != userId)
                .Select(n => n.MaNguoiDung)
                .FirstOrDefault(),
            TenNguoiConLai = c.NguoiThamGias
                .Where(n => n.MaNguoiDung != userId)
                .Select(n => n.NguoiDung.FullName)
                .FirstOrDefault(),
            c.TieuDeTinDang,
            c.AnhDaiDienTinDang,
            c.GiaTinDang,
            IsSeller = _context.TinDangs.Any(t => t.MaTinDang == c.MaTinDang && t.MaNguoiBan == userId),
            HasUnreadMessages = _context.TinNhans
                .Any(t => t.MaCuocTroChuyen == c.MaCuocTroChuyen && t.MaNguoiGui != userId && !t.DaXem &&
                     !_context.TinNhanDaXoas.Any(x => x.TinNhanId == t.MaTinNhan && x.UserId == userId)),
            UserChatState = _context.UserChatStates
                .Where(ucs => ucs.UserId == userId && ucs.ChatId == c.MaCuocTroChuyen)
                .Select(ucs => new { ucs.IsHidden, ucs.IsDeleted })
                .FirstOrDefault()
        })
        .Where(c => !c.IsSeller || (c.IsSeller && !c.IsEmpty))
        .ToListAsync();

    var result = userChats.Select(c => new
    {
        c.MaCuocTroChuyen,
        c.ThoiGianTao,
        c.ThoiGianCapNhat,  // ✅ THÊM: Trả về thời gian cập nhật thực
        c.IsEmpty,
        c.MaTinDang,
        c.TinNhanCuoi,
        c.MaNguoiConLai,
        c.TenNguoiConLai,
        c.TieuDeTinDang,
        c.AnhDaiDienTinDang,
        c.GiaTinDang,
        c.IsSeller,
        c.HasUnreadMessages,
        IsHidden = c.UserChatState?.IsHidden ?? false,
        IsDeleted = c.UserChatState?.IsDeleted ?? false
    }).ToList();

    return Ok(result);
}

        [HttpGet("history/{maCuocTroChuyen}")]
        public async Task<IActionResult> GetChatHistory(string maCuocTroChuyen, [FromQuery] string userId)
        {
            try
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
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi: {ex.Message}");
                return StatusCode(500, "Internal Server Error");
            }
        }

        [HttpGet("info/{maCuocTroChuyen}")]
        public async Task<IActionResult> GetChatInfo(string maCuocTroChuyen)
        {
            var cuocTroChuyen = await _context.CuocTroChuyens
                .Include(c => c.NguoiThamGias)
                    .ThenInclude(ntg => ntg.NguoiDung)
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

            if (cuocTroChuyen == null) return NotFound();

            // Lấy các trường cơ bản
            var result = new
            {
                cuocTroChuyen.MaTinDang,
                cuocTroChuyen.TieuDeTinDang,
                cuocTroChuyen.GiaTinDang,
                cuocTroChuyen.AnhDaiDienTinDang
            };

            // Lấy tin đăng + chủ sản phẩm
            var tinDang = await _context.TinDangs
                .Include(td => td.NguoiBan)
                .FirstOrDefaultAsync(td => td.MaTinDang == cuocTroChuyen.MaTinDang);

            var chuSanPham = tinDang?.NguoiBan;

            // Lấy người còn lại
            var nguoiConLai = cuocTroChuyen.NguoiThamGias
                .Select(ntg => ntg.NguoiDung)
                .FirstOrDefault(u => u.Id != chuSanPham?.Id);

            // Get real-time presence status
            var chuSanPhamStatus = GetUserPresenceStatus(chuSanPham?.Id);
            var nguoiConLaiStatus = GetUserPresenceStatus(nguoiConLai?.Id);

            return Ok(new
            {
                // Thông tin cơ bản
                result.MaTinDang,
                result.TieuDeTinDang,
                result.GiaTinDang,
                result.AnhDaiDienTinDang,

                // Chủ sản phẩm
                maChuSanPham = chuSanPham?.Id,
                tenChuSanPham = chuSanPham?.FullName,
                avatarChuSanPham = chuSanPham?.AvatarUrl,
                daXacMinhEmailChuSanPham = chuSanPham?.EmailConfirmed ?? false,
                trangThaiChuSanPham = chuSanPhamStatus,

                // Người còn lại trong cuộc trò chuyện
                maNguoiConLai = nguoiConLai?.Id,
                tenNguoiConLai = nguoiConLai?.FullName,
                avatarNguoiConLai = nguoiConLai?.AvatarUrl,
                daXacMinhEmailNguoiConLai = nguoiConLai?.EmailConfirmed ?? false,
                trangThaiNguoiConLai = nguoiConLaiStatus
            });
        }

        private object GetUserPresenceStatus(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;

            var memoryStatus = _presenceService?.GetStatus(userId);
            var user = _context.Users.Find(userId);

            bool isOnline;
            DateTime? lastActive;

            if (memoryStatus.HasValue)
            {
                isOnline = memoryStatus.Value.IsOnline;
                lastActive = memoryStatus.Value.IsOnline ? null : memoryStatus.Value.LastActive;
            }
            else if (user != null)
            {
                isOnline = user.IsOnline;
                lastActive = user.IsOnline ? null : user.LastOnlineTime;
            }
            else
            {
                return null;
            }

            return new
            {
                isOnline = isOnline,
                lastActive = lastActive,
                formattedLastSeen = FormatLastSeen(lastActive)
            };
        }

        private string FormatLastSeen(DateTime? lastActive)
        {
            if (!lastActive.HasValue) return null;

            var timeAgo = DateTime.UtcNow - lastActive.Value;

            if (timeAgo.TotalMinutes < 1)
                return "vừa mới";
            else if (timeAgo.TotalMinutes < 60)
                return $"{(int)timeAgo.TotalMinutes} phút trước";
            else if (timeAgo.TotalHours < 24)
                return $"{(int)timeAgo.TotalHours} giờ trước";
            else
                return $"{(int)timeAgo.TotalDays} ngày trước";
        }

        [HttpGet("unread-count/{userId}")]
        public async Task<IActionResult> GetUnreadCount(string userId, [FromQuery] List<string> hiddenChatIds)
        {
            var count = await _context.TinNhans
                .Where(t => t.MaNguoiGui != userId &&
                            !t.DaXem &&
                            !hiddenChatIds.Contains(t.MaCuocTroChuyen) &&
                            _context.NguoiThamGias.Any(n => n.MaCuocTroChuyen == t.MaCuocTroChuyen && n.MaNguoiDung == userId))
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
                return BadRequest("Chỉ hỗ trợ file ảnh (jpg, jpeg, png) và video (mp4, avi).");

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

                if (tinNhan.MaNguoiGui != userId)
                    return Forbid("Bạn không có quyền thu hồi tin nhắn này.");

                var timeDifference = DateTime.UtcNow - tinNhan.ThoiGianGui;
                if (timeDifference.TotalMinutes > 5)
                    return BadRequest("Chỉ có thể thu hồi tin nhắn trong vòng 5 phút sau khi gửi.");

                if (tinNhan.Loai != LoaiTinNhan.Text)
                    return BadRequest("Chỉ có thể thu hồi tin nhắn văn bản.");

                var maCuocTroChuyen = tinNhan.MaCuocTroChuyen;
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

                if (tinNhan.MaNguoiGui != userId)
                    return Forbid("Bạn không có quyền thu hồi tin nhắn này.");

                var timeDifference = DateTime.UtcNow - tinNhan.ThoiGianGui;
                if (timeDifference.TotalMinutes > 5)
                    return BadRequest("Chỉ có thể thu hồi tin nhắn trong vòng 5 phút sau khi gửi.");

                if (tinNhan.Loai != LoaiTinNhan.Image && tinNhan.Loai != LoaiTinNhan.Video)
                    return BadRequest("Chỉ có thể thu hồi tin nhắn ảnh hoặc video.");

                var maCuocTroChuyen = tinNhan.MaCuocTroChuyen;
                var mediaUrl = tinNhan.NoiDung;

                if (!string.IsNullOrEmpty(mediaUrl))
                {
                    var resourceType = tinNhan.Loai == LoaiTinNhan.Image
                        ? CloudinaryDotNet.Actions.ResourceType.Image
                        : CloudinaryDotNet.Actions.ResourceType.Video;

                    var publicId = ExtractPublicIdFromUrl(mediaUrl);
                    if (!string.IsNullOrEmpty(publicId))
                    {
                        var deleteResult = await _photoService.DeletePhotoAsync(publicId, resourceType);
                        if (deleteResult.Result != "ok")
                            Console.WriteLine($"Warning: Could  Could not delete media from Cloudinary. Result: {deleteResult.Result}");
                    }
                }

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

        private string ExtractPublicIdFromUrl(string cloudinaryUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(cloudinaryUrl))
                    return null;

                var uri = new Uri(cloudinaryUrl);
                var path = uri.AbsolutePath;

                var lastDotIndex = path.LastIndexOf('.');
                if (lastDotIndex > 0)
                    path = path.Substring(0, lastDotIndex);

                var uploadIndex = path.IndexOf("/upload/");
                if (uploadIndex >= 0)
                {
                    var afterUpload = path.Substring(uploadIndex + "/upload/".Length);
                    var versionPattern = @"^v\d+/";
                    var match = System.Text.RegularExpressions.Regex.Match(afterUpload, versionPattern);
                    if (match.Success)
                        afterUpload = afterUpload.Substring(match.Length);
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

        [HttpPost("block-user")]
        public async Task<IActionResult> BlockUser([FromBody] BlockUserRequest request)
        {
            if (string.IsNullOrEmpty(request.BlockerId) || string.IsNullOrEmpty(request.BlockedId))
                return BadRequest("Thông tin không đầy đủ.");

            var existing = await _context.BlockedUsers
                .FirstOrDefaultAsync(b => b.BlockerId == request.BlockerId && b.BlockedId == request.BlockedId);

            if (existing != null)
                return Ok("Người dùng đã bị chặn.");

            var blockedUser = new BlockedUser
            {
                BlockerId = request.BlockerId,
                BlockedId = request.BlockedId
            };

            _context.BlockedUsers.Add(blockedUser);
            await _context.SaveChangesAsync();

            return Ok("Đã chặn người dùng.");
        }

        [HttpPost("unblock-user")]
        public async Task<IActionResult> UnblockUser([FromBody] UnblockUserRequest request)
        {
            if (string.IsNullOrEmpty(request.BlockerId) || string.IsNullOrEmpty(request.BlockedId))
                return BadRequest("Thông tin không đầy đủ.");

            var blockedUser = await _context.BlockedUsers
                .FirstOrDefaultAsync(b => b.BlockerId == request.BlockerId && b.BlockedId == request.BlockedId);

            if (blockedUser == null)
                return NotFound("Không tìm thấy quan hệ chặn.");

            _context.BlockedUsers.Remove(blockedUser);
            await _context.SaveChangesAsync();

            return Ok("Đã gỡ chặn người dùng.");
        }

        [HttpGet("check-block/{blockerId}/{blockedId}")]
        public async Task<IActionResult> CheckBlock(string blockerId, string blockedId)
        {
            var isBlocked = await _context.BlockedUsers
                .AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId);

            return Ok(new { IsBlocked = isBlocked });
        }

        [HttpDelete("delete-for-me/{maTinNhan}")]
        public async Task<IActionResult> DeleteMessageForMe(int maTinNhan, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId không được để trống.");

            var tinNhan = await _context.TinNhans.FindAsync(maTinNhan);
            if (tinNhan == null)
                return NotFound("Tin nhắn không tồn tại.");

            var daXoa = await _context.TinNhanDaXoas
                .AnyAsync(x => x.TinNhanId == maTinNhan && x.UserId == userId);
            if (daXoa)
                return Ok(new { message = "Tin nhắn đã được xóa trước đó." });

            var tinNhanDaXoa = new TinNhanDaXoa
            {
                TinNhanId = maTinNhan,
                UserId = userId
            };
            _context.TinNhanDaXoas.Add(tinNhanDaXoa);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa tin nhắn khỏi phía bạn." });
        }

        [HttpDelete("delete-conversation-for-me/{maCuocTroChuyen}")]
        public async Task<IActionResult> DeleteConversationForMe(string maCuocTroChuyen, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId không được để trống.");

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
        // Thêm các API endpoints này vào ChatController.cs

        [HttpGet("user-chat-states/{userId}")]
        public async Task<IActionResult> GetUserChatStates(string userId)
        {
            try
            {
                var chatStates = await _context.UserChatStates
                    .Where(ucs => ucs.UserId == userId)
                    .Select(ucs => new UserChatStateResponse
                    {
                        ChatId = ucs.ChatId,
                        IsHidden = ucs.IsHidden,
                        IsDeleted = ucs.IsDeleted,
                        UpdatedAt = ucs.UpdatedAt
                    })
                    .ToListAsync();

                return Ok(chatStates);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi lấy trạng thái chat", error = ex.Message });
            }
        }

        [HttpPost("set-chat-state")]
        public async Task<IActionResult> SetChatState([FromBody] SetChatStateRequest request)
        {
            if (string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.ChatId))
                return BadRequest("UserId và ChatId không được để trống.");

            try
            {
                var existingState = await _context.UserChatStates
                    .FirstOrDefaultAsync(ucs => ucs.UserId == request.UserId && ucs.ChatId == request.ChatId);

                if (existingState != null)
                {
                    existingState.IsHidden = request.IsHidden;
                    existingState.IsDeleted = request.IsDeleted;
                    existingState.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    var newState = new UserChatState
                    {
                        UserId = request.UserId,
                        ChatId = request.ChatId,
                        IsHidden = request.IsHidden,
                        IsDeleted = request.IsDeleted,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.UserChatStates.Add(newState);
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = "Cập nhật trạng thái chat thành công" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi cập nhật trạng thái chat", error = ex.Message });
            }
        }

        [HttpPost("bulk-set-chat-state")]
        public async Task<IActionResult> BulkSetChatState([FromBody] BulkSetChatStateRequest request)
        {
            if (string.IsNullOrEmpty(request.UserId) || request.ChatIds == null || !request.ChatIds.Any())
                return BadRequest("UserId và ChatIds không được để trống.");

            try
            {
                var existingStates = await _context.UserChatStates
                    .Where(ucs => ucs.UserId == request.UserId && request.ChatIds.Contains(ucs.ChatId))
                    .ToListAsync();

                foreach (var chatId in request.ChatIds)
                {
                    var existingState = existingStates.FirstOrDefault(es => es.ChatId == chatId);

                    if (existingState != null)
                    {
                        existingState.IsHidden = request.IsHidden;
                        existingState.IsDeleted = request.IsDeleted;
                        existingState.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        var newState = new UserChatState
                        {
                            UserId = request.UserId,
                            ChatId = chatId,
                            IsHidden = request.IsHidden,
                            IsDeleted = request.IsDeleted,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _context.UserChatStates.Add(newState);
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = $"Cập nhật trạng thái cho {request.ChatIds.Count} cuộc trò chuyện thành công" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi cập nhật trạng thái chat", error = ex.Message });
            }
        }

    }
}