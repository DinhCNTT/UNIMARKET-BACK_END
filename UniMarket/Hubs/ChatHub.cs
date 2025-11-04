using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;
using UniMarket.Services;

namespace UniMarket.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ChatHub> _logger;
        private readonly PhotoService _photoService;
        private readonly UserPresenceService _presenceService;
        public ChatHub(ApplicationDbContext context, ILogger<ChatHub> logger, PhotoService photoService, UserPresenceService presenceService)
        {
            _context = context;
            _logger = logger;
            _photoService = photoService;
            _presenceService = presenceService;
            _logger.LogInformation("ChatHub initialized with ThuHoiAnhVideo method available.");
        }

        // Thêm vào ChatHub.cs - trong method OnConnectedAsync

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                // ✅ THÊM: Tham gia group user để nhận events realtime
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");

                // Update in-memory service
                _presenceService.SetOnline(userId);

                // Update database
                using (var scope = Context.GetHttpContext().RequestServices.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var user = await db.Users.FindAsync(userId);
                    if (user != null)
                    {
                        user.IsOnline = true;
                        user.LastOnlineTime = null; // Clear last seen when online
                        await db.SaveChangesAsync();
                    }
                }

                // Broadcast to all clients
                await Clients.All.SendAsync("UserStatusChanged", new
                {
                    userId = userId,
                    isOnline = true,
                    lastSeen = (DateTime?)null
                });

                _logger.LogInformation($"User {userId} connected and joined group user-{userId}");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                // ✅ THÊM: Rời khỏi group user khi disconnect
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");

                using (var scope = Context.GetHttpContext().RequestServices.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var user = await db.Users.FindAsync(userId);
                    if (user != null)
                    {
                        user.IsOnline = false;
                        // SỬA LỖI: Sử dụng UtcNow thay vì Now
                        user.LastOnlineTime = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                    }
                }

                // Gửi event offline cho tất cả client
                await Clients.All.SendAsync("UserStatusChanged", new
                {
                    userId = userId,
                    isOnline = false,
                    // SỬA LỖI: Sử dụng UtcNow thay vì Now
                    lastSeen = DateTime.UtcNow
                });

                _logger.LogInformation($"User {userId} disconnected and left group user-{userId}");
            }
            await base.OnDisconnectedAsync(exception);
        }
        // Client gọi ping mỗi 15s để báo "tôi vẫn online"
        public async Task Ping()
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                _presenceService.SetOnline(userId);

                // Nếu muốn realtime hơn, có thể bắn sự kiện này luôn
                await Clients.All.SendAsync("UserStatusChanged", new
                {
                    userId = userId,
                    isOnline = true,
                    lastSeen = (DateTime?)null
                });
            }
        }

        public async Task ThamGiaCuocTroChuyen(string maCuocTroChuyen)
        {
            _logger.LogInformation($"[SignalR] ConnectionId={Context.ConnectionId} joining group '{maCuocTroChuyen}'");
            await Groups.AddToGroupAsync(Context.ConnectionId, maCuocTroChuyen);
        }

        public async Task RoiKhoiCuocTroChuyen(string maCuocTroChuyen)
        {
            _logger.LogInformation($"[SignalR] ConnectionId={Context.ConnectionId} leaving group '{maCuocTroChuyen}'");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, maCuocTroChuyen);
        }

        public async Task GuiTinNhan(string maCuocTroChuyen, string maNguoiGui, string noiDung, string loaiTinNhan = "text")
        {
            _logger.LogInformation($"[SignalR] Received message from user '{maNguoiGui}' in conversation '{maCuocTroChuyen}'. Content: {noiDung}, Type: {loaiTinNhan}");

            try
            {
                var chat = await _context.CuocTroChuyens
                    .Include(c => c.NguoiThamGias)
                    .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

                if (chat == null)
                    throw new HubException("Cuộc trò chuyện không tồn tại.");

                var otherUser = chat.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung != maNguoiGui);
                if (otherUser != null)
                {
                    var isBlocked = await _context.BlockedUsers
                        .AnyAsync(b => (b.BlockerId == otherUser.MaNguoiDung && b.BlockedId == maNguoiGui) ||
                                       (b.BlockerId == maNguoiGui && b.BlockedId == otherUser.MaNguoiDung));
                    if (isBlocked)
                        throw new HubException("Không thể gửi tin nhắn vì một trong hai người đã chặn người kia.");
                }

                // Xác định loại tin nhắn
                LoaiTinNhan loai = LoaiTinNhan.Text;
                if (!string.IsNullOrEmpty(loaiTinNhan) && Enum.TryParse<LoaiTinNhan>(loaiTinNhan, true, out var parsed))
                    loai = parsed;

                // Tạo tin nhắn mới
                var tinNhanMoi = new TinNhan
                {
                    MaCuocTroChuyen = maCuocTroChuyen,
                    MaNguoiGui = maNguoiGui,
                    ThoiGianGui = DateTime.UtcNow,
                    Loai = loai,
                    NoiDung = loai == LoaiTinNhan.Text ? noiDung : "",
                    MediaUrl = (loai == LoaiTinNhan.Image || loai == LoaiTinNhan.Video) ? noiDung : null
                };

                _context.TinNhans.Add(tinNhanMoi);
                await _context.SaveChangesAsync();

                var cuocTroChuyen = await _context.CuocTroChuyens
                    .Include(c => c.NguoiThamGias)
                        .ThenInclude(ntg => ntg.NguoiDung)
                    .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

                if (cuocTroChuyen != null)
                {
                    if (cuocTroChuyen.IsEmpty)
                    {
                        cuocTroChuyen.IsEmpty = false;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Conversation '{maCuocTroChuyen}' marked as not empty.");
                    }

                    var otherUserInfo = cuocTroChuyen.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung != maNguoiGui);
                    var senderUser = cuocTroChuyen.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung == maNguoiGui);

                    // Lấy trạng thái chat của người gửi
                    var senderChatState = await _context.UserChatStates
                        .FirstOrDefaultAsync(ucs => ucs.UserId == maNguoiGui && ucs.ChatId == maCuocTroChuyen);

                    // Lấy trạng thái chat của người nhận
                    var receiverChatState = otherUserInfo != null ? await _context.UserChatStates
                        .FirstOrDefaultAsync(ucs => ucs.UserId == otherUserInfo.MaNguoiDung && ucs.ChatId == maCuocTroChuyen) : null;

                    // ✅ FIX: CHỈ reset trạng thái XÓA cho người gửi, GIỮ NGUYÊN TRẠNG THÁI ẨN
                    if (senderChatState != null && senderChatState.IsDeleted)
                    {
                        senderChatState.IsDeleted = false;
                        // ❌ KHÔNG reset IsHidden cho người gửi
                    }

                    // ✅ FIX: CHỈ reset trạng thái XÓA cho người nhận, GIỮ NGUYÊN TRẠNG THÁI ẨN
                    if (receiverChatState != null && receiverChatState.IsDeleted)
                    {
                        receiverChatState.IsDeleted = false;
                        // ❌ KHÔNG reset IsHidden cho người nhận
                    }

                    await _context.SaveChangesAsync();

                    // Build object gửi cho người gửi
                    var chatForSender = new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        IsEmpty = false,
                        TieuDeTinDang = cuocTroChuyen.TieuDeTinDang,
                        AnhDaiDienTinDang = cuocTroChuyen.AnhDaiDienTinDang,
                        GiaTinDang = cuocTroChuyen.GiaTinDang,
                        MaNguoiConLai = otherUserInfo?.MaNguoiDung,
                        TenNguoiConLai = otherUserInfo?.NguoiDung?.FullName,
                        TinNhanCuoi = loai == LoaiTinNhan.Text ? tinNhanMoi.NoiDung : tinNhanMoi.MediaUrl,
                        MaNguoiGui = tinNhanMoi.MaNguoiGui,
                        LoaiTinNhan = loai.ToString().ToLower(),
                        ThoiGianCapNhat = DateTime.UtcNow, // ✅ Thêm timestamp
                        HasUnreadMessages = false,
                        IsHidden = senderChatState?.IsHidden ?? false,
                        IsDeleted = senderChatState?.IsDeleted ?? false
                    };

                    // Build object gửi cho người nhận
                    var chatForReceiver = new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        IsEmpty = false,
                        TieuDeTinDang = cuocTroChuyen.TieuDeTinDang,
                        AnhDaiDienTinDang = cuocTroChuyen.AnhDaiDienTinDang,
                        GiaTinDang = cuocTroChuyen.GiaTinDang,
                        MaNguoiConLai = senderUser?.MaNguoiDung,
                        TenNguoiConLai = senderUser?.NguoiDung?.FullName,
                        TinNhanCuoi = loai == LoaiTinNhan.Text ? tinNhanMoi.NoiDung : tinNhanMoi.MediaUrl,
                        MaNguoiGui = tinNhanMoi.MaNguoiGui,
                        LoaiTinNhan = loai.ToString().ToLower(),
                        ThoiGianCapNhat = DateTime.UtcNow, // ✅ Thêm timestamp
                                                           // ✅ FIX: Chỉ set unread = true nếu chat KHÔNG BỊ ẨN
                        HasUnreadMessages = !(receiverChatState?.IsHidden ?? false),
                        IsHidden = receiverChatState?.IsHidden ?? false,
                        IsDeleted = receiverChatState?.IsDeleted ?? false
                    };

                    // Gửi cập nhật cho cả 2 phía
                    await Clients.Group($"user-{maNguoiGui}").SendAsync("CapNhatCuocTroChuyen", chatForSender);
                    if (otherUserInfo != null)
                        await Clients.Group($"user-{otherUserInfo.MaNguoiDung}").SendAsync("CapNhatCuocTroChuyen", chatForReceiver);
                }

                // Gửi tin nhắn thực tế
                await Clients.Group(maCuocTroChuyen).SendAsync("NhanTinNhan", new
                {
                    maTinNhan = tinNhanMoi.MaTinNhan,
                    maCuocTroChuyen,
                    maNguoiGui,
                    noiDung = loai == LoaiTinNhan.Text ? tinNhanMoi.NoiDung : tinNhanMoi.MediaUrl,
                    loaiTinNhan = loai.ToString().ToLower(),
                    thoiGianGui = tinNhanMoi.ThoiGianGui,
                    daXem = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending message in conversation '{maCuocTroChuyen}' by user '{maNguoiGui}'");
                throw;
            }
        }


        public async Task DanhDauDaXem(string maCuocTroChuyen, string maNguoiXem)
        {
            try
            {
                var chuaXem = await _context.TinNhans
                    .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen
                             && t.MaNguoiGui != maNguoiXem
                             && !t.DaXem)
                    .OrderBy(t => t.ThoiGianGui)
                    .ToListAsync();

                if (chuaXem.Any())
                {
                    foreach (var msg in chuaXem)
                    {
                        msg.DaXem = true;
                        msg.ThoiGianXem = DateTime.UtcNow;
                    }

                    await _context.SaveChangesAsync();

                    var tinNhanCuoi = chuaXem.Last();

                    _logger.LogInformation($"[SignalR] User '{maNguoiXem}' marked messages as seen in conversation '{maCuocTroChuyen}'");

                    await Clients.Group(maCuocTroChuyen).SendAsync("DaXemTinNhan", new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        MaTinNhanCuoi = tinNhanCuoi.MaTinNhan,
                        NguoiXem = maNguoiXem
                    });

                    await Clients.Group($"user-{maNguoiXem}").SendAsync("CapNhatTrangThaiTinNhan", new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        HasUnreadMessages = false
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking messages as seen in conversation '{maCuocTroChuyen}'");
            }
        }

        public async Task ThuHoiTinNhan(int maTinNhan, string maNguoiGui)
        {
            _logger.LogInformation($"[SignalR] User '{maNguoiGui}' attempting to recall text message {maTinNhan}");

            try
            {
                var tinNhan = await _context.TinNhans
                    .FirstOrDefaultAsync(t => t.MaTinNhan == maTinNhan);

                if (tinNhan == null)
                {
                    _logger.LogWarning($"Message {maTinNhan} not found for recall by user '{maNguoiGui}'");
                    throw new HubException("Tin nhắn không tồn tại.");
                }

                if (tinNhan.MaNguoiGui != maNguoiGui)
                {
                    _logger.LogWarning($"User '{maNguoiGui}' tried to recall message {maTinNhan} without permission");
                    throw new HubException("Bạn không có quyền thu hồi tin nhắn này.");
                }

                var timeDifference = DateTime.UtcNow - tinNhan.ThoiGianGui;
                if (timeDifference.TotalMinutes > 5)
                {
                    _logger.LogWarning($"User '{maNguoiGui}' tried to recall message {maTinNhan} after 5 minutes");
                    throw new HubException("Chỉ có thể thu hồi tin nhắn trong vòng 5 phút sau khi gửi.");
                }

                if (tinNhan.Loai != LoaiTinNhan.Text)
                {
                    _logger.LogWarning($"User '{maNguoiGui}' tried to recall non-text message {maTinNhan}");
                    throw new HubException("Chỉ có thể thu hồi tin nhắn văn bản bằng phương thức này.");
                }

                var maCuocTroChuyen = tinNhan.MaCuocTroChuyen;
                _context.TinNhans.Remove(tinNhan);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"[SignalR] Text message {maTinNhan} recalled successfully by user '{maNguoiGui}'");

                await Clients.Group(maCuocTroChuyen).SendAsync("TinNhanDaThuHoi", new
                {
                    maTinNhan = maTinNhan,
                    maCuocTroChuyen = maCuocTroChuyen,
                    maNguoiThuHoi = maNguoiGui,
                    loaiTinNhan = "text"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recalling text message {maTinNhan} by user '{maNguoiGui}'");
                throw;
            }
        }

        public async Task ThuHoiAnhVideo(int maTinNhan, string maNguoiGui)
        {
            _logger.LogInformation($"[SignalR] User '{maNguoiGui}' attempting to recall media message {maTinNhan}");

            try
            {
                var tinNhan = await _context.TinNhans
                    .FirstOrDefaultAsync(t => t.MaTinNhan == maTinNhan);

                if (tinNhan == null)
                {
                    _logger.LogWarning($"Media message {maTinNhan} not found for recall by user '{maNguoiGui}'");
                    throw new HubException("Tin nhắn không tồn tại.");
                }

                if (tinNhan.MaNguoiGui != maNguoiGui)
                {
                    _logger.LogWarning($"User '{maNguoiGui}' tried to recall media message {maTinNhan} without permission");
                    throw new HubException("Bạn không có quyền thu hồi tin nhắn này.");
                }

                var timeDifference = DateTime.UtcNow - tinNhan.ThoiGianGui;
                if (timeDifference.TotalMinutes > 5)
                {
                    _logger.LogWarning($"User '{maNguoiGui}' tried to recall media message {maTinNhan} after 5 minutes");
                    throw new HubException("Chỉ có thể thu hồi tin nhắn trong vòng 5 phút sau khi gửi.");
                }

                if (tinNhan.Loai != LoaiTinNhan.Image && tinNhan.Loai != LoaiTinNhan.Video)
                {
                    _logger.LogWarning($"User '{maNguoiGui}' tried to recall non-media message {maTinNhan}");
                    throw new HubException("Chỉ có thể thu hồi tin nhắn ảnh hoặc video bằng phương thức này.");
                }

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

                        if (deleteResult.Result == "ok")
                            _logger.LogInformation($"Successfully deleted media from Cloudinary for message {maTinNhan}");
                        else
                            _logger.LogWarning($"Could not delete media from Cloudinary for message {maTinNhan}. Result: {deleteResult.Result}");
                    }
                    else
                        _logger.LogWarning($"Could not extract publicId from URL: {mediaUrl}");
                }

                _context.TinNhans.Remove(tinNhan);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"[SignalR] Media message {maTinNhan} recalled successfully by user '{maNguoiGui}'");

                await Clients.Group(maCuocTroChuyen).SendAsync("TinNhanDaThuHoi", new
                {
                    maTinNhan = maTinNhan,
                    maCuocTroChuyen = maCuocTroChuyen,
                    maNguoiThuHoi = maNguoiGui,
                    loaiTinNhan = tinNhan.Loai == LoaiTinNhan.Image ? "image" : "video"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recalling media message {maTinNhan} by user '{maNguoiGui}'");
                throw;
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
                _logger.LogError(ex, $"Error extracting publicId from URL {cloudinaryUrl}");
                return null;
            }
        }
        public async Task CapNhatTrangThaiNguoiDung(string userId, bool isOnline)
        {
            // Cập nhật trạng thái trong database (nếu có)
            using (var scope = Context.GetHttpContext().RequestServices.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await db.Users.FindAsync(userId);
                if (user != null)
                {
                    user.IsOnline = isOnline;
                    // SỬA LỖI: Sử dụng UtcNow thay vì Now
                    user.LastOnlineTime = isOnline ? null : DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }

            // Gửi event cho tất cả client với payload thống nhất
            await Clients.All.SendAsync("UserStatusChanged", new
            {
                userId = userId,
                isOnline = isOnline,
                // SỬA LỖI: Sử dụng UtcNow thay vì Now
                lastSeen = isOnline ? (DateTime?)null : DateTime.UtcNow
            });
        }
    }
}