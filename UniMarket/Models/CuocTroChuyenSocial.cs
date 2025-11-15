using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class CuocTroChuyenSocial
    {
        [Key]
        public string MaCuocTroChuyen { get; set; } = Guid.NewGuid().ToString();

        public DateTime ThoiGianTao { get; set; } = DateTime.UtcNow;
        public DateTime? NgayCapNhat { get; set; }
        public bool IsEmpty { get; set; } = true;
        public bool IsBlocked { get; set; } = false;

        [StringLength(450)]
        public string? MaNguoiChan { get; set; }

        // Navigation Properties
        public ICollection<TinNhanSocial>? TinNhans { get; set; }
        public ICollection<NguoiThamGiaSocial>? NguoiThamGias { get; set; }
    }

    public class TinNhanSocial
    {
        [Key]
        public string MaTinNhan { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string MaCuocTroChuyen { get; set; } = string.Empty;

        [Required]
        public string MaNguoiGui { get; set; } = string.Empty;

        public string? NoiDung { get; set; }
        public string? MediaUrl { get; set; }
        public DateTime ThoiGianGui { get; set; } = DateTime.UtcNow;
        public bool DaXem { get; set; } = false;

        // ==================================================
        // ✨ TÍNH NĂNG MỚI ✨
        // ==================================================

        // 1️⃣ Trả lời tin nhắn (Reply)
        public string? ParentMessageId { get; set; }

        // 2️⃣ Thu hồi tin nhắn (Recall)
        public bool IsRecalled { get; set; } = false;

        // Navigation
        [ForeignKey("MaNguoiGui")]
        public ApplicationUser? Sender { get; set; }

        [ForeignKey("MaCuocTroChuyen")]
        public CuocTroChuyenSocial? CuocTroChuyenSocial { get; set; }

        // 3️⃣ Navigation tới tin nhắn được trả lời
        [ForeignKey("ParentMessageId")]
        public TinNhanSocial? ParentMessage { get; set; }

        // 4️⃣ Danh sách người dùng đã xóa tin nhắn này (xóa 1 phía)
        public ICollection<DeletedMessageForUser>? DeletedForUsers { get; set; }
    }

    public class NguoiThamGiaSocial
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string MaCuocTroChuyen { get; set; } = string.Empty;

        [Required, StringLength(450)]
        public string MaNguoiDung { get; set; } = string.Empty;

        public bool IsOnline { get; set; } = false;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;

        // Navigation
        [ForeignKey("MaNguoiDung")]
        public ApplicationUser? User { get; set; }

        [ForeignKey("MaCuocTroChuyen")]
        public CuocTroChuyenSocial? CuocTroChuyenSocial { get; set; }
        public bool IsMuted { get; set; } = false;
    }

    // Bảng này lưu trạng thái "xóa tin nhắn chỉ ở phía người dùng"
    public class DeletedMessageForUser
    {
        [Required, StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        // ✨ SỬA LỖI KEY LENGTH: Thêm StringLength(450) vào đây
        [Required, StringLength(450)]
        public string TinNhanSocialId { get; set; } = string.Empty;

        // Navigation
        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [ForeignKey("TinNhanSocialId")]
        public TinNhanSocial? TinNhanSocial { get; set; }
    }

    // Bảng lưu trạng thái online toàn hệ thống (tùy chọn)
    public class UserActivity
    {
        [Key]
        [StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        public bool IsOnline { get; set; } = false;
        public DateTime LastActive { get; set; } = DateTime.UtcNow;
    }
}