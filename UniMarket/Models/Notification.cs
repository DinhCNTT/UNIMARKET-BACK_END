using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = null!; // recipient user id

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = null!;

        [MaxLength(2000)]
        public string? Message { get; set; }

        [MaxLength(500)]
        public string? Url { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        // If true, notification was created/sent by an administrator action
        public bool IsFromAdmin { get; set; } = false;
    }
}
