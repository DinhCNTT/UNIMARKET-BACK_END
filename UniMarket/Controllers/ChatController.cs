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
using Microsoft.AspNetCore.Identity;

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
        private readonly UserManager<ApplicationUser> _userManager;

        public ChatController(ApplicationDbContext context, PhotoService photoService, IHubContext<ChatHub> hubContext, UserPresenceService presenceService, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _photoService = photoService;
            _hubContext = hubContext;
            _presenceService = presenceService;
            _userManager = userManager;
        }
        [HttpPost("start")]
        public async Task<IActionResult> StartChat([FromBody] StartChatRequest request)
        {
            if (string.IsNullOrEmpty(request.MaNguoiDung1) || string.IsNullOrEmpty(request.MaNguoiDung2) || request.MaTinDang <= 0)
                return BadRequest(new { message = "Thông tin không đầy đủ." });

            // 🔎 Kiểm tra block
            var blockRecord = await _context.BlockedUsers
                .FirstOrDefaultAsync(b =>
                    (b.BlockerId == request.MaNguoiDung1 && b.BlockedId == request.MaNguoiDung2) ||
                    (b.BlockerId == request.MaNguoiDung2 && b.BlockedId == request.MaNguoiDung1));

            if (blockRecord != null)
            {
                // 🔎 Lấy thông tin tên từ AspNetUsers (ApplicationUser)
                var blocker = await _userManager.FindByIdAsync(blockRecord.BlockerId);
                var blocked = await _userManager.FindByIdAsync(blockRecord.BlockedId);

                var blockerName = blocker?.FullName ?? blocker?.UserName ?? "Người dùng";
                var blockedName = blocked?.FullName ?? blocked?.UserName ?? "Người dùng";

                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = $"{blockerName} đã chặn {blockedName}.",
                    blockerId = blockRecord.BlockerId,
                    blockedId = blockRecord.BlockedId,
                    blockerName,
                    blockedName
                });
            }

            // 🔎 Hàm tạo Id cuộc trò chuyện
            string GenerateChatId(string u1, string u2, int maTinDang)
            {
                var arr = new[] { u1, u2 };
                Array.Sort(arr);
                return $"{arr[0]}-{arr[1]}-{maTinDang}";
            }

            var maCuocTroChuyen = GenerateChatId(request.MaNguoiDung1, request.MaNguoiDung2, request.MaTinDang);

            // 🔎 Kiểm tra đã tồn tại chưa
            var existingChat = await _context.CuocTroChuyens
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

            if (existingChat != null)
            {
                var userChatState = await _context.UserChatStates
                    .FirstOrDefaultAsync(ucs => ucs.UserId == request.MaNguoiDung1 && ucs.ChatId == maCuocTroChuyen);

                if (userChatState?.IsDeleted == true)
                {
                    userChatState.IsDeleted = false;
                    userChatState.IsHidden = false;
                    userChatState.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                return Ok(new { MaCuocTroChuyen = existingChat.MaCuocTroChuyen });
            }

            // 🔎 Kiểm tra tin đăng
            var tinDang = await _context.TinDangs.Include(t => t.AnhTinDangs)
                .FirstOrDefaultAsync(t => t.MaTinDang == request.MaTinDang);

            if (tinDang == null)
                return NotFound(new { message = "Tin đăng không tồn tại." });

            // 🔎 Tạo mới cuộc trò chuyện
            var newChat = new CuocTroChuyen
            {
                MaCuocTroChuyen = maCuocTroChuyen,
                ThoiGianTao = DateTime.UtcNow,
                IsEmpty = true,
                MaTinDang = tinDang.MaTinDang,
                TieuDeTinDang = tinDang.TieuDe,
                AnhDaiDienTinDang = tinDang.AnhTinDangs?.FirstOrDefault()?.DuongDan ?? "",
                GiaTinDang = tinDang.Gia,
                MaNguoiBan = tinDang.MaNguoiBan,
                IsPostDeleted = false
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
                        .Where(t => !_context.TinNhanXoas.Any(x => x.MaTinNhan == t.MaTinNhan && x.UserId == userId))
                        .OrderByDescending(t => t.ThoiGianGui)
                        .Select(t => new
                        {
                            NoiDung = t.IsRecalled ? "Tin nhắn đã được thu hồi" : t.NoiDung,
                            MaNguoiGui = t.MaNguoiGui,
                            LoaiTinNhan = t.IsRecalled ? "text" : t.Loai.ToString().ToLower(),
                            ThoiGianGui = t.ThoiGianGui,
                            IsRecalled = t.IsRecalled,  // ✅ THÊM FIELD
                            TenNguoiGui = t.NguoiGui.FullName  // ✅ THÊM TÊN NGƯỜI GỬI
                        })
                        .FirstOrDefault(),
                    ThoiGianCapNhat = _context.TinNhans
                        .Where(t => t.MaCuocTroChuyen == c.MaCuocTroChuyen)
                        .Where(t => !_context.TinNhanXoas.Any(x => x.MaTinNhan == t.MaTinNhan && x.UserId == userId))
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
                    IsSeller = c.MaNguoiBan == userId,
                    HasUnreadMessages = _context.TinNhans
                        .Any(t => t.MaCuocTroChuyen == c.MaCuocTroChuyen && t.MaNguoiGui != userId && !t.DaXem &&
                             !_context.TinNhanXoas.Any(x => x.MaTinNhan == t.MaTinNhan && x.UserId == userId)),
                    UserChatState = _context.UserChatStates
                        .Where(ucs => ucs.UserId == userId && ucs.ChatId == c.MaCuocTroChuyen)
                        .Select(ucs => new { ucs.IsHidden, ucs.IsDeleted })
                        .FirstOrDefault(),
                    c.IsPostDeleted,
                    c.IsBlocked,
                    c.MaNguoiChan
                })
                .Where(c => !c.IsSeller || (c.IsSeller && !c.IsEmpty))
                .ToListAsync();
            // Load any hidden conversation records for this user so we can apply the
            // ThoiGianAn cutoff to previews (so old messages don't reappear in the list).
            var hiddenList = await _context.UserHiddenConversations
                .Where(h => h.UserId == userId)
                .ToListAsync();

            var result = userChats.Select(c =>
            {
                var hidden = hiddenList.FirstOrDefault(h => h.MaCuocTroChuyen == c.MaCuocTroChuyen);
                var hasHidden = hidden != null;

                // If hidden exists, always apply the ThoiGianAn cutoff to previews so
                // messages older than the hide time are not shown to the user. This
                // prevents old messages from reappearing after a new message arrives.
                var thoiGianCapNhat = c.ThoiGianCapNhat;
                var tinNhanCuoi = c.TinNhanCuoi;

                if (hasHidden)
                {
                    var cutoff = hidden.ThoiGianAn;
                    if (c.ThoiGianCapNhat < cutoff)
                    {
                        tinNhanCuoi = null;
                        thoiGianCapNhat = c.ThoiGianTao;
                    }
                }

                return new
                {
                    c.MaCuocTroChuyen,
                    c.ThoiGianTao,
                    ThoiGianCapNhat = thoiGianCapNhat,
                    c.IsEmpty,
                    c.MaTinDang,
                    TinNhanCuoi = tinNhanCuoi,
                    c.MaNguoiConLai,
                    c.TenNguoiConLai,
                    c.TieuDeTinDang,
                    c.AnhDaiDienTinDang,
                    c.GiaTinDang,
                    c.IsSeller,
                    c.HasUnreadMessages,
                    IsHidden = c.UserChatState?.IsHidden ?? false,
                    IsDeleted = c.UserChatState?.IsDeleted ?? false,
                    c.IsPostDeleted,
                    c.IsBlocked,
                    c.MaNguoiChan
                };
            }).ToList();

            return Ok(result);
        }

        [HttpGet("history/{maCuocTroChuyen}")]
        public async Task<IActionResult> GetChatHistory(string maCuocTroChuyen,
                                              [FromQuery] string userId,
                                              [FromQuery] int page = 1,
                                              [FromQuery] int pageSize = 30) // Giữ phân trang
        {
            try
            {
                // 1. LẤY LOGIC "ẨN" TỪ CODE BẠN BẠN
                // Đảm bảo bạn có bảng 'UserHiddenConversations' trong DbContext
                var hidden = await _context.UserHiddenConversations
                    .FirstOrDefaultAsync(h => h.UserId == userId && h.MaCuocTroChuyen == maCuocTroChuyen);
                var cutoff = hidden?.ThoiGianAn; // Lấy thời gian ẩn (mốc xoá)

                // 2. QUERY GỐC (Dùng bảng xoá của bạn bạn nếu muốn, ví dụ TinNhanXoas)
                var messagesQuery = _context.TinNhans
                    .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen)
                    // Dùng logic "Xoá" (ví dụ: TinNhanXoas)
                    .Where(t => !_context.TinNhanXoas.Any(x => x.MaTinNhan == t.MaTinNhan && x.UserId == userId));

                // 3. THÊM LOGIC LỌC "ẨN" CỦA BẠN BẠN VÀO
                if (cutoff != null)
                {
                    // Chỉ lấy các tin nhắn được gửi sau (hoặc bằng) thời điểm ẩn
                    messagesQuery = messagesQuery.Where(t => t.ThoiGianGui >= cutoff);
                }

                // 4. GIỮ NGUYÊN LOGIC PHÂN TRANG CỦA BẠN
                var paginatedMessages = await messagesQuery
                    .OrderByDescending(t => t.ThoiGianGui) // Sắp xếp mới -> cũ
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(t => new
                    {
                        t.MaTinNhan,
                        t.MaCuocTroChuyen,
                        t.MaNguoiGui,
                        NoiDung = (t.Loai == LoaiTinNhan.Text) ? t.NoiDung : t.MediaUrl,
                        LoaiTinNhan = t.Loai.ToString().ToLower(),
                        ThoiGianGui = t.ThoiGianGui.ToString("O"),
                        t.DaXem,
                        t.ThoiGianXem,
                        t.IsRecalled
                    })
                    .ToListAsync();

                return Ok(paginatedMessages);
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

            var result = new
            {
                MaTinDang = cuocTroChuyen.MaTinDang,
                TieuDeTinDang = cuocTroChuyen.TieuDeTinDang,
                GiaTinDang = cuocTroChuyen.GiaTinDang,
                AnhDaiDienTinDang = cuocTroChuyen.AnhDaiDienTinDang,
                IsPostDeleted = cuocTroChuyen.IsPostDeleted,  // ✅ Flag
                IsBlocked = cuocTroChuyen.IsBlocked,  // Giữ nguyên
                MaNguoiChan = cuocTroChuyen.MaNguoiChan  // Giữ nguyên
            };

            TinDang? tinDang = null;
            string? chuSanPhamId = cuocTroChuyen.MaNguoiBan;  // ✅ Ưu tiên MaNguoiBan
            try
            {
                tinDang = await _context.TinDangs
                    .Include(td => td.NguoiBan)
                    .FirstOrDefaultAsync(td => td.MaTinDang == cuocTroChuyen.MaTinDang);

                if (tinDang != null)
                {
                    if (cuocTroChuyen.TieuDeTinDang != tinDang.TieuDe ||
                        cuocTroChuyen.GiaTinDang != tinDang.Gia ||
                        cuocTroChuyen.IsPostDeleted)
                    {
                        cuocTroChuyen.TieuDeTinDang = tinDang.TieuDe;
                        cuocTroChuyen.GiaTinDang = tinDang.Gia;
                        cuocTroChuyen.AnhDaiDienTinDang = tinDang.AnhTinDangs?.FirstOrDefault()?.DuongDan ?? "";
                        cuocTroChuyen.IsPostDeleted = false;
                        await _context.SaveChangesAsync();
                    }
                    chuSanPhamId = tinDang.MaNguoiBan;
                }
                else if (!cuocTroChuyen.IsPostDeleted)
                {
                    cuocTroChuyen.IsPostDeleted = true;
                    cuocTroChuyen.TieuDeTinDang += " (đã xóa)";  // Optional
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching post: {ex.Message}");
                if (!cuocTroChuyen.IsPostDeleted)
                {
                    cuocTroChuyen.IsPostDeleted = true;
                    await _context.SaveChangesAsync();
                }
            }

            var chuSanPham = await _context.Users.FindAsync(chuSanPhamId);
            var nguoiConLai = cuocTroChuyen.NguoiThamGias
                .Select(ntg => ntg.NguoiDung)
                .FirstOrDefault(u => u.Id != chuSanPhamId);

            var chuSanPhamStatus = GetUserPresenceStatus(chuSanPham?.Id);
            var nguoiConLaiStatus = GetUserPresenceStatus(nguoiConLai?.Id);

            return Ok(new
            {
                result.MaTinDang,
                result.TieuDeTinDang,
                result.GiaTinDang,
                result.AnhDaiDienTinDang,
                result.IsPostDeleted,  // ✅ Flag
                result.IsBlocked,  // Giữ nguyên
                result.MaNguoiChan,  // Giữ nguyên
                maChuSanPham = chuSanPham?.Id,
                tenChuSanPham = chuSanPham?.FullName ?? "Chủ sản phẩm (tin đã xóa)",
                avatarChuSanPham = chuSanPham?.AvatarUrl ?? "",
                daXacMinhEmailChuSanPham = chuSanPham?.EmailConfirmed ?? false,
                trangThaiChuSanPham = chuSanPhamStatus,
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

            // Check nếu đã chặn
            var existing = await _context.BlockedUsers
                .FirstOrDefaultAsync(b => b.BlockerId == request.BlockerId && b.BlockedId == request.BlockedId);

            if (existing != null)
                return Ok("Người dùng đã bị chặn.");

            // Lưu vào bảng BlockedUsers
            var blockedUser = new BlockedUser
            {
                BlockerId = request.BlockerId,
                BlockedId = request.BlockedId
            };

            _context.BlockedUsers.Add(blockedUser);

            // 👉 Lấy tất cả các cuộc trò chuyện có cả 2 user
            var cuocTroChuyens = await _context.CuocTroChuyens
                .Where(c =>
                    c.NguoiThamGias.Any(ntg => ntg.MaNguoiDung == request.BlockerId) &&
                    c.NguoiThamGias.Any(ntg => ntg.MaNguoiDung == request.BlockedId)
                )
                .ToListAsync();

            foreach (var ctc in cuocTroChuyens)
            {
                ctc.IsBlocked = true;
                ctc.MaNguoiChan = request.BlockerId;
                _context.CuocTroChuyens.Update(ctc);
            }

            await _context.SaveChangesAsync();

            // ✅ THÊM: Broadcast realtime qua SignalR
            await _hubContext.Clients.Group($"user-{request.BlockerId}").SendAsync("UserBlocked", new
            {
                blockedUserId = request.BlockedId,
                isBlocked = true,
                actionType = "block"
            });

            await _hubContext.Clients.Group($"user-{request.BlockedId}").SendAsync("UserBlocked", new
            {
                blockedUserId = request.BlockerId,
                isBlocked = true,
                actionType = "blocked_by"
            });

            // Broadcast cập nhật trạng thái chat cho cả 2 user
            foreach (var ctc in cuocTroChuyens)
            {
                await _hubContext.Clients.Group($"user-{request.BlockerId}").SendAsync("ChatStatusChanged", new
                {
                    chatId = ctc.MaCuocTroChuyen,
                    isBlocked = true,
                    blockedByMe = true
                });

                await _hubContext.Clients.Group($"user-{request.BlockedId}").SendAsync("ChatStatusChanged", new
                {
                    chatId = ctc.MaCuocTroChuyen,
                    isBlocked = true,
                    blockedByMe = false
                });
            }

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

            // Xóa khỏi bảng BlockedUsers
            _context.BlockedUsers.Remove(blockedUser);

            // 👉 Lấy tất cả các cuộc trò chuyện có cả 2 user
            var cuocTroChuyens = await _context.CuocTroChuyens
                .Where(c =>
                    c.NguoiThamGias.Any(ntg => ntg.MaNguoiDung == request.BlockerId) &&
                    c.NguoiThamGias.Any(ntg => ntg.MaNguoiDung == request.BlockedId)
                )
                .ToListAsync();

            foreach (var ctc in cuocTroChuyens)
            {
                // Chỉ gỡ nếu đúng người đã chặn trước đó
                if (ctc.IsBlocked && ctc.MaNguoiChan == request.BlockerId)
                {
                    ctc.IsBlocked = false;
                    ctc.MaNguoiChan = null;
                    _context.CuocTroChuyens.Update(ctc);
                }
            }

            await _context.SaveChangesAsync();

            // ✅ THÊM: Broadcast realtime qua SignalR
            await _hubContext.Clients.Group($"user-{request.BlockerId}").SendAsync("UserBlocked", new
            {
                blockedUserId = request.BlockedId,
                isBlocked = false,
                actionType = "unblock"
            });

            await _hubContext.Clients.Group($"user-{request.BlockedId}").SendAsync("UserBlocked", new
            {
                blockedUserId = request.BlockerId,
                isBlocked = false,
                actionType = "unblocked_by"
            });

            // Broadcast cập nhật trạng thái chat cho cả 2 user
            foreach (var ctc in cuocTroChuyens)
            {
                if (ctc.IsBlocked == false) // Chỉ broadcast nếu thực sự đã gỡ chặn
                {
                    await _hubContext.Clients.Group($"user-{request.BlockerId}").SendAsync("ChatStatusChanged", new
                    {
                        chatId = ctc.MaCuocTroChuyen,
                        isBlocked = false,
                        blockedByMe = false
                    });

                    await _hubContext.Clients.Group($"user-{request.BlockedId}").SendAsync("ChatStatusChanged", new
                    {
                        chatId = ctc.MaCuocTroChuyen,
                        isBlocked = false,
                        blockedByMe = false
                    });
                }
            }

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

            var daXoa = await _context.TinNhanXoas
                .AnyAsync(x => x.MaTinNhan == maTinNhan && x.UserId == userId);
            if (daXoa)
                return Ok(new { message = "Tin nhắn đã được xóa trước đó." });

            var tinNhanXoa = new TinNhanXoa
            {
                MaTinNhan = maTinNhan,
                UserId = userId,
                ThoiGianXoa = DateTime.UtcNow
            };
            _context.TinNhanXoas.Add(tinNhanXoa);
            await _context.SaveChangesAsync();

            // 📡 Emit realtime event to conversation group
            try
            {
                var maCuocTroChuyen = tinNhan.MaCuocTroChuyen;
                await _hubContext.Clients.Group(maCuocTroChuyen).SendAsync("TinNhanDaXoa", new
                {
                    maTinNhan = maTinNhan,
                    userId = userId,
                    thoiGianXoa = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending SignalR event: {ex.Message}");
            }

            return Ok(new { message = "Đã xóa tin nhắn khỏi phía bạn." });
        }

        [HttpDelete("delete-conversation-for-me/{maCuocTroChuyen}")]
        public async Task<IActionResult> DeleteConversationForMe(string maCuocTroChuyen, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId không được để trống.");
            try
            {
                // Ghi trạng thái xóa cuộc trò chuyện vào bảng UserHiddenConversations
                // để phân biệt rõ: Tin nhắn xóa từng cái -> TinNhanXoas;
                // xóa toàn bộ cuộc trò chuyện -> UserHiddenConversations (IsDeleted = true)

                var existing = await _context.UserHiddenConversations
                    .FirstOrDefaultAsync(u => u.UserId == userId && u.MaCuocTroChuyen == maCuocTroChuyen);

                if (existing != null)
                {
                    existing.IsDeleted = true;
                    existing.ThoiGianAn = DateTime.UtcNow;
                    existing.HasReappeared = false;
                    _context.UserHiddenConversations.Update(existing);
                }
                else
                {
                    var hidden = new UserHiddenConversation
                    {
                        UserId = userId,
                        MaCuocTroChuyen = maCuocTroChuyen,
                        ThoiGianAn = DateTime.UtcNow,
                        HasReappeared = false,
                        IsDeleted = true
                    };
                    _context.UserHiddenConversations.Add(hidden);
                }

                // Nếu có bất kỳ state cũ trong UserChatStates, xóa/đặt lại để tránh mâu thuẫn
                var oldStates = _context.UserChatStates.Where(ucs => ucs.UserId == userId && ucs.ChatId == maCuocTroChuyen);
                _context.UserChatStates.RemoveRange(oldStates);

                await _context.SaveChangesAsync();

                // Return the saved/updated hidden record's timestamp so clients can use server time
                var savedHidden = await _context.UserHiddenConversations
                    .Where(u => u.UserId == userId && u.MaCuocTroChuyen == maCuocTroChuyen)
                    .Select(u => new
                    {
                        u.UserId,
                        u.MaCuocTroChuyen,
                        ThoiGianAn = u.ThoiGianAn
                    })
                    .FirstOrDefaultAsync();

                return Ok(new { message = "Đã xóa cuộc trò chuyện khỏi phía bạn.", hidden = savedHidden });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi khi xóa cuộc trò chuyện", error = ex.Message });
            }
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