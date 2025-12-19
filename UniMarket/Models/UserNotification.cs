using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    // Enum định nghĩa các loại thông báo
    public enum NotificationType
    {
        Like = 1,
        Comment = 2,
        Reply = 3,
        Follow = 4,         
        System = 5,
        Mention = 6,
        FollowRequest = 7,   
        FollowAccepted = 8   
    }
    public class UserNotification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string ReceiverId { get; set; } // Người nhận (User B)

        [Required]
        public string SenderId { get; set; }   // Người gửi (User A)

        [Required]
        public NotificationType Type { get; set; }

        // --- CÁC TRƯỜNG LIÊN KẾT (QUAN TRỌNG) ---

        /// <summary>
        /// ID của đối tượng CHÍNH (thường là Video/TinDang).
        /// Dùng để lấy ảnh thumbnail và điều hướng đến trang xem video.
        /// </summary>
        public int? ReferenceId { get; set; }

        /// <summary>
        /// 🔥 [MỚI THÊM] ID của đối tượng CỤ THỂ (Comment hoặc Reply).
        /// Dùng để scroll tới đúng vị trí bình luận đó.
        /// </summary>
        public int? EntityId { get; set; }

        // Nội dung text hiển thị (VD: "đã bình luận vào video của bạn")
        public string? Content { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // --- KHÓA NGOẠI (RELATIONSHIPS) ---

        [ForeignKey("ReceiverId")]
        public virtual ApplicationUser Receiver { get; set; }

        [ForeignKey("SenderId")]
        public virtual ApplicationUser Sender { get; set; }
    }
}