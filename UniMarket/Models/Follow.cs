using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace UniMarket.Models
{
    public enum FollowStatus
    {
        Pending = 0,  
        Accepted = 1  
    }

    public class Follow
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FollowerId { get; set; }

        [Required]
        public string FollowingId { get; set; }

        public DateTime FollowedAt { get; set; } = DateTime.UtcNow;

        public FollowStatus Status { get; set; } = FollowStatus.Accepted; 

        [ForeignKey("FollowerId")]
        public ApplicationUser? Follower { get; set; }

        [ForeignKey("FollowingId")]
        public ApplicationUser? Following { get; set; }
    }
}