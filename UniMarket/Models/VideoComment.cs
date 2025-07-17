using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using UniMarket.Models;

public class VideoComment
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int MaTinDang { get; set; }

    [Required]
    public string UserId { get; set; }

    [Required]
    [MaxLength(500)]
    public string Content { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Self-reference: Bình luận cha
    public int? ParentCommentId { get; set; }

    [ForeignKey(nameof(ParentCommentId))]
    public virtual VideoComment? ParentComment { get; set; }

    public virtual ICollection<VideoComment> Replies { get; set; } = new List<VideoComment>();

    [ForeignKey(nameof(MaTinDang))]
    public virtual TinDang TinDang { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; }
}
