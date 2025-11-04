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

[Authorize]
public class SocialChatHub : Hub
{
    private readonly ApplicationDbContext _context;

    public SocialChatHub(ApplicationDbContext context)
    {
        _context = context;
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

        // Nếu noiDung chứa một hint dạng [ShareId:xxx:video] hoặc có từ khóa video
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
    public async Task SendMessage(string maCuocTroChuyen, string content, string? mediaUrl, string? parentMessageId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return;

        var sender = await _context.Users.FindAsync(userId);
        if (sender == null) return;

        var convo = await _context.CuocTroChuyenSocials
            .Include(c => c.NguoiThamGias)
                .ThenInclude(n => n.User)
            .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

        if (convo == null) return;
        if (convo.IsBlocked && convo.MaNguoiChan != userId) return;

        // --- Tạo tin nhắn mới ---
        var msg = new TinNhanSocial
        {
            MaCuocTroChuyen = maCuocTroChuyen,
            MaNguoiGui = userId,
            NoiDung = content,
            MediaUrl = mediaUrl,
            ThoiGianGui = DateTime.UtcNow,
            DaXem = false,
            ParentMessageId = parentMessageId // ✨ thêm parent message id
        };

        _context.TinNhanSocials.Add(msg);
        convo.IsEmpty = false;
        convo.NgayCapNhat = DateTime.UtcNow;

        // ======================= ẨN/HIỆN CHAT =======================
        var senderHidden = await _context.UserHiddenConversations
            .FirstOrDefaultAsync(h => h.UserId == userId && h.MaCuocTroChuyen == maCuocTroChuyen);
        if (senderHidden != null) senderHidden.HasReappeared = true;

        var receiver = convo.NguoiThamGias.FirstOrDefault(p => p.MaNguoiDung != userId);
        if (receiver != null)
        {
            var receiverHidden = await _context.UserHiddenConversations
                .FirstOrDefaultAsync(h => h.UserId == receiver.MaNguoiDung && h.MaCuocTroChuyen == maCuocTroChuyen);
            if (receiverHidden != null) receiverHidden.HasReappeared = true;
        }

        await _context.SaveChangesAsync(); // Lưu để lấy MaTinNhan

        // ======================= LẤY TIN NHẮN CHA (NẾU CÓ) =======================
        TinNhanSocial? parentMessage = null;
        if (!string.IsNullOrEmpty(parentMessageId))
        {
            parentMessage = await _context.TinNhanSocials
                .Include(p => p.Sender)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.MaTinNhan == parentMessageId);
        }

        // ======================= SHARE CỦA TIN NHẮN CHA =======================
        object parentShareInfo = null;
        if (parentMessage != null && !parentMessage.IsRecalled)
        {
            var parentMatch = System.Text.RegularExpressions.Regex.Match(parentMessage.NoiDung ?? "", @"\[ShareId:(\d+):?.*?\]");
            if (parentMatch.Success && int.TryParse(parentMatch.Groups[1].Value, out int parentShareId))
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
                        TargetType = (int)share.TargetType
                    };
                }
            }
        }

        // ======================= SHARE CỦA TIN NHẮN CHÍNH =======================
        object mainShareInfo = null;
        var mainMatch = System.Text.RegularExpressions.Regex.Match(msg.NoiDung ?? "", @"\[ShareId:(\d+):?.*?\]");
        if (mainMatch.Success && int.TryParse(mainMatch.Groups[1].Value, out int mainShareId))
        {
            var mainShare = await _context.Shares
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ShareId == mainShareId);
            if (mainShare != null)
            {
                mainShareInfo = new
                {
                    mainShare.ShareId,
                    mainShare.PreviewTitle,
                    mainShare.PreviewImage,
                    mainShare.PreviewVideo,
                    mainShare.ShareLink,
                    TargetType = (int)mainShare.TargetType
                };
            }
        }

        // ======================= CHUẨN BỊ GỬI TIN NHẮN REAL-TIME =======================
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

        await Clients.Group(maCuocTroChuyen).SendAsync("ReceiveMessage", messageDto);

        // ======================= GỬI CẬP NHẬT HỘI THOẠI =======================
        var partnerParticipant = convo.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung != userId);
        object partnerDto = null;
        if (partnerParticipant != null)
        {
            var pUser = partnerParticipant.User;
            partnerDto = new
            {
                Id = pUser?.Id ?? partnerParticipant.MaNguoiDung,
                FullName = pUser?.FullName,
                AvatarUrl = pUser?.AvatarUrl
            };
        }

        foreach (var participant in convo.NguoiThamGias)
        {
            var currentPartner = (participant.MaNguoiDung == userId)
                ? partnerDto
                : new { Id = sender.Id, FullName = sender.FullName, AvatarUrl = sender.AvatarUrl };

            var hasUnread = participant.MaNguoiDung != userId;

            var payloadForUser = new
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
                HasUnreadMessages = hasUnread,
                Partner = currentPartner
            };

            try
            {
                await Clients.User(participant.MaNguoiDung)
                    .SendAsync("CapNhatCuocTroChuyen", payloadForUser);
            }
            catch
            {
                // Bỏ qua nếu user offline
            }
        }
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

        var msg = await _context.TinNhanSocials.FirstOrDefaultAsync(m =>
            m.MaTinNhan == maTinNhan && m.MaCuocTroChuyen == maCuocTroChuyen);

        if (msg == null || msg.MaNguoiGui != userId) return;

        // ✨ Cập nhật trạng thái thay vì xóa nội dung
        msg.IsRecalled = true;
        await _context.SaveChangesAsync();

        await Clients.Group(maCuocTroChuyen).SendAsync("MessageRecalled", new
        {
            MaTinNhan = msg.MaTinNhan,
            MaCuocTroChuyen = msg.MaCuocTroChuyen
        });
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

