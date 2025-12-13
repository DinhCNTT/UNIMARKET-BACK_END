using System;

namespace UniMarket.DTOs
{
    public class UserNotificationDto
    {
        public int Id { get; set; }

        // Thông tin người gửi (để hiển thị Avatar + Tên bên trái)
        public string SenderId { get; set; }
        public string SenderName { get; set; }
        public string SenderAvatarUrl { get; set; }

        // Loại thông báo (Like, Comment, Follow...)
        public string Type { get; set; }

        // Nội dung hiển thị
        public string Content { get; set; }

        // ID tham chiếu (để click vào chuyển trang, ví dụ MaTinDang)
        public int? ReferenceId { get; set; }
        public int? EntityId { get; set; }

        // Ảnh thumbnail bài viết (hiển thị bên phải giống TikTok)
        public string? PostThumbnailUrl { get; set; }

        public bool IsRead { get; set; }

        public DateTime CreatedAt { get; set; }

        // Thời gian hiển thị dạng "2 phút trước", "Vừa xong"
        public string TimeAgo { get; set; }
    }
}