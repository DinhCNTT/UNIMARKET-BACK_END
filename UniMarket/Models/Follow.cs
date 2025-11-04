using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class Follow
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FollowerId { get; set; }   // người đi follow (mình follow ai đó)

        [Required]
        public string FollowingId { get; set; } // người được follow (ai đó được mình follow)

        public DateTime FollowedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        [ForeignKey("FollowerId")]
        public ApplicationUser? Follower { get; set; }

        [ForeignKey("FollowingId")]
        public ApplicationUser? Following { get; set; }
    }
}
