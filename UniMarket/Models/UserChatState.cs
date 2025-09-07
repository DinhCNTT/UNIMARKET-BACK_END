using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class UserChatState
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(450)] // ASP.NET Identity UserId
        public string UserId { get; set; }

        [Required]
        public string ChatId { get; set; } // MaCuocTroChuyen

        // --- Trạng thái ---
        public bool IsHidden { get; set; } = false;   // Ẩn (archive)
        public bool IsDeleted { get; set; } = false;  // Xóa (chỉ phía user)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // --- Navigation ---
        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [ForeignKey("ChatId")]
        public CuocTroChuyen? Chat { get; set; }
    }
}