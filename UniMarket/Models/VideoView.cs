using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using UniMarket.Models;

public class VideoView
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int MaTinDang { get; set; }

    public string? UserId { get; set; } // null nếu chưa login

    // ⏱️ Thời điểm bắt đầu xem
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    // ⏱️ Thời gian xem (tính bằng giây)
    public int WatchedSeconds { get; set; } = 0;

    // ✅ Đánh dấu user có xem hết video hay không
    public bool IsCompleted { get; set; } = false;

    // 🔁 Số lần xem lại (nếu user tua lại video)
    public int RewatchCount { get; set; } = 0;

    // 🌍 IP của client
    [MaxLength(64)]
    public string? IpAddress { get; set; }

    // 📱 Tên thiết bị / user-agent
    [MaxLength(256)]
    public string? DeviceName { get; set; }

    [ForeignKey("MaTinDang")]
    public TinDang TinDang { get; set; }

    [ForeignKey("UserId")]
    public ApplicationUser? User { get; set; }
}
