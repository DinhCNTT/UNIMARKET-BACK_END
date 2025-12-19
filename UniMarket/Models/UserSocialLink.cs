using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class UserSocialLink
    {
        [Key]
        public int Id { get; set; }

        public string UserId { get; set; } // Khóa ngoại trỏ về User

        [Required]
        public string Provider { get; set; } // Ví dụ: "Facebook", "Google", "Instagram"

        public string? ExternalUserId { get; set; } // ID của user bên MXH đó (nếu có)

        public string? ProfileUrl { get; set; } // Link đến trang cá nhân (nếu cần hiển thị)

        public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public virtual ApplicationUser User { get; set; }
    }
}