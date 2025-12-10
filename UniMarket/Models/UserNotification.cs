using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    // Giữ nguyên Enum để phân loại
    public enum NotificationType
    {
        Like = 1,       // Ai đó thích video của bạn
        Comment = 2,    // Ai đó bình luận video
        Reply = 3,      // Ai đó trả lời bình luận của bạn
        Follow = 4,     // Ai đó follow bạn
        System = 5,     // Thông báo từ hệ thống
        Mention = 6     // Ai đó tag bạn (@tenban)
    }

    public class UserNotification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string ReceiverId { get; set; } // Người nhận thông báo (User B)

        [Required]
        public string SenderId { get; set; }   // Người gây ra hành động (User A)

        [Required]
        public NotificationType Type { get; set; }

        // ReferenceId: Lưu ID bài đăng (MaTinDang) hoặc ID khác tùy loại thông báo
        public int? ReferenceId { get; set; }

        // Nội dung hiển thị (Ví dụ: "đã thích video của bạn")
        public string? Content { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // --- RELATIONSHIPS (Khóa ngoại) ---
        [ForeignKey("ReceiverId")]
        public virtual ApplicationUser Receiver { get; set; }

        [ForeignKey("SenderId")]
        public virtual ApplicationUser Sender { get; set; }
    }
}