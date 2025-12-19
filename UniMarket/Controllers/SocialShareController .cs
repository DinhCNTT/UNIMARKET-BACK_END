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

        [Authorize]
        [HttpPost("start-conversation")]
        public async Task<IActionResult> StartConversation([FromBody] string targetUserId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized(new { message = "Bạn cần đăng nhập." });

            if (string.IsNullOrEmpty(targetUserId))
                return BadRequest(new { message = "ID người nhận không hợp lệ." });

            if (userId == targetUserId)
                return BadRequest(new { message = "Không thể nhắn tin cho chính mình." });

            try
            {
                // 1. Tìm cuộc trò chuyện riêng tư (2 người) đã tồn tại
                var conversation = await _context.CuocTroChuyenSocials
                    .Include(c => c.NguoiThamGias)
                    .ThenInclude(nt => nt.User) // Include User để lấy thông tin Partner trả về
                    .FirstOrDefaultAsync(c =>
                        c.NguoiThamGias.Count == 2 &&
                        c.NguoiThamGias.Any(n => n.MaNguoiDung == userId) &&
                        c.NguoiThamGias.Any(n => n.MaNguoiDung == targetUserId));

                // 2. Nếu chưa có, tạo mới
                if (conversation == null)
                {
                    conversation = new CuocTroChuyenSocial
                    {
                        ThoiGianTao = DateTime.UtcNow,
                        IsEmpty = true, // Mới tạo chưa có tin nhắn
                        NgayCapNhat = DateTime.UtcNow,
                        NguoiThamGias = new List<NguoiThamGiaSocial>
                {
                    new NguoiThamGiaSocial { MaNguoiDung = userId, IsMuted = false },
                    new NguoiThamGiaSocial { MaNguoiDung = targetUserId, IsMuted = false }
                }
                    };
                    _context.CuocTroChuyenSocials.Add(conversation);
                    await _context.SaveChangesAsync();

                    // Reload để lấy thông tin User (Partner) vừa insert
                    await _context.Entry(conversation).Collection(c => c.NguoiThamGias).Query().Include(n => n.User).LoadAsync();
                }
                else
                {
                    // Nếu cuộc trò chuyện bị ẩn, cho nó hiện lại (Logic tương tự Share)
                    var hiddenEntry = await _context.UserHiddenConversations
                        .FirstOrDefaultAsync(h => h.UserId == userId && h.MaCuocTroChuyen == conversation.MaCuocTroChuyen);

                    if (hiddenEntry != null)
                    {
                        hiddenEntry.HasReappeared = true;
                        await _context.SaveChangesAsync();
                    }
                }

                // 3. Chuẩn bị dữ liệu trả về cho Frontend
                var partner = conversation.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung == targetUserId)?.User;

                return Ok(new
                {
                    maCuocTroChuyen = conversation.MaCuocTroChuyen,
                    partner = new
                    {
                        id = partner.Id,
                        fullName = partner.FullName,
                        avatarUrl = partner.AvatarUrl,
                        isOnline = false // Frontend sẽ tự cập nhật qua SignalR sau
                    },
                    isBlocked = conversation.IsBlocked,
                    maNguoiChan = conversation.MaNguoiChan
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi server: " + ex.Message });
            }
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
        public async Task<IActionResult> GetSocialConversations(string userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != currentUserId) return Unauthorized();

            // 1. Lấy danh sách ID các cuộc trò chuyện bị ẩn (Query nhẹ)
            var hiddenConversationIds = await _context.UserHiddenConversations
                .AsNoTracking()
                .Where(h => h.UserId == userId && !h.HasReappeared)
                .Select(h => h.MaCuocTroChuyen)
                .ToListAsync();

            // 2. Query chính (Đã tối ưu)
            var query = _context.CuocTroChuyenSocials
                .AsNoTracking()
                .Where(c => c.NguoiThamGias.Any(n => n.MaNguoiDung == userId)) // User có tham gia
                .Select(c => new
                {
                    c.MaCuocTroChuyen,
                    c.ThoiGianTao,
                    c.IsBlocked,
                    c.MaNguoiChan,

                    // Chỉ lấy thông tin cần thiết
                    IsMuted = c.NguoiThamGias.FirstOrDefault(n => n.MaNguoiDung == userId).IsMuted,

                    // Lấy tin nhắn cuối cùng (Nhờ Index ở Bước 1 sẽ cực nhanh)
                    LastMessage = c.TinNhans
                        .OrderByDescending(t => t.ThoiGianGui)
                        .Select(t => new {
                            t.NoiDung,
                            t.MediaUrl,
                            t.ThoiGianGui,
                            t.MaNguoiGui,
                            t.DaXem,
                            SenderId = t.Sender.Id,
                            SenderName = t.Sender.FullName,
                            SenderAvatar = t.Sender.AvatarUrl
                        })
                        .FirstOrDefault(),

                    // Lấy Partner (Người kia)
                    Partner = c.NguoiThamGias
                        .Where(n => n.MaNguoiDung != userId)
                        .Select(n => new { n.User.Id, n.User.FullName, n.User.AvatarUrl })
                        .FirstOrDefault(),

                    // Đếm tin chưa đọc (Chỉ đếm khi cần thiết)
                    UnreadCount = c.TinNhans.Count(m => m.MaNguoiGui != userId && !m.DaXem)
                });

            // 3. Thực thi lọc trên Memory (hoặc DB tùy logic) và Phân trang
            // Lưu ý: Lọc IsHidden phức tạp nên đưa về client list hoặc lọc ID trước
            // Ở đây lọc sơ bộ các cuộc hội thoại ẩn mà không có tin nhắn mới

            var rawData = await query
                .Where(c => c.LastMessage != null) // Chỉ lấy cuộc có tin nhắn
                .OrderByDescending(c => c.LastMessage.ThoiGianGui) // Sắp xếp theo tin mới nhất
                .Skip((page - 1) * pageSize) // 🔥 PHÂN TRANG (Chìa khóa chống sập)
                .Take(pageSize)
                .ToListAsync();

            // 4. Xử lý Logic (Ẩn hiện, Regex) ở Client Side (RAM Server)
            // Phần này nhanh vì chỉ chạy trên 20 dòng (pageSize) thay vì hàng nghìn dòng
            var finalResult = new List<object>();

            // Cache share info để tránh query lặp
            var shareIds = new List<int>();
            foreach (var item in rawData)
            {
                if (!string.IsNullOrEmpty(item.LastMessage.NoiDung))
                {
                    var match = Regex.Match(item.LastMessage.NoiDung, @"\[ShareId:(\d+)\]");
                    if (match.Success) shareIds.Add(int.Parse(match.Groups[1].Value));
                }
            }

            var sharesInfo = await _context.Shares
                .AsNoTracking()
                .Where(s => shareIds.Contains(s.ShareId))
                .ToDictionaryAsync(s => s.ShareId, s => s.TargetType);

            foreach (var c in rawData)
            {
                // Logic lọc ẩn: Nếu bị ẩn VÀ không có tin chưa đọc -> Bỏ qua
                bool isHidden = hiddenConversationIds.Contains(c.MaCuocTroChuyen);
                if (isHidden && c.UnreadCount == 0) continue;

                // Xử lý hiển thị nội dung (Copy logic cũ của bạn)
                var msgType = "text";
                var txt = c.LastMessage.NoiDung ?? "";
                if (!string.IsNullOrEmpty(c.LastMessage.MediaUrl))
                {
                    msgType = UrlHelpers.IsVideoUrl(c.LastMessage.MediaUrl) ? "video" : "image";
                    txt = msgType == "video" ? "đã gửi 1 video" : "đã gửi 1 ảnh";
                }
                else if (txt.StartsWith("[ShareId:"))
                {
                    var match = Regex.Match(txt, @"\[ShareId:(\d+)\]");
                    if (match.Success && sharesInfo.TryGetValue(int.Parse(match.Groups[1].Value), out var type))
                    {
                        msgType = type == ShareTargetType.Video ? "video" : "share";
                        txt = msgType == "video" ? "đã chia sẻ 1 video" : "đã chia sẻ 1 bài viết";
                    }
                }

                finalResult.Add(new
                {
                    c.MaCuocTroChuyen,
                    c.ThoiGianTao,
                    c.IsBlocked,
                    c.MaNguoiChan,
                    c.IsMuted,
                    LastMessage = new { NoiDung = txt, c.LastMessage.MediaUrl, c.LastMessage.ThoiGianGui, MessageType = msgType },
                    c.Partner,
                    c.UnreadCount
                });
            }

            return Ok(finalResult);
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
        // 3) Lấy lịch sử tin nhắn 
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

            // =========================================================================
            // 1️⃣ XÁC ĐỊNH THỜI GIAN LỌC (LOGIC CŨ)
            // =========================================================================
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

            // =========================================================================
            // 2️⃣ TRUY VẤN TIN NHẮN & PHÂN TRANG (LOGIC CŨ)
            // =========================================================================
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

            // Đảo ngược để hiển thị theo thời gian tăng dần (cũ nhất -> mới nhất)
            messages.Reverse();

            // =========================================================================
            // 3️⃣ XỬ LÝ SHARE LINK VỚI REGEX (LOGIC CŨ)
            // =========================================================================

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

            // =========================================================================
            // 4️⃣ XÂY DỰNG MESSAGE DTO (LOGIC CŨ)
            // =========================================================================
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

                        // Share của tin nhắn cha
                        Share = parentShareInfo == null ? null : new
                        {
                            parentShareInfo.ShareId,
                            parentShareInfo.PreviewTitle,
                            parentShareInfo.PreviewImage,
                            parentShareInfo.PreviewVideo,
                            parentShareInfo.ShareLink,
                            TargetType = (int)parentShareInfo.TargetType,
                            TinDangId = parentShareInfo.TinDangId
                        }
                    },

                    // ===== Share của tin nhắn chính =====
                    Share = mainShareId != -1 && sharesInfo.TryGetValue(mainShareId, out var mainShareInfo) && mainShareInfo != null
                        ? new
                        {
                            mainShareInfo.ShareId,
                            mainShareInfo.PreviewTitle,
                            mainShareInfo.PreviewImage,
                            mainShareInfo.PreviewVideo,
                            mainShareInfo.ShareLink,
                            TargetType = (int)mainShareInfo.TargetType,
                            TinDangId = mainShareInfo.TinDangId
                        }
                        : null
                };
            }).ToList();

            // =========================================================================
            // 5️⃣ ✨ [MỚI & CẬP NHẬT] CHECK QUYỀN VÀ MỐI QUAN HỆ FOLLOW
            // =========================================================================
            bool canChat = true;
            string restrictionReason = "";
            bool isFollowedByPartner = false; 
            bool isFollowingPartner = false;  

            // 1. Lấy thông tin cuộc trò chuyện và người tham gia để tìm Partner
            var conversation = await _context.CuocTroChuyenSocials
                .Include(c => c.NguoiThamGias)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

            if (conversation != null)
            {
                var partnerId = conversation.NguoiThamGias
                    .FirstOrDefault(n => n.MaNguoiDung != userId)?.MaNguoiDung;

                if (!string.IsNullOrEmpty(partnerId))
                {
                    // 2. ✨ Kiểm tra mối quan hệ 2 chiều
                    isFollowedByPartner = await _context.Follows
                        .AnyAsync(f => f.FollowerId == partnerId && f.FollowingId == userId);

                    isFollowingPartner = await _context.Follows
                        .AnyAsync(f => f.FollowerId == userId && f.FollowingId == partnerId);

                    if (!isFollowedByPartner)
                    {
                        // Lấy 3 tin nhắn mới nhất trong DB (bao gồm cả tin nhắn ĐÃ THU HỒI - anti-spam trick)
                        var last3Messages = await _context.TinNhanSocials
                            .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen) // ⚠️ Không lọc IsRecalled
                            .OrderByDescending(t => t.ThoiGianGui)
                            .Take(3)
                            .Select(t => t.MaNguoiGui)
                            .ToListAsync();

                        // Nếu có đủ 3 tin và TẤT CẢ đều là do MÌNH gửi => Chặn
                        if (last3Messages.Count >= 3 && last3Messages.All(sender => sender == userId))
                        {
                            canChat = false;
                            restrictionReason = "Đã đạt giới hạn tin nhắn chờ.";
                        }
                    }
                }
            }

            // =========================================================================
            // 6️⃣ TRẢ VỀ RESPONSE (KẾT HỢP DỮ LIỆU)
            // =========================================================================
            var response = new
            {
                Messages = resultMessages,
                TotalMessages = totalMessages,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalMessages / (double)pageSize),
                SessionTimestamp = filterTime,
                CanChat = canChat,
                RestrictionReason = restrictionReason,
                IsFollowedByPartner = isFollowedByPartner, 
                IsFollowingPartner = isFollowingPartner   
            };

            return Ok(response);
        }

        [Authorize]
        [HttpPost("conversation/{maCuocTroChuyen}/accept")]
        public async Task<IActionResult> AcceptMessageRequest(string maCuocTroChuyen)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            // 1. Lấy thông tin cuộc trò chuyện
            var conversation = await _context.CuocTroChuyenSocials
                .Include(c => c.NguoiThamGias)
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

            if (conversation == null) return NotFound();

            // 2. Tìm người gửi (Partner)
            var partnerId = conversation.NguoiThamGias
                .FirstOrDefault(n => n.MaNguoiDung != userId)?.MaNguoiDung;

            if (string.IsNullOrEmpty(partnerId)) return BadRequest("Không tìm thấy đối phương.");

            // 3. ✨ LƯU DB: Tạo Follow
            var existingFollow = await _context.Follows
                .FirstOrDefaultAsync(f => f.FollowerId == userId && f.FollowingId == partnerId);

            if (existingFollow == null)
            {
                _context.Follows.Add(new Follow
                {
                    FollowerId = userId,
                    FollowingId = partnerId,
                    FollowedAt = DateTime.UtcNow // ✅ Sửa thành FollowedAt cho đúng Model
                });
                await _context.SaveChangesAsync();
            }

            // 4. ✨ TẠO TIN NHẮN HỆ THỐNG
            var systemMessage = new TinNhanSocial
            {
                MaCuocTroChuyen = maCuocTroChuyen,
                MaNguoiGui = userId,
                NoiDung = "Đã chấp nhận lời mời bắt đầu cuộc trò chuyện.",
                ThoiGianGui = DateTime.UtcNow,
                DaXem = false,
                MediaUrl = null
            };
            _context.TinNhanSocials.Add(systemMessage);
            await _context.SaveChangesAsync();

            // 5. Lấy thông tin người chấp nhận
            var senderInfo = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.FullName, u.AvatarUrl })
                .FirstOrDefaultAsync();

            var messageDto = new
            {
                MaTinNhan = systemMessage.MaTinNhan,
                MaCuocTroChuyen = systemMessage.MaCuocTroChuyen,
                MaNguoiGui = systemMessage.MaNguoiGui,
                NoiDung = systemMessage.NoiDung,
                ThoiGianGui = systemMessage.ThoiGianGui,
                Sender = senderInfo,
                IsSystemMessage = true
            };

            // 6. ✨ GỬI REALTIME
            await _socialHubContext.Clients.Group(maCuocTroChuyen).SendAsync("ReceiveMessage", messageDto);

            await _socialHubContext.Clients.User(partnerId).SendAsync("ConversationAccepted", new
            {
                maCuocTroChuyen,
                acceptedBy = userId
            });

            return Ok(new { message = "Đã chấp nhận cuộc trò chuyện." });
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