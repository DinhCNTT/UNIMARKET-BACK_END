using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UniMarket.DataAccess;
using UniMarket.DTOs;
using UniMarket.Hubs;
using UniMarket.Models;

namespace UniMarket.Services
{
    public class UserNotificationService : IUserNotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<UserNotificationHub> _hubContext;

        public UserNotificationService(ApplicationDbContext context, IHubContext<UserNotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task CreateNotification(string senderId, string receiverId, NotificationType type, int? refId, string content)
        {
            if (senderId == receiverId) return;

            // 1. Tạo thông báo
            var noti = new UserNotification
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Type = type,
                ReferenceId = refId,
                Content = content,
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };

            _context.UserNotifications.Add(noti);
            await _context.SaveChangesAsync();

            // 2. Lấy thông tin người gửi
            var sender = await _context.ApplicationUsers
                .Where(u => u.Id == senderId)
                .Select(u => new { u.FullName, u.AvatarUrl })
                .FirstOrDefaultAsync();

            // 3. Lấy ảnh thumbnail (LOGIC ĐÃ SỬA: Ưu tiên Video -> Sau đó mới đến Ảnh bìa)
            string? postThumbnail = null;
            if (refId.HasValue && (type == NotificationType.Like || type == NotificationType.Comment || type == NotificationType.Reply))
            {
                var postInfo = await _context.TinDangs
                    .Where(t => t.MaTinDang == refId.Value)
                    .Select(t => new
                    {
                        t.VideoUrl,
                        // Lấy đường dẫn ảnh đầu tiên trong danh sách (dự phòng)
                        ImageThumb = t.AnhTinDangs.OrderBy(a => a.Order).Select(a => a.DuongDan).FirstOrDefault()
                    })
                    .FirstOrDefaultAsync();

                if (postInfo != null)
                {
                    // 🔥 SỬA: Thử lấy thumbnail từ Video trước
                    var videoThumb = GetCloudinaryThumbnail(postInfo.VideoUrl);

                    // Nếu có video thumb thì dùng, nếu không mới dùng ảnh sản phẩm
                    postThumbnail = !string.IsNullOrEmpty(videoThumb) ? videoThumb : postInfo.ImageThumb;
                }
            }

            // 4. Gửi Real-time
            await _hubContext.Clients.User(receiverId).SendAsync("ReceiveNotification", new UserNotificationDto
            {
                Id = noti.Id,
                SenderId = senderId,
                SenderName = sender?.FullName ?? "Người dùng",
                SenderAvatarUrl = sender?.AvatarUrl,
                Type = type.ToString(),
                Content = content,
                ReferenceId = refId,
                PostThumbnailUrl = postThumbnail,
                IsRead = false,
                CreatedAt = noti.CreatedAt,
                TimeAgo = "Vừa xong"
            });
        }

        public async Task<List<UserNotificationDto>> GetNotifications(string userId, string filterType, int page, int pageSize)
        {
            var query = _context.UserNotifications
                .Include(n => n.Sender)
                .AsNoTracking()
                .Where(n => n.ReceiverId == userId);

            switch (filterType.ToLower())
            {
                case "likes":
                    query = query.Where(n => n.Type == NotificationType.Like);
                    break;
                case "comments":
                    query = query.Where(n => n.Type == NotificationType.Comment || n.Type == NotificationType.Reply);
                    break;
                case "followers":
                    query = query.Where(n => n.Type == NotificationType.Follow);
                    break;
                case "mentions":
                    query = query.Where(n => n.Type == NotificationType.Mention);
                    break;
            }

            var rawData = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Lấy danh sách ID bài đăng để query ảnh
            var postIds = rawData
                .Where(x => (x.Type == NotificationType.Like || x.Type == NotificationType.Comment || x.Type == NotificationType.Reply) && x.ReferenceId.HasValue)
                .Select(x => x.ReferenceId.Value)
                .Distinct()
                .ToList();

            // Lấy VideoUrl và Ảnh từ DB
            var postInfos = await _context.TinDangs
                .Where(t => postIds.Contains(t.MaTinDang))
                .Select(t => new
                {
                    t.MaTinDang,
                    t.VideoUrl,
                    ImageThumb = t.AnhTinDangs.OrderBy(a => a.Order).Select(a => a.DuongDan).FirstOrDefault()
                })
                .ToListAsync();

            // 🔥 SỬA LOGIC CHỌN ẢNH (Quan trọng):
            // Ưu tiên GetCloudinaryThumbnail(VideoUrl) trước. Nếu null mới lấy ImageThumb.
            var postImages = postInfos.ToDictionary(
                k => k.MaTinDang,
                v => GetCloudinaryThumbnail(v.VideoUrl) ?? v.ImageThumb
            );

            var result = rawData.Select(item => new UserNotificationDto
            {
                Id = item.Id,
                SenderId = item.SenderId,
                SenderName = item.Sender?.FullName ?? "Người dùng",
                SenderAvatarUrl = item.Sender?.AvatarUrl,
                Type = item.Type.ToString(),
                Content = item.Content,
                ReferenceId = item.ReferenceId,
                IsRead = item.IsRead,
                CreatedAt = item.CreatedAt,
                TimeAgo = CalculateTimeAgo(item.CreatedAt),

                PostThumbnailUrl = (item.ReferenceId.HasValue && postImages.ContainsKey(item.ReferenceId.Value))
                                    ? postImages[item.ReferenceId.Value]
                                    : null
            }).ToList();

            return result;
        }

        public async Task MarkAsRead(int notificationId)
        {
            var noti = await _context.UserNotifications.FindAsync(notificationId);
            if (noti != null && !noti.IsRead)
            {
                noti.IsRead = true;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<int> GetUnreadCount(string userId)
        {
            return await _context.UserNotifications.CountAsync(n => n.ReceiverId == userId && !n.IsRead);
        }

        private string CalculateTimeAgo(DateTime date)
        {
            var span = DateTime.UtcNow - date;
            if (span.TotalMinutes < 1) return "Vừa xong";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d";
            return date.ToString("dd/MM");
        }

        // ==========================================================
        // HÀM HỖ TRỢ: LẤY THUMBNAIL TỪ CLOUDINARY VIDEO URL
        // ==========================================================
        private string? GetCloudinaryThumbnail(string? videoUrl)
        {
            if (string.IsNullOrEmpty(videoUrl)) return null;

            if (videoUrl.Contains("cloudinary.com"))
            {
                // Đổi đuôi .mp4/.mov -> .jpg
                if (videoUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    return videoUrl.Substring(0, videoUrl.Length - 4) + ".jpg";
                }
                if (videoUrl.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
                {
                    return videoUrl.Substring(0, videoUrl.Length - 4) + ".jpg";
                }
                // Fallback: Nếu không phải mp4/mov nhưng là link video cloudinary, thử cộng .jpg vào cuối
                if (!videoUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                    !videoUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                    !videoUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    return videoUrl + ".jpg";
                }
            }
            return null;
        }
    }
}