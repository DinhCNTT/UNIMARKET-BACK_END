using System.Collections.Generic;
using System.Threading.Tasks;
using UniMarket.DTOs; // Sử dụng UserNotificationDto
using UniMarket.Models;

namespace UniMarket.Services
{
    public interface IUserNotificationService
    {
        // Tạo thông báo mới
        Task CreateNotification(string senderId, string receiverId, NotificationType type, int? refId, string content);

        // Lấy danh sách (Trả về UserNotificationDto)
        Task<List<UserNotificationDto>> GetNotifications(string userId, string filterType, int page, int pageSize);

        // Đánh dấu đã đọc
        Task MarkAsRead(int notificationId);

        // Đếm số lượng chưa đọc
        Task<int> GetUnreadCount(string userId);
    }
}