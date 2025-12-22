using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Helpers;
using System.IO;
using System.Linq;
using System;
using YourNamespace.Helpers;
using System.Text.RegularExpressions;

[Authorize]
public class SocialChatHub : Hub
{
    private readonly ApplicationDbContext _context;
    private readonly IUserAffinityService _affinityService;

    public SocialChatHub(ApplicationDbContext context, IUserAffinityService affinityService)
    {
        _context = context;
        _affinityService = affinityService;
    }

    private string? GetUserId()
    {
        return Context.UserIdentifier ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    // Khi user kết nối
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            var ua = await _context.UserActivities.FindAsync(userId);
            if (ua == null)
            {
                ua = new UserActivity
                {
                    UserId = userId,
                    IsOnline = true,
                    LastActive = DateTime.UtcNow
                };
                _context.UserActivities.Add(ua);
            }
            else
            {
                ua.IsOnline = true;
                ua.LastActive = DateTime.UtcNow;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Ghi log lỗi ở đây nếu cần
            }

            // Gửi thông báo cho các client khác về trạng thái online
            await Clients.Others.SendAsync("PresenceUpdated", new
            {
                userId,
                isOnline = true,
                lastActive = (DateTime?)null
            });
        }
        await base.OnConnectedAsync();
    }

    // Khi user ngắt kết nối
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            try
            {
                var ua = await _context.UserActivities.FindAsync(userId);
                if (ua != null)
                {
                    ua.IsOnline = false;
                    ua.LastActive = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                // Gửi thông báo cho các client khác về trạng thái offline
                await Clients.Others.SendAsync("PresenceUpdated", new
                {
                    userId,
                    isOnline = false,
                    lastActive = ua?.LastActive ?? DateTime.UtcNow
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                // An toàn bỏ qua lỗi xung đột
            }
            catch (Exception ex)
            {
                // log nếu cần
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    // Ping giữ kết nối online
    public async Task Ping()
    {
        var userId = GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            var ua = await _context.UserActivities.FindAsync(userId);
            if (ua != null)
            {
                ua.IsOnline = true;
                ua.LastActive = DateTime.UtcNow;
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException) { /* Bỏ qua lỗi xung đột */ }
            }
        }
    }

    // Tham gia 1 group chat
    public Task JoinGroup(string maCuocTroChuyen)
        => Groups.AddToGroupAsync(Context.ConnectionId, maCuocTroChuyen);

    public Task LeaveGroup(string maCuocTroChuyen)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, maCuocTroChuyen);

    // Helper: kiểm tra url có dạng video hay không
    private bool IsVideoUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        try
        {
            var lower = url.ToLowerInvariant();
            if (lower.Contains("/video/") || lower.Contains("cdn") && lower.Contains("video")) return true;
            var ext = Path.GetExtension(url);
            if (!string.IsNullOrEmpty(ext))
            {
                var videoExts = new[] { ".mp4", ".mov", ".avi", ".wmv", ".mkv" };
                if (videoExts.Contains(ext.ToLower())) return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    private string DetermineMessageType(string? noiDung, string? mediaUrl)
    {
        if (!string.IsNullOrEmpty(mediaUrl))
        {
            return IsVideoUrl(mediaUrl) ? "video" : "image";
        }

        // Nếu noiDung chứa một hint dạng [ShareId:***:video] hoặc có từ khóa video
        if (!string.IsNullOrEmpty(noiDung))
        {
            var m = System.Text.RegularExpressions.Regex.Match(noiDung, @"\[ShareId:[^\]\:]+(?::(\w+))?\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value))
            {
                var hint = m.Groups[1].Value.ToLowerInvariant();
                if (hint == "video") return "video";
            }

            if (noiDung.ToLowerInvariant().Contains("/video/") || noiDung.ToLowerInvariant().Contains("video"))
            {
                // heuristic
                return "video";
            }
        }

        return "text";
    }

    // Gửi tin nhắn
    // FILE: SocialChatHub.cs
    // =========================================================
    // 🔥 Phiên bản hoàn chỉnh tối ưu cho nhiều người dùng
    // =========================================================
    public async Task SendMessage(string maCuocTroChuyen, string content, string? mediaUrl, string? parentMessageId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return;

        // ======================= 1. LẤY NGƯỜI GỬI =======================
        var sender = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (sender == null) return;

        // ======================= 2. LẤY CUỘC TRÒ CHUYỆN =======================
        var convo = await _context.CuocTroChuyenSocials
            .Include(c => c.NguoiThamGias).ThenInclude(n => n.User)
            .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

        if (convo == null) return;

        // ============================================================
        // 🚫 3. KIỂM TRA TRẠNG THÁI CHẶN (BLOCK)
        // ============================================================
        if (convo.IsBlocked)
        {
            string errorMessage = convo.MaNguoiChan == userId
                ? "Bạn cần gỡ chặn người dùng này để gửi tin nhắn."
                : "Bạn đã bị người dùng này chặn.";

            await Clients.Caller.SendAsync("ReceiveError", errorMessage);
            return;
        }

        // ============================================================
        // 🚫 4. KIỂM TRA GIỚI HẠN SPAM (ANTI-SPAM CHECK)
        // ============================================================
        var partner = convo.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung != userId);
        if (partner != null)
        {
            // 4.1. Kiểm tra xem đối phương có follow mình không
            var isFollowedBack = await _context.Follows
                .AnyAsync(f => f.FollowerId == partner.MaNguoiDung && f.FollowingId == userId);

            if (!isFollowedBack)
            {
                // 4.2. Lấy 3 tin nhắn gần nhất
                // ⚠️ QUAN TRỌNG: KHÔNG check !IsRecalled. Tin thu hồi vẫn tính là 1 lần gửi.
                var last3Messages = await _context.TinNhanSocials
                    .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen)
                    .OrderByDescending(t => t.ThoiGianGui)
                    .Take(3)
                    .Select(t => t.MaNguoiGui)
                    .ToListAsync();

                // 4.3. Nếu gửi quá 3 tin liên tiếp mà chưa được rep
                if (last3Messages.Count >= 3 && last3Messages.All(senderId => senderId == userId))
                {
                    await Clients.Caller.SendAsync("ReceiveError", "Bạn đã đạt giới hạn tin nhắn. Việc xóa/thu hồi tin nhắn không giúp bạn gửi thêm được.");
                    return; // ⛔ DỪNG LẠI NGAY
                }
            }
        }

        // ============================================================
        // ✨ 5. TẠO TIN NHẮN MỚI (DB)
        // ============================================================
        var msg = new TinNhanSocial
        {
            MaCuocTroChuyen = maCuocTroChuyen,
            MaNguoiGui = userId,
            NoiDung = content,
            MediaUrl = mediaUrl,
            ThoiGianGui = DateTime.UtcNow,
            DaXem = false,
            ParentMessageId = parentMessageId
        };

        _context.TinNhanSocials.Add(msg);
        convo.IsEmpty = false;
        convo.NgayCapNhat = DateTime.UtcNow;

        // ============================================================
        // 🔄 6. CẬP NHẬT TRẠNG THÁI ẨN/HIỆN CHAT
        // ============================================================
        // Hiện lại cho người gửi (nếu đang ẩn)
        var senderHidden = await _context.UserHiddenConversations
            .FirstOrDefaultAsync(h => h.UserId == userId && h.MaCuocTroChuyen == maCuocTroChuyen);
        if (senderHidden != null) senderHidden.HasReappeared = true;

        // Hiện lại cho người nhận (nếu đang ẩn)
        if (partner != null)
        {
            var receiverHidden = await _context.UserHiddenConversations
                .FirstOrDefaultAsync(h => h.UserId == partner.MaNguoiDung && h.MaCuocTroChuyen == maCuocTroChuyen);
            if (receiverHidden != null) receiverHidden.HasReappeared = true;
        }

        await _context.SaveChangesAsync(); // Lưu để lấy MaTinNhan
        foreach (var participant in convo.NguoiThamGias)
        {
            if (participant.MaNguoiDung != userId)
            {
                // Gọi Service để cộng điểm: Gửi tin nhắn = 10 điểm
                await _affinityService.TrackInteractionAsync(userId, participant.MaNguoiDung, InteractionType.SendMessage);
            }
        }

        // ============================================================
        // 🔁 7. LẤY TIN NHẮN CHA (NẾU REPLY)
        // ============================================================
        TinNhanSocial? parentMessage = null;
        if (!string.IsNullOrEmpty(parentMessageId))
        {
            parentMessage = await _context.TinNhanSocials
                .Include(p => p.Sender)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.MaTinNhan == parentMessageId);
        }

        // ============================================================
        // 🧩 8. XỬ LÝ SHARE TRONG TIN CHA (REPLY) - REGEX
        // ============================================================
        object parentShareInfo = null;
        if (parentMessage != null && !parentMessage.IsRecalled)
        {
            var match = Regex.Match(parentMessage.NoiDung ?? "", @"\[ShareId:(\d+):?.*?\]");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int parentShareId))
            {
                var share = await _context.Shares
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.ShareId == parentShareId);
                if (share != null)
                {
                    parentShareInfo = new
                    {
                        share.ShareId,
                        share.PreviewTitle,
                        share.PreviewImage,
                        share.PreviewVideo,
                        share.ShareLink,
                        TargetType = (int)share.TargetType,
                        TinDangId = share.TinDangId
                    };
                }
            }
        }

        // ============================================================
        // 🧩 9. XỬ LÝ SHARE TRONG TIN CHÍNH - REGEX
        // ============================================================
        object mainShareInfo = null;
        var mainMatch = Regex.Match(msg.NoiDung ?? "", @"\[ShareId:(\d+):?.*?\]");
        if (mainMatch.Success && int.TryParse(mainMatch.Groups[1].Value, out int mainShareId))
        {
            var share = await _context.Shares
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ShareId == mainShareId);
            if (share != null)
            {
                mainShareInfo = new
                {
                    share.ShareId,
                    share.PreviewTitle,
                    share.PreviewImage,
                    share.PreviewVideo,
                    share.ShareLink,
                    TargetType = (int)share.TargetType,
                    TinDangId = share.TinDangId
                };
            }
        }

        // ============================================================
        // 📤 10. GỬI REALTIME MESSAGE (ReceiveMessage)
        // ============================================================
        var messageType = DetermineMessageType(msg.NoiDung, msg.MediaUrl);

        var messageDto = new
        {
            MaTinNhan = msg.MaTinNhan,
            MaCuocTroChuyen = msg.MaCuocTroChuyen,
            MaNguoiGui = msg.MaNguoiGui,
            NoiDung = msg.NoiDung,
            MediaUrl = msg.MediaUrl,
            ThoiGianGui = msg.ThoiGianGui.ToString("O"),
            DaXem = msg.DaXem,
            MessageType = messageType,
            LoaiTinNhan = messageType,
            Sender = new
            {
                Id = sender.Id,
                FullName = sender.FullName,
                AvatarUrl = sender.AvatarUrl
            },
            Share = mainShareInfo,
            ParentMessage = parentMessage == null ? null : new
            {
                MaTinNhan = parentMessage.MaTinNhan,
                NoiDung = parentMessage.IsRecalled ? "[Tin nhắn đã thu hồi]" : (parentMessage.NoiDung ?? ""),
                MediaUrl = parentMessage.IsRecalled ? null : parentMessage.MediaUrl,
                MaNguoiGui = parentMessage.MaNguoiGui,
                SenderFullName = parentMessage.Sender?.FullName,
                IsRecalled = parentMessage.IsRecalled,
                Share = parentShareInfo
            }
        };

        // 🚀 Gửi cho những người đang online trong group chat này
        await Clients.Group(maCuocTroChuyen).SendAsync("ReceiveMessage", messageDto);

        // ============================================================
        // 🔄 11. GỬI CẬP NHẬT LIST HỘI THOẠI (CapNhatCuocTroChuyen)
        // ============================================================
        // Lấy thông tin partner để gửi kèm trong DTO (dành cho người nhận hiển thị)
        var partnerParticipant = convo.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung != userId);
        var partnerDto = partnerParticipant != null
            ? new
            {
                Id = partnerParticipant.User?.Id ?? partnerParticipant.MaNguoiDung,
                FullName = partnerParticipant.User?.FullName,
                AvatarUrl = partnerParticipant.User?.AvatarUrl
            }
            : null;

        // Payload chung
        var payload = new
        {
            MaCuocTroChuyen = convo.MaCuocTroChuyen,
            TinNhanCuoi = MessageFormatter.Format(msg.NoiDung, sender.FullName),
            MediaUrl = msg.MediaUrl,
            ThoiGianCapNhat = msg.ThoiGianGui,
            NguoiGuiId = userId,
            TenNguoiGui = sender.FullName,
            AvatarNguoiGui = sender.AvatarUrl,
            MessageType = messageType,
            LoaiTinNhan = messageType,
            Partner = partnerDto
        };

        // Gửi sự kiện cập nhật cho từng người tham gia (để cập nhật thanh bên trái)
        var sendTasks = convo.NguoiThamGias.Select(async participant =>
        {
            try
            {
                await Clients.User(participant.MaNguoiDung)
                    .SendAsync("CapNhatCuocTroChuyen", payload);
            }
            catch { /* Bỏ qua nếu user offline hoặc lỗi kết nối */ }
        });

        await Task.WhenAll(sendTasks);
    }



    // User đang gõ
    public Task Typing(string maCuocTroChuyen, string? toUserId)
    {
        var userId = GetUserId();
        return Clients.Group(maCuocTroChuyen).SendAsync("Typing", new
        {
            maCuocTroChuyen,
            from = userId,
            to = toUserId
        });
    }

    // Thu hồi tin nhắn
    public async Task ThuHoiTinNhan(string maCuocTroChuyen, string maTinNhan)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return;

        // --- BƯỚC 1: LẤY RIÊNG LẼ ---

        // 1. Lấy tin nhắn (msg)
        var msg = await _context.TinNhanSocials.FirstOrDefaultAsync(m =>
            m.MaTinNhan == maTinNhan && m.MaCuocTroChuyen == maCuocTroChuyen);

        if (msg == null || msg.MaNguoiGui != userId) return;

        // 2. Lấy cuộc trò chuyện (convo)
        var convo = await _context.CuocTroChuyenSocials
            .Include(c => c.NguoiThamGias)
                .ThenInclude(n => n.User)
            .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

        if (convo == null) return; // Không tìm thấy cuộc trò chuyện

        // --- HẾT BƯỚC 1 ---

        // 3. Lấy thông tin người gửi (người thu hồi)
        var sender = convo.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung == userId)?.User;
        if (sender == null) return;

        // 4. Cập nhật cả hai đối tượng trong DB
        msg.IsRecalled = true;
        msg.NoiDung = "[Tin nhắn đã thu hồi]";
        msg.MediaUrl = null;

        convo.NgayCapNhat = DateTime.UtcNow; // Cập nhật thời gian cho cuộc trò chuyện

        await _context.SaveChangesAsync();

        // 5. Gửi sự kiện 'MessageRecalled' (cho cửa sổ chat đang mở)
        await Clients.Group(maCuocTroChuyen).SendAsync("MessageRecalled", new
        {
            MaTinNhan = msg.MaTinNhan,
            MaCuocTroChuyen = msg.MaCuocTroChuyen
        });

        // =================================================================
        // 6. Gửi sự kiện 'CapNhatCuocTroChuyen' (cho FriendChatList.jsx)
        // =================================================================

        // 6.1. Lấy thông tin partner (giống hàm SendMessage)
        var partnerParticipant = convo.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung != userId);
        object partnerDto = null;
        if (partnerParticipant != null)
        {
            var pUser = partnerParticipant.User; // Đã include User ở trên
            partnerDto = new
            {
                Id = pUser?.Id ?? partnerParticipant.MaNguoiDung,
                FullName = pUser?.FullName,
                AvatarUrl = pUser?.AvatarUrl
            };
        }

        // 6.2. Gửi cập nhật cho TẤT CẢ thành viên (giống hàm SendMessage)
        foreach (var participant in convo.NguoiThamGias)
        {
            var currentPartner = (participant.MaNguoiDung == userId)
                ? partnerDto
                : new { Id = sender.Id, FullName = sender.FullName, AvatarUrl = sender.AvatarUrl };

            var hasUnread = false;

            // 6.3. Tạo payload với nội dung đã thu hồi
            var payloadForUser = new
            {
                MaCuocTroChuyen = convo.MaCuocTroChuyen,
                TinNhanCuoi = msg.NoiDung, // Gửi "[Tin nhắn đã thu hồi]"
                MediaUrl = msg.MediaUrl,   // Gửi null
                ThoiGianCapNhat = convo.NgayCapNhat, // Dùng thời gian vừa cập nhật
                NguoiGuiId = userId,
                TenNguoiGui = sender.FullName,
                AvatarNguoiGui = sender.AvatarUrl,
                MessageType = "text",
                LoaiTinNhan = "text",
                HasUnreadMessages = hasUnread,
                Partner = currentPartner
            };

            try
            {
                // Gửi sự kiện cập nhật danh sách chat
                await Clients.User(participant.MaNguoiDung)
                    .SendAsync("CapNhatCuocTroChuyen", payloadForUser);
            }
            catch { /* Bỏ qua nếu user offline */ }
        }
    }

    // File: SocialChatHub.cs

    public async Task MarkAsSeen(string maCuocTroChuyen)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return;

        var messagesToUpdate = await _context.TinNhanSocials
            .Where(m => m.MaCuocTroChuyen == maCuocTroChuyen &&
                        m.MaNguoiGui != userId &&
                        !m.DaXem)
            .ToListAsync();
        if (messagesToUpdate.Any())
        {
            foreach (var msg in messagesToUpdate)
            {
                msg.DaXem = true;
            }
            await _context.SaveChangesAsync();
        }
        await Clients.GroupExcept(maCuocTroChuyen, Context.ConnectionId).SendAsync("MessagesHaveBeenSeen", new
        {
            MaCuocTroChuyen = maCuocTroChuyen,
            SeenBy = userId
        });
    }
}

