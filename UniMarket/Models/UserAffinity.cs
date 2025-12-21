using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    // Bảng này lưu trữ độ thân thiết giữa 2 người dùng
    public class UserAffinity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(450)] // Quan trọng: Giới hạn độ dài giống UserId trong Identity
        public string SourceUserId { get; set; } // Người thực hiện hành động (VD: Tôi)

        [Required]
        [StringLength(450)] // Quan trọng: Giới hạn độ dài giống UserId trong Identity
        public string TargetUserId { get; set; } // Người nhận hành động (VD: Người tôi hay nhắn tin)

        // Điểm thân thiết (Score càng cao càng hiển thị lên đầu)
        public double AffinityScore { get; set; } = 0;

        // Lần cuối tương tác (để tính độ trôi theo thời gian)
        public DateTime LastInteraction { get; set; } = DateTime.UtcNow;

        // Navigation Properties (Để DbContext có thể trỏ tới)
        [ForeignKey("SourceUserId")]
        public virtual ApplicationUser? SourceUser { get; set; }

        [ForeignKey("TargetUserId")]
        public virtual ApplicationUser? TargetUser { get; set; }
    }
}