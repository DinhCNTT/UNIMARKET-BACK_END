using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using UniMarket.Models;

public class SearchHistory
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(500)]
    public string Keyword { get; set; } = null!;

    public string? UserId { get; set; }  // null nếu user chưa đăng nhập

    [Required]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Thêm cột tracking device
    [StringLength(100)]
    public string? DeviceName { get; set; }

    [StringLength(50)]
    public string? IpAddress { get; set; }

    // Liên kết với user
    [ForeignKey("UserId")]
    public virtual ApplicationUser? User { get; set; }
}