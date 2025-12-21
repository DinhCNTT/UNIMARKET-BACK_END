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
using UniMarket.Controllers;

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
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");

                // ✅ GHI LẠI THỜI GIAN ĐỂ DÙNG CHUNG
                var lastSeenTime = DateTime.UtcNow;

                using (var scope = Context.GetHttpContext().RequestServices.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var user = await db.Users.FindAsync(userId);
                    if (user != null)
                    {
                        user.IsOnline = false;
                        user.LastOnlineTime = lastSeenTime; // Dùng biến lastSeenTime
                        await db.SaveChangesAsync();
                    }
                }

                // Gửi event offline cho tất cả client
                await Clients.All.SendAsync("UserStatusChanged", new
                {
                    userId = userId,
                    isOnline = false,
                    lastSeen = lastSeenTime, // Dùng biến lastSeenTime

                    // ✅ THÊM DÒNG NÀY ĐỂ GỬI CHUỖI ĐÃ FORMAT
                    formattedLastSeen = UserController.FormatLastSeen(lastSeenTime)
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
                {
                    // Nếu là chat AI (ID bắt đầu bằng ai-assistant-), tạo cuộc trò chuyện placeholder nếu chưa có
                    if (!string.IsNullOrEmpty(maCuocTroChuyen) && maCuocTroChuyen.StartsWith("ai-assistant-"))
                    {
                        var placeholder = new CuocTroChuyen
                        {
                            MaCuocTroChuyen = maCuocTroChuyen,
                            ThoiGianTao = DateTime.UtcNow,
                            IsEmpty = true,
                            MaTinDang = 0,
                            TieuDeTinDang = "Uni.AI",
                            AnhDaiDienTinDang = "/images/uni-ai-avatar.png",
                            GiaTinDang = 0,
                            MaNguoiBan = null,
                            IsPostDeleted = false
                        };

                        _context.CuocTroChuyens.Add(placeholder);
                        _context.NguoiThamGias.Add(new NguoiThamGia { MaCuocTroChuyen = maCuocTroChuyen, MaNguoiDung = maNguoiGui });
                        await _context.SaveChangesAsync();

                        chat = await _context.CuocTroChuyens
                            .Include(c => c.NguoiThamGias)
                            .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);
                    }
                    else
                    {
                        throw new HubException("Cuộc trò chuyện không tồn tại.");
                    }
                }

                // Kiểm tra chặn (Block)
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
                    // Location được lưu vào NoiDung giống như Text
                    NoiDung = (loai == LoaiTinNhan.Text || loai == LoaiTinNhan.Location) ? noiDung : "",
                    MediaUrl = (loai == LoaiTinNhan.Image || loai == LoaiTinNhan.Video) ? noiDung : null
                };

                _context.TinNhans.Add(tinNhanMoi);
                await _context.SaveChangesAsync();

                // Cập nhật trạng thái cuộc trò chuyện (IsDeleted, IsHidden, IsEmpty)
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

                    // Lấy trạng thái chat
                    var senderChatState = await _context.UserChatStates
                        .FirstOrDefaultAsync(ucs => ucs.UserId == maNguoiGui && ucs.ChatId == maCuocTroChuyen);

                    var receiverChatState = otherUserInfo != null ? await _context.UserChatStates
                        .FirstOrDefaultAsync(ucs => ucs.UserId == otherUserInfo.MaNguoiDung && ucs.ChatId == maCuocTroChuyen) : null;

                    // Nếu người gửi từng xóa chat, khôi phục lại (nhưng giữ nguyên trạng thái ẩn nếu có)
                    if (senderChatState != null && senderChatState.IsDeleted)
                    {
                        senderChatState.IsDeleted = false;
                    }

                    // Nếu người nhận từng xóa chat, khôi phục lại để hiện tin nhắn mới
                    if (receiverChatState != null && receiverChatState.IsDeleted)
                    {
                        receiverChatState.IsDeleted = false;
                    }

                    // Đánh dấu HasReappeared (cho logic ẩn chat giống Messenger)
                    var senderHidden = await _context.UserHiddenConversations
                        .FirstOrDefaultAsync(h => h.UserId == maNguoiGui && h.MaCuocTroChuyen == maCuocTroChuyen);
                    if (senderHidden != null) senderHidden.HasReappeared = true;

                    if (otherUserInfo != null)
                    {
                        var receiverHidden = await _context.UserHiddenConversations
                            .FirstOrDefaultAsync(h => h.UserId == otherUserInfo.MaNguoiDung && h.MaCuocTroChuyen == maCuocTroChuyen);
                        if (receiverHidden != null) receiverHidden.HasReappeared = true;
                    }

                    await _context.SaveChangesAsync();

                    // ====================================================================================
                    // 🛠️ PHẦN SỬA LỖI QUAN TRỌNG: Logic TinNhanCuoi cho Preview
                    // ====================================================================================

                    // Nếu là Location hoặc Text -> Lấy NoiDung. Nếu là Image/Video -> Lấy MediaUrl
                    var lastMessageContent = (loai == LoaiTinNhan.Text || loai == LoaiTinNhan.Location)
                                             ? tinNhanMoi.NoiDung
                                             : tinNhanMoi.MediaUrl;

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

                        // ✅ Đã sửa: dùng biến lastMessageContent đã xử lý đúng logic ở trên
                        TinNhanCuoi = lastMessageContent,

                        MaNguoiGui = tinNhanMoi.MaNguoiGui,
                        LoaiTinNhan = loai.ToString().ToLower(),
                        ThoiGianCapNhat = DateTime.UtcNow,
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

                        // ✅ Đã sửa: dùng biến lastMessageContent
                        TinNhanCuoi = lastMessageContent,

                        MaNguoiGui = tinNhanMoi.MaNguoiGui,
                        LoaiTinNhan = loai.ToString().ToLower(),
                        ThoiGianCapNhat = DateTime.UtcNow,
                        HasUnreadMessages = !(receiverChatState?.IsHidden ?? false),
                        IsHidden = receiverChatState?.IsHidden ?? false,
                        IsDeleted = receiverChatState?.IsDeleted ?? false
                    };

                    // Gửi cập nhật Sidebar (Chat List) cho cả 2 phía
                    await Clients.Group($"user-{maNguoiGui}").SendAsync("CapNhatCuocTroChuyen", chatForSender);
                    if (otherUserInfo != null)
                        await Clients.Group($"user-{otherUserInfo.MaNguoiDung}").SendAsync("CapNhatCuocTroChuyen", chatForReceiver);
                }

                // Gửi tin nhắn thực tế vào khung chat (Chat Box)
                await Clients.Group(maCuocTroChuyen).SendAsync("NhanTinNhan", new
                {
                    maTinNhan = tinNhanMoi.MaTinNhan,
                    maCuocTroChuyen,
                    maNguoiGui,
                    // Logic hiển thị nội dung tin nhắn realtime
                    noiDung = (loai == LoaiTinNhan.Text || loai == LoaiTinNhan.Location) ? tinNhanMoi.NoiDung : tinNhanMoi.MediaUrl,
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
                    var idNguoiGui = tinNhanCuoi.MaNguoiGui; // 🔥 LẤY ID NGƯỜI GỬI (Người cần nhận thông báo "Đã xem")

                    _logger.LogInformation($"[SignalR] User '{maNguoiXem}' marked messages as seen. Notifying sender '{idNguoiGui}'");

                    // 1. Gửi vào group chat chung (Giữ nguyên cái cũ của bạn)
                    await Clients.Group(maCuocTroChuyen).SendAsync("DaXemTinNhan", new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        MaTinNhanCuoi = tinNhanCuoi.MaTinNhan,
                        NguoiXem = maNguoiXem
                    });

                    // 🔥 2. THÊM ĐOẠN NÀY: Gửi đích danh vào Group User của người gửi
                    // Đây là cái giúp người gửi thấy chữ "Đã xem" ngay lập tức kể cả khi họ đang ở ngoài list chat
                    await Clients.Group($"user-{idNguoiGui}").SendAsync("DaXemTinNhan", new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        MaTinNhanCuoi = tinNhanCuoi.MaTinNhan,
                        NguoiXem = maNguoiXem
                    });

                    // 3. Cập nhật cho người xem (Giữ nguyên cái cũ của bạn)
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
            _logger.LogInformation($"[SignalR] User '{maNguoiGui}' attempting to recall message {maTinNhan}");

            try
            {
                var tinNhan = await _context.TinNhans
                    .FirstOrDefaultAsync(t => t.MaTinNhan == maTinNhan);

                // 1. Kiểm tra tồn tại
                if (tinNhan == null)
                {
                    _logger.LogWarning($"Message {maTinNhan} not found for recall by user '{maNguoiGui}'");
                    throw new HubException("Tin nhắn không tồn tại.");
                }

                // 2. Kiểm tra quyền chủ sở hữu
                if (tinNhan.MaNguoiGui != maNguoiGui)
                {
                    _logger.LogWarning($"User '{maNguoiGui}' tried to recall message {maTinNhan} without permission");
                    throw new HubException("Bạn không có quyền thu hồi tin nhắn này.");
                }

                // 3. Kiểm tra thời gian (5 phút)
                var timeDifference = DateTime.UtcNow - tinNhan.ThoiGianGui;
                if (timeDifference.TotalMinutes > 5)
                {
                    _logger.LogWarning($"User '{maNguoiGui}' tried to recall message {maTinNhan} after 5 minutes");
                    throw new HubException("Chỉ có thể thu hồi tin nhắn trong vòng 5 phút sau khi gửi.");
                }

                // 4. Xử lý phân loại tin nhắn
                // Nếu là Ảnh hoặc Video -> Gọi hàm chuyên dụng để xóa file trên Cloud
                if (tinNhan.Loai == LoaiTinNhan.Image || tinNhan.Loai == LoaiTinNhan.Video)
                {
                    await ThuHoiAnhVideo(maTinNhan, maNguoiGui);
                    return; // Kết thúc hàm này tại đây
                }

                // Nếu KHÔNG phải Text VÀ KHÔNG phải Location -> Báo lỗi
                if (tinNhan.Loai != LoaiTinNhan.Text && tinNhan.Loai != LoaiTinNhan.Location)
                {
                    _logger.LogWarning($"User '{maNguoiGui}' tried to recall unsupported message type {maTinNhan}");
                    throw new HubException("Loại tin nhắn này không hỗ trợ thu hồi.");
                }

                var maCuocTroChuyen = tinNhan.MaCuocTroChuyen;

                // 5. Thực hiện thu hồi (Soft delete)
                tinNhan.IsRecalled = true;
                tinNhan.ThoiGianThuHoi = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"[SignalR] Message {maTinNhan} ({tinNhan.Loai}) recalled successfully by user '{maNguoiGui}'");

                // 6. Gửi thông báo cho mọi người trong phòng
                await Clients.Group(maCuocTroChuyen).SendAsync("TinNhanDaThuHoi", new
                {
                    maTinNhan = maTinNhan,
                    maCuocTroChuyen = maCuocTroChuyen,
                    maNguoiThuHoi = maNguoiGui,
                    // Trả về đúng loại ("text" hoặc "location") để UI hiển thị đúng icon
                    loaiTinNhan = tinNhan.Loai.ToString().ToLower(),
                    isRecalled = true
                });

                // 7. Cập nhật dòng tin nhắn xem trước bên sidebar (ChatList)
                await UpdateChatPreviewAfterRecall(maCuocTroChuyen);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recalling message {maTinNhan} by user '{maNguoiGui}'");
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

                // ✅ VẪN XÓA TRÊN CLOUDINARY
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

                // ✅ THAY ĐỔI: Đánh dấu thu hồi thay vì xóa
                tinNhan.IsRecalled = true;
                tinNhan.ThoiGianThuHoi = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"[SignalR] Media message {maTinNhan} recalled successfully by user '{maNguoiGui}'");

                await Clients.Group(maCuocTroChuyen).SendAsync("TinNhanDaThuHoi", new
                {
                    maTinNhan = maTinNhan,
                    maCuocTroChuyen = maCuocTroChuyen,
                    maNguoiThuHoi = maNguoiGui,
                    loaiTinNhan = tinNhan.Loai == LoaiTinNhan.Image ? "image" : "video",
                    isRecalled = true
                });

                // ✅ Cập nhật preview trong ChatList
                await UpdateChatPreviewAfterRecall(maCuocTroChuyen);
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
        private async Task UpdateChatPreviewAfterRecall(string maCuocTroChuyen)
        {
            var cuocTroChuyen = await _context.CuocTroChuyens
                .Include(c => c.NguoiThamGias)
                    .ThenInclude(ntg => ntg.NguoiDung)
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

            if (cuocTroChuyen == null) return;

            foreach (var nguoiThamGia in cuocTroChuyen.NguoiThamGias)
            {
                var tinNhanCuoi = await _context.TinNhans
                    .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen)
                    .Where(t => !_context.TinNhanXoas.Any(x => x.MaTinNhan == t.MaTinNhan && x.UserId == nguoiThamGia.MaNguoiDung))
                    .OrderByDescending(t => t.ThoiGianGui)
                    .Select(t => new
                    {
                        t.NoiDung,
                        t.MaNguoiGui,
                        LoaiTinNhan = t.Loai.ToString().ToLower(),
                        t.IsRecalled,
                        TenNguoiGui = t.NguoiGui.FullName
                    })
                    .FirstOrDefaultAsync();

                if (tinNhanCuoi != null)
                {
                    var otherUser = cuocTroChuyen.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung != nguoiThamGia.MaNguoiDung);

                    await Clients.Group($"user-{nguoiThamGia.MaNguoiDung}").SendAsync("CapNhatCuocTroChuyen", new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        IsEmpty = false,
                        TieuDeTinDang = cuocTroChuyen.TieuDeTinDang,
                        AnhDaiDienTinDang = cuocTroChuyen.AnhDaiDienTinDang,
                        GiaTinDang = cuocTroChuyen.GiaTinDang,
                        MaNguoiConLai = otherUser?.MaNguoiDung,
                        TenNguoiConLai = otherUser?.NguoiDung?.FullName,
                        TinNhanCuoi = tinNhanCuoi.IsRecalled ? "Đã thu hồi tin nhắn" : tinNhanCuoi.NoiDung,
                        MaNguoiGui = tinNhanCuoi.MaNguoiGui,
                        LoaiTinNhan = tinNhanCuoi.IsRecalled ? "text" : tinNhanCuoi.LoaiTinNhan,
                        IsRecalled = tinNhanCuoi.IsRecalled,
                        TenNguoiThuHoi = tinNhanCuoi.TenNguoiGui,
                        ThoiGianCapNhat = DateTime.UtcNow
                    });
                }
            }
        }
    }
}
