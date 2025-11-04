using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Hubs;
using UniMarket.Models;
using UniMarket.Helpers;
using System.Linq;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using YourNamespace.Helpers;       // Giữ nguyên
using YourProjectName.Helpers;   // Giữ nguyên
using System.Text.RegularExpressions; // ✅ 1. THÊM DÒNG NÀY ĐỂ SỬA LỖI REGEX

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SocialShareController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<SocialChatHub> _socialHubContext;

        public SocialShareController(ApplicationDbContext context, IHubContext<SocialChatHub> socialHubContext)
        {
            _context = context;
            _socialHubContext = socialHubContext;
        }

        // File: UniMarket/Controllers/SocialShareController.cs

        [Authorize]
        [HttpPost("share-to-friends")]
        public async Task<IActionResult> ShareToFriends([FromBody] ShareToFriendsRequest req)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Unauthorized(new { message = "Bạn cần đăng nhập." });
            }

            if (req.TargetUserIds == null || !req.TargetUserIds.Any())
            {
                return BadRequest(new { message = "Vui lòng chọn ít nhất một người nhận." });
            }

            if (req.ChatType == ChatType.BanHang)
            {
                return BadRequest(new { message = "Chat bán hàng không hỗ trợ tính năng này." });
            }

            var createdResults = new List<object>();

            var senderInfo = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.FullName, u.AvatarUrl })
                .FirstOrDefaultAsync();

            if (senderInfo == null)
            {
                return Unauthorized(new { message = "Không tìm thấy thông tin người gửi." });
            }

            foreach (var targetId in req.TargetUserIds.Distinct())
            {
                if (targetId == userId) continue;

                try
                {
                    // --- 1. Tìm hoặc tạo cuộc trò chuyện (LOGIC CŨ CỦA BẠN - GIỮ NGUYÊN) ---
                    var conversation = await _context.CuocTroChuyenSocials
                        .Include(c => c.NguoiThamGias)
                        .FirstOrDefaultAsync(c =>
                            c.NguoiThamGias.Count == 2 &&
                            c.NguoiThamGias.Any(n => n.MaNguoiDung == userId) &&
                            c.NguoiThamGias.Any(n => n.MaNguoiDung == targetId));

                    if (conversation != null)
                    {
                        conversation.NgayCapNhat = DateTime.UtcNow;
                        conversation.IsEmpty = false;
                    }
                    else
                    {
                        conversation = new CuocTroChuyenSocial
                        {
                            ThoiGianTao = DateTime.UtcNow,
                            IsEmpty = false,
                            NgayCapNhat = DateTime.UtcNow,
                            NguoiThamGias = new List<NguoiThamGiaSocial>
                            {
                                new NguoiThamGiaSocial { MaNguoiDung = userId },
                                new NguoiThamGiaSocial { MaNguoiDung = targetId }
                            }
                        };
                        _context.CuocTroChuyenSocials.Add(conversation);
                    }

                    // ================================================================
                    // ✨✨ BẮT ĐẦU SỬA LỖI ✨✨
                    // --- 2. Xử lý logic ẩn/hiện (ĐÃ SỬA LỖI) ---
                    // ================================================================
                    var allHiddenEntries = await _context.UserHiddenConversations
                        .Where(h => h.MaCuocTroChuyen == conversation.MaCuocTroChuyen)
                        .ToListAsync();

                    foreach (var entry in allHiddenEntries)
                    {
                        // Logic mới: Luôn set HasReappeared = true cho cả hai
                        // thay vì xóa của người gửi.
                        // Điều này đồng bộ logic với SocialChatHub.cs
                        entry.HasReappeared = true;
                    }
                    // ================================================================
                    // ✨✨ KẾT THÚC SỬA LỖI ✨✨
                    // ================================================================

                    // --- 3. Tạo bản ghi Share (LOGIC CŨ CỦA BẠN - GIỮ NGUYÊN) ---
                    var previewImage = req.PreviewImage;
                    if (string.IsNullOrEmpty(previewImage) && !string.IsNullOrEmpty(req.PreviewVideo) && req.PreviewVideo.Contains("cloudinary"))
                    {
                        int lastDotIndex = req.PreviewVideo.LastIndexOf('.');
                        if (lastDotIndex != -1)
                        {
                            previewImage = req.PreviewVideo.Substring(0, lastDotIndex) + ".jpg";
                        }
                    }

                    var share = new Share
                    {
                        UserId = userId,
                        ShareType = ShareType.Chat,
                        TargetType = req.DisplayMode == ShareDisplayMode.Video ? ShareTargetType.Video : ShareTargetType.TinDang,
                        DisplayMode = req.DisplayMode,
                        TinDangId = req.TinDangId,
                        MaCuocTroChuyen = conversation.MaCuocTroChuyen,
                        ShareLink = req.TinDangId.HasValue ? $"/tin/{req.TinDangId}" : req.PreviewVideo,
                        SharedAt = DateTime.UtcNow,
                        PreviewTitle = req.PreviewTitle,
                        PreviewImage = previewImage,
                        PreviewVideo = req.PreviewVideo
                    };
                    _context.Shares.Add(share);
                    await _context.SaveChangesAsync(); // Lưu share để lấy ShareId

                    // --- 4. Tạo tin nhắn (LOGIC CŨ CỦA BẠN - GIỮ NGUYÊN) ---
                    var tin = new TinNhanSocial
                    {
                        MaCuocTroChuyen = conversation.MaCuocTroChuyen,
                        MaNguoiGui = userId,
                        NoiDung = $"[ShareId:{share.ShareId}:video] {req.ExtraText ?? ""}".Trim(),
                        MediaUrl = null,
                        ThoiGianGui = share.SharedAt,
                        DaXem = false
                    };
                    _context.TinNhanSocials.Add(tin);
                    await _context.SaveChangesAsync(); // Lưu tin nhắn để lấy MaTinNhan

                    // --- 5. Gửi Real-time (LOGIC CŨ CỦA BẠN - GIỮ NGUYÊN) ---

                    // 5.1. Tạo DTO đầy đủ cho `ReceiveMessage`
                    var messageDto = new
                    {
                        MaTinNhan = tin.MaTinNhan,
                        MaCuocTroChuyen = tin.MaCuocTroChuyen,
                        MaNguoiGui = tin.MaNguoiGui,
                        NoiDung = tin.NoiDung,
                        MediaUrl = tin.MediaUrl,
                        ThoiGianGui = tin.ThoiGianGui.ToString("O"),
                        Sender = senderInfo,
                        Share = new
                        {
                            share.ShareId,
                            share.PreviewTitle,
                            share.PreviewImage,
                            share.PreviewVideo,
                            share.ShareLink,
                            TargetType = (int)share.TargetType
                        }
                    };
                    await _socialHubContext.Clients.Group(conversation.MaCuocTroChuyen).SendAsync("ReceiveMessage", messageDto);

                    // 5.2. Tạo DTO đầy đủ cho `CapNhatCuocTroChuyen`
                    var receiverInfo = await _context.Users
                        .AsNoTracking()
                        .Where(u => u.Id == targetId)
                        .Select(u => new { u.Id, u.FullName, u.AvatarUrl })
                        .FirstOrDefaultAsync();

                    var updatePayloadForReceiver = new
                    {
                        MaCuocTroChuyen = conversation.MaCuocTroChuyen,
                        TinNhanCuoi = tin.NoiDung,
                        ThoiGianCapNhat = tin.ThoiGianGui,
                        NguoiGuiId = userId,
                        MessageType = "video",
                        Partner = senderInfo,
                        HasUnreadMessages = true
                    };

                    var updatePayloadForSender = new
                    {
                        MaCuocTroChuyen = conversation.MaCuocTroChuyen,
                        TinNhanCuoi = tin.NoiDung,
                        ThoiGianCapNhat = tin.ThoiGianGui,
                        NguoiGuiId = userId,
                        MessageType = "video",
                        Partner = receiverInfo,
                        HasUnreadMessages = false
                    };

                    await _socialHubContext.Clients.User(targetId).SendAsync("CapNhatCuocTroChuyen", updatePayloadForReceiver);
                    await _socialHubContext.Clients.User(userId).SendAsync("CapNhatCuocTroChuyen", updatePayloadForSender);

                    createdResults.Add(new { targetId, conversationId = conversation.MaCuocTroChuyen, shareId = share.ShareId });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"--> FATAL ERROR sharing to {targetId}: {ex.ToString()}");
                }
            }

            if (createdResults.Count == 0 && req.TargetUserIds.Any())
            {
                return StatusCode(500, new { message = "Không thể gửi tin nhắn cho bất kỳ ai." });
            }

            return Ok(new { success = true, created = createdResults });
        }


        // ============================
        // 0) Lấy danh sách bạn bè (unchanged)
        // ============================
        [Authorize]
        [HttpGet("friends/list")]
        public async Task<IActionResult> GetFriendsList()
        {
            // ... (Code của bạn ở đây giữ nguyên, không có lỗi) ...
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var friends = await _context.Users
                .Where(u =>
                    _context.Follows.Any(f => f.FollowerId == userId && f.FollowingId == u.Id) ||
                    _context.Follows.Any(f => f.FollowerId == u.Id && f.FollowingId == userId)
                )
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.AvatarUrl,
                    IsFollowing = _context.Follows.Any(f => f.FollowerId == userId && f.FollowingId == u.Id),
                    IsFollower = _context.Follows.Any(f => f.FollowerId == u.Id && f.FollowingId == userId)
                })
                .ToListAsync();

            return Ok(friends);
        }

        // ============================
        // 2) Lấy danh sách Social Chats (sửa để trả MessageType)
        // ============================
        // File: SocialShareController.cs
        [Authorize]
        [HttpGet("social/user/{userId}")]
        public async Task<IActionResult> GetSocialConversations(string userId)
        {
            // 🔹 Kiểm tra người dùng hợp lệ
            if (!await _context.Users.AnyAsync(u => u.Id == userId))
                return NotFound(new { message = "Người dùng không tồn tại" });

            // 🔹 Lấy danh sách cuộc trò chuyện
            var conversations = await _context.CuocTroChuyenSocials
                .AsNoTracking()
                .Where(c =>
                    !c.IsBlocked &&
                    c.NguoiThamGias.Any(n => n.MaNguoiDung == userId)
                )
                .Select(c => new
                {
                    c.MaCuocTroChuyen,
                    c.ThoiGianTao,

                    LastMessage = c.TinNhans
                        .OrderByDescending(t => t.ThoiGianGui)
                        .Select(t => new
                        {
                            t.NoiDung,
                            t.MediaUrl,
                            t.ThoiGianGui,
                            Sender = new
                            {
                                t.Sender.Id,
                                t.Sender.FullName,
                                t.Sender.AvatarUrl
                            }
                        })
                        .FirstOrDefault(),

                    Partner = c.NguoiThamGias
                        .Where(n => n.MaNguoiDung != userId)
                        .Select(n => new
                        {
                            n.User.Id,
                            n.User.FullName,
                            n.User.AvatarUrl
                        })
                        .FirstOrDefault(),

                    // ✅ Đếm số tin chưa đọc
                    UnreadCount = c.TinNhans.Count(m => m.MaNguoiGui != userId && !m.DaXem),

                    // ✅ LOGIC ISHIDDEN MỚI (chỉ ẩn khi chưa “hiện lại”)
                    IsHidden = _context.UserHiddenConversations
                        .Any(h => h.UserId == userId &&
                                  h.MaCuocTroChuyen == c.MaCuocTroChuyen &&
                                  !h.HasReappeared)
                })
                // ✅ Lọc bỏ các cuộc hội thoại rác
                .Where(c => c.Partner != null && c.LastMessage != null)

                // ✅ Zalo Mode: Nếu bị ẩn thì chỉ hiện lại khi có tin mới (UnreadCount > 0)
                .Where(c => !c.IsHidden || c.UnreadCount > 0)

                .OrderByDescending(c => c.LastMessage.ThoiGianGui)
                .ToListAsync();


            // 🔹 Lấy ShareId trong tin nhắn
            var shareIds = conversations
                .Select(c =>
                {
                    var msg = c.LastMessage?.NoiDung;
                    if (string.IsNullOrEmpty(msg)) return -1;
                    var match = Regex.Match(msg, @"\[ShareId:(\d+)\]");
                    return match.Success ? int.Parse(match.Groups[1].Value) : -1;
                })
                .Where(id => id != -1)
                .Distinct()
                .ToList();

            // 🔹 Lấy thông tin Share nếu có
            var sharesInfo = shareIds.Any()
                ? await _context.Shares
                    .Where(s => shareIds.Contains(s.ShareId))
                    .ToDictionaryAsync(s => s.ShareId, s => s.TargetType)
                : new Dictionary<int, ShareTargetType>();

            // 🔹 Chuẩn hóa dữ liệu gửi về FE
            var result = conversations.Select(c =>
            {
                var lastMessage = c.LastMessage;
                string messageType = "text";
                string displayText = lastMessage.NoiDung ?? "";

                if (!string.IsNullOrEmpty(lastMessage.MediaUrl))
                {
                    messageType = UrlHelpers.IsVideoUrl(lastMessage.MediaUrl) ? "video" : "image";
                    displayText = messageType == "video" ? "đã gửi 1 video" : "đã gửi 1 ảnh";
                }
                else if (displayText.StartsWith("[ShareId:"))
                {
                    var match = Regex.Match(displayText, @"\[ShareId:(\d+)\]");
                    if (match.Success &&
                        int.TryParse(match.Groups[1].Value, out int shareId) &&
                        sharesInfo.TryGetValue(shareId, out var targetType))
                    {
                        messageType = targetType == ShareTargetType.Video ? "video" : "share";
                        displayText = messageType == "video" ? "đã chia sẻ 1 video" : "đã chia sẻ 1 bài viết";
                    }
                }

                return new
                {
                    c.MaCuocTroChuyen,
                    c.ThoiGianTao,
                    LastMessage = new
                    {
                        NoiDung = displayText,
                        lastMessage.MediaUrl,
                        ThoiGianGui = lastMessage.ThoiGianGui.ToString("O"),
                        MessageType = messageType,
                        LoaiTinNhan = messageType,
                        lastMessage.Sender
                    },
                    c.Partner,
                    UnreadCount = c.UnreadCount
                };
            });

            return Ok(result);
        }

        // xóa ẩn tin nhắn
        [Authorize]
        [HttpDelete("conversation/{maCuocTroChuyen}")]
        public async Task<IActionResult> HideConversation(string maCuocTroChuyen)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." });

            // ✅ Kiểm tra người dùng có thuộc cuộc trò chuyện này không
            var isParticipant = await _context.CuocTroChuyenSocials
                .AsNoTracking()
                .AnyAsync(c =>
                    c.MaCuocTroChuyen == maCuocTroChuyen &&
                    c.NguoiThamGias.Any(n => n.MaNguoiDung == userId)
                );

            if (!isParticipant)
                return NotFound(new { message = "Không tìm thấy hoặc bạn không thuộc cuộc trò chuyện này." });

            // ✅ Kiểm tra xem đã ẩn chưa
            var hiddenEntry = await _context.UserHiddenConversations
                .FirstOrDefaultAsync(h => h.UserId == userId && h.MaCuocTroChuyen == maCuocTroChuyen);

            if (hiddenEntry == null)
            {
                // ⭐ Nếu CHƯA ẩn → THÊM MỚI
                _context.UserHiddenConversations.Add(new UserHiddenConversation
                {
                    UserId = userId,
                    MaCuocTroChuyen = maCuocTroChuyen,
                    ThoiGianAn = DateTime.UtcNow,   // Lưu lại thời gian ẩn
                    HasReappeared = false           // ⭐ BẮT BUỘC có
                });
            }
            else
            {
                // ⭐ Nếu ĐÃ ẩn → CẬP NHẬT LẠI
                hiddenEntry.ThoiGianAn = DateTime.UtcNow;
                hiddenEntry.HasReappeared = false; // ⭐ Reset cờ khi ẩn lại
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "✅ Cuộc trò chuyện đã được ẩn thành công." });
        }

        // ============================
        // 3) Lấy lịch sử tin nhắn (BẢN GỘP HOÀN CHỈNH)
        // ============================
        [Authorize]
        [HttpGet("social/history/{maCuocTroChuyen}")]
        public async Task<IActionResult> GetSocialHistory(
            string maCuocTroChuyen,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 30,
            [FromQuery] DateTime? sessionTimestamp = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." });

            // ==================== 1️⃣ XÁC ĐỊNH THỜI GIAN LỌC ====================
            DateTime? filterTime = null;
            if (sessionTimestamp.HasValue)
            {
                filterTime = sessionTimestamp.Value;
            }
            else
            {
                var hiddenInfo = await _context.UserHiddenConversations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(h => h.UserId == userId && h.MaCuocTroChuyen == maCuocTroChuyen);
                filterTime = hiddenInfo?.ThoiGianAn;
            }

            // ==================== 2️⃣ TRUY VẤN TIN NHẮN ====================
            var query = _context.TinNhanSocials
                .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen)
                // Bỏ qua tin nhắn user đã xóa riêng cho mình
                .Where(t => !t.DeletedForUsers.Any(d => d.UserId == userId));

            if (filterTime.HasValue)
            {
                query = query.Where(t => t.ThoiGianGui > filterTime.Value);
            }

            var totalMessages = await query.CountAsync();

            var messages = await query
                .Include(t => t.Sender)
                .Include(t => t.ParentMessage)
                    .ThenInclude(p => p.Sender)
                .OrderByDescending(t => t.ThoiGianGui)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Đảo ngược để hiển thị theo thời gian tăng dần
            messages.Reverse();

            // ==================== 3️⃣ XỬ LÝ SHARE LINK ====================

            // --- Lấy ShareId từ tin nhắn chính ---
            var mainShareIds = messages
                .Select(m => Regex.Match(m.NoiDung ?? "", @"\[ShareId:(\d+):?.*?\]"))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value));

            // --- Lấy ShareId từ tin nhắn CHA (nếu có) ---
            var parentShareIds = messages
                .Where(m => m.ParentMessage != null)
                .Select(m => Regex.Match(m.ParentMessage.NoiDung ?? "", @"\[ShareId:(\d+):?.*?\]"))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value));

            // --- Gộp và loại bỏ trùng ---
            var shareIds = mainShareIds.Concat(parentShareIds).Distinct().ToList();

            // --- Lấy toàn bộ thông tin Share 1 lần ---
            var sharesInfo = new Dictionary<int, Share>();
            if (shareIds.Any())
            {
                sharesInfo = await _context.Shares
                    .Where(s => shareIds.Contains(s.ShareId))
                    .ToDictionaryAsync(s => s.ShareId);
            }

            // ==================== 4️⃣ XÂY DỰNG RESPONSE ====================
            var resultMessages = messages.Select(t =>
            {
                // ===== Tin nhắn chính =====
                int mainShareId = -1;
                string extraText = t.NoiDung;
                var mainMatch = Regex.Match(t.NoiDung ?? "", @"\[ShareId:(\d+):?.*?\](.*)");
                if (mainMatch.Success)
                {
                    mainShareId = int.Parse(mainMatch.Groups[1].Value);
                    extraText = mainMatch.Groups[2].Value.Trim();
                }

                // ===== Tin nhắn cha (nếu có) =====
                Share parentShareInfo = null;
                if (t.ParentMessage != null)
                {
                    var parentMatch = Regex.Match(t.ParentMessage.NoiDung ?? "", @"\[ShareId:(\d+):?.*?\]");
                    if (parentMatch.Success && int.TryParse(parentMatch.Groups[1].Value, out int parentShareId))
                    {
                        sharesInfo.TryGetValue(parentShareId, out parentShareInfo);
                    }
                }

                return new
                {
                    MaTinNhan = t.MaTinNhan,
                    MaNguoiGui = t.MaNguoiGui,
                    NoiDung = extraText,
                    MediaUrl = t.MediaUrl,
                    ThoiGianGui = t.ThoiGianGui,
                    IsRecalled = t.IsRecalled,

                    // ===== Người gửi =====
                    Sender = t.Sender == null ? null : new
                    {
                        Id = t.Sender.Id,
                        FullName = t.Sender.FullName,
                        AvatarUrl = t.Sender.AvatarUrl
                    },

                    // ===== Tin nhắn được trả lời (nếu có) =====
                    ParentMessage = t.ParentMessage == null ? null : new
                    {
                        MaTinNhan = t.ParentMessage.MaTinNhan,
                        NoiDung = t.ParentMessage.IsRecalled
                            ? "[Tin nhắn đã thu hồi]"
                            : (t.ParentMessage.NoiDung ?? ""),
                        MediaUrl = t.ParentMessage.IsRecalled
                            ? null
                            : t.ParentMessage.MediaUrl,
                        MaNguoiGui = t.ParentMessage.MaNguoiGui,
                        SenderFullName = t.ParentMessage.Sender?.FullName,
                        IsRecalled = t.ParentMessage.IsRecalled,

                        // ✅ Đính kèm Share của tin nhắn cha
                        Share = parentShareInfo == null ? null : new
                        {
                            parentShareInfo.ShareId,
                            parentShareInfo.PreviewTitle,
                            parentShareInfo.PreviewImage,
                            parentShareInfo.PreviewVideo,
                            parentShareInfo.ShareLink,
                            TargetType = (int)parentShareInfo.TargetType
                        }
                    },

                    // ✅ Đính kèm Share của tin nhắn chính
                    Share = mainShareId != -1 && sharesInfo.ContainsKey(mainShareId)
                        ? new
                        {
                            sharesInfo[mainShareId].ShareId,
                            sharesInfo[mainShareId].PreviewTitle,
                            sharesInfo[mainShareId].PreviewImage,
                            sharesInfo[mainShareId].PreviewVideo,
                            sharesInfo[mainShareId].ShareLink,
                            TargetType = (int)sharesInfo[mainShareId].TargetType
                        }
                        : null
                };
            }).ToList();

            // ==================== 5️⃣ PHÂN TRANG + TRẢ VỀ ====================
            var response = new
            {
                Messages = resultMessages,
                TotalMessages = totalMessages,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalMessages / (double)pageSize),
                SessionTimestamp = filterTime
            };

            return Ok(response);
        }



        // ============================
        // [MỚI] 4) Lấy trạng thái hoạt động của user
        // ============================
        [Authorize]
        [HttpGet("activity/{userId}")]
        public async Task<IActionResult> GetUserActivity(string userId)
        {
            // ... (Code của bạn ở đây giữ nguyên, không có lỗi) ...
            var activity = await _context.UserActivities.FindAsync(userId);
            if (activity == null)
            {
                // Nếu không có, mặc định là offline và lastActive là null
                return Ok(new { UserId = userId, IsOnline = false, LastActive = (DateTime?)null });
            }
            return Ok(new { activity.UserId, activity.IsOnline, activity.LastActive });
        }
        [Authorize]
        [HttpPost("message/delete-for-me")]
        public async Task<IActionResult> DeleteMessageForMe([FromBody] DeleteMessageRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var message = await _context.TinNhanSocials
                .FirstOrDefaultAsync(m => m.MaTinNhan == request.MessageId && m.MaCuocTroChuyen == request.ConversationId);

            if (message == null) return NotFound();

            // Không cho xóa tin nhắn của chính mình bằng cách này
            if (message.MaNguoiGui == userId)
                return BadRequest(new { message = "Sử dụng chức năng thu hồi để xóa tin nhắn của bạn." });

            // Kiểm tra xem đã xóa trước đó chưa
            var alreadyDeleted = await _context.DeletedMessagesForUsers
                .AnyAsync(d => d.UserId == userId && d.TinNhanSocialId == request.MessageId);

            if (!alreadyDeleted)
            {
                _context.DeletedMessagesForUsers.Add(new DeletedMessageForUser
                {
                    UserId = userId,
                    TinNhanSocialId = request.MessageId
                });
                await _context.SaveChangesAsync();
            }

            // Gửi sự kiện real-time về cho chính user đó để UI cập nhật
            await _socialHubContext.Clients.User(userId).SendAsync("MessageRemovedForMe", new
            {
                MaTinNhan = request.MessageId,
                MaCuocTroChuyen = request.ConversationId
            });

            return Ok(new { success = true });
        }
    }
}