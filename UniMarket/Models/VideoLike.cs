using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using UniMarket.Models;

public class VideoLike
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int MaTinDang { get; set; }

    [Required]
    public string UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [ForeignKey("MaTinDang")]
    public TinDang TinDang { get; set; }

    [ForeignKey("UserId")]
    public ApplicationUser User { get; set; }
}
