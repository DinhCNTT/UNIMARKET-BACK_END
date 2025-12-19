using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace UniMarket.Models
{
    public class ApplicationUser : IdentityUser
    {
        // --- Thông tin cá nhân cơ bản ---
        [Required]
        public string? FullName { get; set; }

        public string? Address { get; set; }

        public string? Age { get; set; }

        [DefaultValue("User")]
        public string Role { get; set; } = "User";

        public string? AvatarUrl { get; set; }

        // --- Xác thực & Bảo mật ---
        public string? EmailVerificationCode { get; set; }

        public DateTime? CodeGeneratedAt { get; set; }

        // --- Trạng thái Online ---
        public bool IsOnline { get; set; }

        public DateTime? LastOnlineTime { get; set; }

        // --- Cài đặt riêng tư ---
        [DefaultValue(false)]
        public bool IsPrivateAccount { get; set; } = false; // Mặc định là công khai

        // --- Quản lý Xóa mềm (Soft Delete) ---
        [DefaultValue(false)]
        public bool IsDeleted { get; set; } = false; // Đánh dấu đã xóa hay chưa

        public DateTime? DeletedAt { get; set; } // Thời điểm xóa
    }
}