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
        private readonly IHubContext<VideoHub> _videoHubContext;

        public SocialShareController(ApplicationDbContext context,
                                     IHubContext<SocialChatHub> socialHubContext,
                                     IHubContext<VideoHub> videoHubContext)
        {
            _context = context;
            _socialHubContext = socialHubContext;
            _videoHubContext = videoHubContext;
        }

        // File: UniMarket/Controllers/SocialShareController.cs

        [Authorize]
        [HttpPost("share-to-friends")]
        public async Task<IActionResult> ShareToFriends([FromBody] ShareToFriendsRequest req)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized(new { message = "Bạn cần đăng nhập." });

            if (req.TargetUserIds == null || !req.TargetUserIds.Any())
                return BadRequest(new { message = "Vui lòng chọn ít nhất một người nhận." });

            if (req.ChatType == ChatType.BanHang)
                return BadRequest(new { message = "Chat bán hàng không hỗ trợ tính năng này." });

            var createdResults = new List<object>();
            var skippedResults = new List<string>(); // ✨ Theo dõi người bị chặn hoặc lỗi

            var senderInfo = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.FullName, u.AvatarUrl })
                .FirstOrDefaultAsync();

            if (senderInfo == null)
                return Unauthorized(new { message = "Không tìm thấy thông tin người gửi." });

            // ==========================================================
            // Duyệt qua từng người nhận
            // ==========================================================
            foreach (var targetId in req.TargetUserIds.Distinct())
            {
                if (targetId == userId) continue;

                try
                {
                    // 1️⃣ Tìm hoặc tạo cuộc trò chuyện
                    var conversation = await _context.CuocTroChuyenSocials
                        .Include(c => c.NguoiThamGias)
                        .FirstOrDefaultAsync(c =>
                            c.NguoiThamGias.Count == 2 &&
                            c.NguoiThamGias.Any(n => n.MaNguoiDung == userId) &&
                            c.NguoiThamGias.Any(n => n.MaNguoiDung == targetId));

                    if (conversation == null)
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
                    else
                    {
                        conversation.NgayCapNhat = DateTime.UtcNow;
                        conversation.IsEmpty = false;
                    }

                    // ==========================================================
                    // 2️⃣ Kiểm tra trạng thái CHẶN
                    // ==========================================================
                    if (conversation.IsBlocked)
                    {
                        Console.WriteLine($"⚠️ Bỏ qua share tới {targetId}: cuộc trò chuyện {conversation.MaCuocTroChuyen} đang bị chặn.");
                        skippedResults.Add(targetId);
                        continue;
                    }

                    // ==========================================================
                    // 3️⃣ Cập nhật hội thoại bị ẩn (HasReappeared)
                    // ==========================================================
                    var allHiddenEntries = await _context.UserHiddenConversations
                        .Where(h => h.MaCuocTroChuyen == conversation.MaCuocTroChuyen)
                        .ToListAsync();

                    foreach (var entry in allHiddenEntries)
                        entry.HasReappeared = true;

                    // ==========================================================
                    // 4️⃣ Tạo bản ghi Share
                    // ==========================================================
                    var previewImage = req.PreviewImage;
                    if (string.IsNullOrEmpty(previewImage)
                        && !string.IsNullOrEmpty(req.PreviewVideo)
                        && req.PreviewVideo.Contains("cloudinary"))
                    {
                        int lastDotIndex = req.PreviewVideo.LastIndexOf('.');
                        if (lastDotIndex != -1)
                            previewImage = req.PreviewVideo.Substring(0, lastDotIndex) + ".jpg";
                    }

                    var share = new Share
                    {
                        UserId = userId,
                        ShareType = ShareType.Chat,
                        TargetType = req.DisplayMode == ShareDisplayMode.Video
                            ? ShareTargetType.Video
                            : ShareTargetType.TinDang,
                        DisplayMode = req.DisplayMode,
                        TinDangId = req.TinDangId,
                        MaCuocTroChuyen = conversation.MaCuocTroChuyen,
                        ShareLink = req.TinDangId.HasValue
                            ? $"/tin-dang/{req.TinDangId}"
                            : req.PreviewVideo,
                        SharedAt = DateTime.UtcNow,
                        PreviewTitle = req.PreviewTitle,
                        PreviewImage = previewImage,
                        PreviewVideo = req.PreviewVideo
                    };
                    _context.Shares.Add(share);
                    await _context.SaveChangesAsync();

                    // ==========================================================
                    // 5️⃣ Tạo tin nhắn chứa Share
                    // ==========================================================
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
                    await _context.SaveChangesAsync();

                    // ==========================================================
                    // 6️⃣ Gửi realtime tới cả hai phía
                    // ==========================================================
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
                            TargetType = (int)share.TargetType,
                            TinDangId = share.TinDangId
                        }
                    };

                    await _socialHubContext.Clients.Group(conversation.MaCuocTroChuyen)
                        .SendAsync("ReceiveMessage", messageDto);

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

                    await _socialHubContext.Clients.User(targetId)
                        .SendAsync("CapNhatCuocTroChuyen", updatePayloadForReceiver);
                    await _socialHubContext.Clients.User(userId)
                        .SendAsync("CapNhatCuocTroChuyen", updatePayloadForSender);

                    createdResults.Add(new
                    {
                        targetId,
                        conversationId = conversation.MaCuocTroChuyen,
                        shareId = share.ShareId
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Lỗi gửi share tới {targetId}: {ex}");
                    skippedResults.Add(targetId);
                }
            }

            // ✅✅ BƯỚC 4: THÊM LOGIC GỬI REAL-TIME (ĐẶT BÊN NGOÀI VÒNG LOOP)
            if (req.TinDangId.HasValue && createdResults.Count > 0)
            {
                var tinDangId = req.TinDangId.Value;

                // Đếm tổng số lượt share cho tin đăng này
                var totalShares = await _context.Shares
                    .CountAsync(s => s.TinDangId == tinDangId);

                // Gửi cập nhật real-time
                await _videoHubContext.Clients.Group(tinDangId.ToString())
                    .SendAsync("UpdateShareCount", tinDangId, totalShares);
            }

            // ==========================================================
            // 7️⃣ Trả về kết quả
            // ==========================================================
            var totalAttempted = req.TargetUserIds.Distinct().Count();

            if (createdResults.Count == 0 && totalAttempted > 0)
            {
                if (skippedResults.Count == totalAttempted)
                {
                    return BadRequest(new
                    {
                        message = "Không thể gửi: Bạn đã chặn người dùng này hoặc bị người dùng này chặn."
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        message = "Không thể gửi tin nhắn cho bất kỳ ai do lỗi hệ thống."
                    });
                }
            }

            return Ok(new
            {
                success = true,
                created = createdResults,
                skipped = skippedResults // ✨ thêm danh sách bị bỏ qua
            });
        }




        // ============================
        // 0) Lấy danh sách bạn bè (unchanged)
        // ============================
        [Authorize]
        [HttpGet("friends/list")]
        public async Task<IActionResult> GetFriendsList()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            // ----- BẮT ĐẦU TỐI ƯU -----

            // 1. Tạo một IQueryable (chưa thực thi) để lấy ID của những người BẠN ĐANG FOLLOW
            var followingIds = _context.Follows
                .Where(f => f.FollowerId == userId)
                .Select(f => f.FollowingId);

            // 2. Tạo một IQueryable (chưa thực thi) để lấy ID của những người ĐANG FOLLOW BẠN
            var followerIds = _context.Follows
                .Where(f => f.FollowingId == userId)
                .Select(f => f.FollowerId);

            // 3. Tạo một IQueryable gộp 2 danh sách ID ở trên
            var allFriendIds = followingIds.Union(followerIds);

            // 4. Query chính:
            // EF Core sẽ tự động biên dịch tất cả các IQueryable trên thành MỘT SQL
            var friends = await _context.Users
                .AsNoTracking() // Rất quan trọng: Tăng tốc độ đọc
                .Where(u => allFriendIds.Contains(u.Id)) // Chỉ lấy User có ID trong danh sách gộp
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.AvatarUrl,
                    // EF Core sẽ tự dịch các .Contains() này thành SQL JOINs hoặc EXISTS
                    IsFollowing = followingIds.Contains(u.Id),
                    IsFollower = followerIds.Contains(u.Id)
                })
                .ToListAsync();

            // ----- KẾT THÚC TỐI ƯU -----

            return Ok(friends);
        }
        // =========================================================
        // ✅ 2) Lấy danh sách Social Chats (Tối ưu + có trạng thái Block)
        // =========================================================
        [Authorize]
        [HttpGet("social/user/{userId}")]
        public async Task<IActionResult> GetSocialConversations(string userId)
        {
            // =========================================================
            // 🔐 Xác thực quyền truy cập
            // =========================================================
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != currentUserId)
                return Unauthorized(new { message = "Không được phép truy cập dữ liệu của người khác" });

            // 🔹 Kiểm tra user có tồn tại không
            if (!await _context.Users.AnyAsync(u => u.Id == userId))
                return NotFound(new { message = "Người dùng không tồn tại" });

            // =========================================================
            // 🧠 Lấy danh sách cuộc trò chuyện
            // =========================================================
            var conversations = await _context.CuocTroChuyenSocials
                .AsNoTracking()
                .Where(c => c.NguoiThamGias.Any(n => n.MaNguoiDung == userId))
                .Select(c => new
                {
                    c.MaCuocTroChuyen,
                    c.ThoiGianTao,
                    c.IsBlocked,
                    c.MaNguoiChan,

                    // ✨ [NEW] Trạng thái Mute của user hiện tại
                    IsMuted = c.NguoiThamGias
                                .Where(n => n.MaNguoiDung == userId)
                                .Select(n => n.IsMuted)
                                .FirstOrDefault(),

                    // Tin nhắn cuối
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

                    // Partner
                    Partner = c.NguoiThamGias
                        .Where(n => n.MaNguoiDung != userId)
                        .Select(n => new
                        {
                            n.User.Id,
                            n.User.FullName,
                            n.User.AvatarUrl
                        })
                        .FirstOrDefault(),

                    // Chưa đọc
                    UnreadCount = c.TinNhans.Count(m => m.MaNguoiGui != userId && !m.DaXem),

                    // Ẩn tin nhắn
                    IsHidden = _context.UserHiddenConversations
                        .Any(h => h.UserId == userId &&
                                  h.MaCuocTroChuyen == c.MaCuocTroChuyen &&
                                  !h.HasReappeared)
                })
                .Where(c => c.Partner != null && c.LastMessage != null)
                .Where(c => !c.IsHidden || c.UnreadCount > 0)
                .OrderByDescending(c => c.LastMessage.ThoiGianGui)
                .ToListAsync();

            // =========================================================
            // 📎 Lấy danh sách ShareId từ tin nhắn
            // =========================================================
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

            // Lấy thông tin share 1 lần
            var sharesInfo = shareIds.Any()
                ? await _context.Shares
                    .Where(s => shareIds.Contains(s.ShareId))
                    .ToDictionaryAsync(s => s.ShareId, s => s.TargetType)
                : new Dictionary<int, ShareTargetType>();

            // =========================================================
            // ✨ Chuẩn hóa dữ liệu trả về (format)
            // =========================================================
            var result = conversations.Select(c =>
            {
                var lastMessage = c.LastMessage;
                string messageType = "text";
                string displayText = lastMessage.NoiDung ?? "";

                // 🎥 Nếu có MediaUrl → ảnh/video
                if (!string.IsNullOrEmpty(lastMessage.MediaUrl))
                {
                    messageType = UrlHelpers.IsVideoUrl(lastMessage.MediaUrl) ? "video" : "image";
                    displayText = messageType == "video" ? "đã gửi 1 video" : "đã gửi 1 ảnh";
                }
                // 🔗 Nếu là share dạng [ShareId:xxx]
                else if (displayText.StartsWith("[ShareId:"))
                {
                    var match = Regex.Match(displayText, @"\[ShareId:(\d+)\]");
                    if (match.Success &&
                        int.TryParse(match.Groups[1].Value, out int shareId) &&
                        sharesInfo.TryGetValue(shareId, out var targetType))
                    {
                        messageType = targetType == ShareTargetType.Video ? "video" : "share";
                        displayText = messageType == "video"
                            ? "đã chia sẻ 1 video"
                            : "đã chia sẻ 1 bài viết";
                    }
                }

                return new
                {
                    c.MaCuocTroChuyen,
                    c.ThoiGianTao,
                    c.IsBlocked,
                    c.MaNguoiChan,
                    c.IsMuted, // ✨ NEW

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
                    c.UnreadCount
                };
            });

            return Ok(result);
        }

        // =========================================================
        // ✨ [MỚI] API TẮT/BẬT THÔNG BÁO
        // =========================================================

        // Helper gửi Realtime (chỉ gửi cho user thực hiện)
        private async Task NotifyMuteStatusChanged(string userId, string maCuocTroChuyen, bool isMuted)
        {
            var payload = new
            {
                maCuocTroChuyen = maCuocTroChuyen,
                isMuted = isMuted
            };
            await _socialHubContext.Clients.User(userId)
                .SendAsync("MuteStatusChanged", payload);
        }

        [Authorize]
        [HttpPost("conversation/{maCuocTroChuyen}/mute")]
        public async Task<IActionResult> MuteConversation(string maCuocTroChuyen)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var participant = await _context.NguoiThamGiaSocials
                .FirstOrDefaultAsync(n => n.MaCuocTroChuyen == maCuocTroChuyen && n.MaNguoiDung == userId);

            if (participant == null) return NotFound();
            if (participant.IsMuted) return BadRequest(new { message = "Cuộc trò chuyện đã được tắt thông báo." });

            participant.IsMuted = true;
            await _context.SaveChangesAsync();

            // Gửi sự kiện Real-time
            await NotifyMuteStatusChanged(userId, maCuocTroChuyen, true);

            return Ok(new { message = "Đã tắt thông báo." });
        }

        [Authorize]
        [HttpPost("conversation/{maCuocTroChuyen}/unmute")]
        public async Task<IActionResult> UnmuteConversation(string maCuocTroChuyen)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var participant = await _context.NguoiThamGiaSocials
                .FirstOrDefaultAsync(n => n.MaCuocTroChuyen == maCuocTroChuyen && n.MaNguoiDung == userId);

            if (participant == null) return NotFound();
            if (!participant.IsMuted) return BadRequest(new { message = "Cuộc trò chuyện đang bật thông báo." });

            participant.IsMuted = false;
            await _context.SaveChangesAsync();

            // Gửi sự kiện Real-time
            await NotifyMuteStatusChanged(userId, maCuocTroChuyen, false);

            return Ok(new { message = "Đã bật lại thông báo." });
        }


        // =========================================================
        // ✨ [MỚI] API CHẶN VÀ GỠ CHẶN
        // =========================================================

        private async Task NotifyBlockStatusChanged(CuocTroChuyenSocial conversation)
        {
            var payload = new
            {
                maCuocTroChuyen = conversation.MaCuocTroChuyen,
                isBlocked = conversation.IsBlocked,
                maNguoiChan = conversation.MaNguoiChan
            };

            // Gửi sự kiện cho cả 2 người trong nhóm
            foreach (var participant in conversation.NguoiThamGias)
            {
                await _socialHubContext.Clients.User(participant.MaNguoiDung)
                    .SendAsync("BlockStatusChanged", payload);
            }
        }
        [Authorize]
        [HttpPost("conversation/{maCuocTroChuyen}/block")]
        public async Task<IActionResult> BlockConversation(string maCuocTroChuyen)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var conversation = await _context.CuocTroChuyenSocials
                .Include(c => c.NguoiThamGias) // ✨ Cần Include NguoiThamGias
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen &&
                                          c.NguoiThamGias.Any(n => n.MaNguoiDung == userId));

            if (conversation == null) return NotFound();
            if (conversation.IsBlocked) return BadRequest(new { message = "Người dùng đã bị chặn." });

            conversation.IsBlocked = true;
            conversation.MaNguoiChan = userId;

            await _context.SaveChangesAsync();

            // ✨ Gửi sự kiện Real-time
            await NotifyBlockStatusChanged(conversation);

            return Ok(new { message = "Đã chặn người dùng." });
        }

        [Authorize]
        [HttpPost("conversation/{maCuocTroChuyen}/unblock")]
        public async Task<IActionResult> UnblockConversation(string maCuocTroChuyen)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var conversation = await _context.CuocTroChuyenSocials
                .Include(c => c.NguoiThamGias) // ✨ Cần Include NguoiThamGias
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen &&
                                          c.NguoiThamGias.Any(n => n.MaNguoiDung == userId));

            if (conversation == null) return NotFound();
            if (!conversation.IsBlocked) return BadRequest(new { message = "Người dùng không bị chặn." });

            // Chỉ người chặn mới được gỡ
            if (conversation.MaNguoiChan != userId)
            {
                return Forbid("Bạn không có quyền gỡ chặn người dùng này.");
            }

            conversation.IsBlocked = false;
            conversation.MaNguoiChan = null;

            await _context.SaveChangesAsync();

            // ✨ Gửi sự kiện Real-time
            await NotifyBlockStatusChanged(conversation);

            return Ok(new { message = "Đã gỡ chặn người dùng." });
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
                Share parentShareInfo = null; // Logic này giữ nguyên
                if (t.ParentMessage != null)
                {
                    var parentMatch = Regex.Match(t.ParentMessage.NoiDung ?? "", @"\[ShareId:(\d+):?.*?\]");
                    if (parentMatch.Success && int.TryParse(parentMatch.Groups[1].Value, out int parentShareId))
                    {
                        sharesInfo.TryGetValue(parentShareId, out parentShareInfo);
                    }
                }

                // ==================== 4️⃣ XÂY DỰNG RESPONSE ====================
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

                        // ✅ Đính kèm Share của tin nhắn cha (Đã an toàn)
                        Share = parentShareInfo == null ? null : new
                        {
                            parentShareInfo.ShareId,
                            parentShareInfo.PreviewTitle,
                            parentShareInfo.PreviewImage,
                            parentShareInfo.PreviewVideo,
                            parentShareInfo.ShareLink,
                            TargetType = (int)parentShareInfo.TargetType,
                            TinDangId = parentShareInfo.TinDangId // <-- Dòng bạn thêm
                        }
                    },

                    // ======================================================
                    // ✅ [SỬA LỖI] ĐÍNH KÈM SHARE CỦA TIN NHẮN CHÍNH
                    // ======================================================
                    Share = mainShareId != -1 && sharesInfo.TryGetValue(mainShareId, out var mainShareInfo) && mainShareInfo != null
                        ? new
                        {
                            mainShareInfo.ShareId,
                            mainShareInfo.PreviewTitle,
                            mainShareInfo.PreviewImage,
                            mainShareInfo.PreviewVideo,
                            mainShareInfo.ShareLink,
                            TargetType = (int)mainShareInfo.TargetType,
                            TinDangId = mainShareInfo.TinDangId // <-- Dòng bạn thêm
                        }
                        : null
                    // ======================================================
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