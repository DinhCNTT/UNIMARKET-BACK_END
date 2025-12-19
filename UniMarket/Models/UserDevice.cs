using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class UserDevice
    {
        [Key]
        public int Id { get; set; }
        public string UserId { get; set; }
        public string DeviceName { get; set; } // Ví dụ: Chrome on Windows
        public string Location { get; set; }   // Ví dụ: Ho Chi Minh, VN
        public DateTime LastLogin { get; set; }
        public bool IsCurrent { get; set; }    // Là thiết bị đang dùng?

        [ForeignKey("UserId")]
        public virtual ApplicationUser User { get; set; }
    }
}