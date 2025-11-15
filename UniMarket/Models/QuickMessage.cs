using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class QuickMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey("User")]
        public string? UserId { get; set; }

        [Required]
        [StringLength(500, MinimumLength = 1)]
        public string? Content { get; set; }

        [Range(1, 5)]
        public int Order { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        // Navigation property
        public virtual ApplicationUser? User { get; set; }
    }
}
