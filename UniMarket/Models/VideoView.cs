using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using UniMarket.Models;

public class VideoView
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int MaTinDang { get; set; }

    public string? UserId { get; set; } // có thể null nếu không login

    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;


    [ForeignKey("MaTinDang")]
    public TinDang TinDang { get; set; }

    [ForeignKey("UserId")]
    public ApplicationUser? User { get; set; }
}
