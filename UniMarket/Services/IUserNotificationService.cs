using System.Collections.Generic;
using System.Threading.Tasks;
using UniMarket.DTOs;
using UniMarket.Models;

namespace UniMarket.Services
{
    public interface IUserNotificationService
    {
        /// <summary>
        /// Tạo thông báo mới và gửi real-time.
        /// </summary>
        /// <param name="senderId">Người gửi</param>
        /// <param name="receiverId">Người nhận</param>
        /// <param name="type">Loại thông báo (Like, Comment, ...)</param>
        /// <param name="refId">ID của Video/Bài đăng chính (ReferenceId)</param>
        /// <param name="content">Nội dung text hiển thị</param>
        /// <param name="entityId">🔥 [Mới] ID của Comment/Reply cụ thể để scroll tới (Optional)</param>
        Task CreateNotification(string senderId, string receiverId, NotificationType type, int? refId, string content, int? entityId = null);

        // Lấy danh sách (Trả về UserNotificationDto)
        Task<List<UserNotificationDto>> GetNotifications(string userId, string filterType, int page, int pageSize);

        // Đánh dấu đã đọc
        Task MarkAsRead(int notificationId);

        // Đếm số lượng chưa đọc
        Task<int> GetUnreadCount(string userId);
    }
}