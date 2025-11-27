using System;
using System.ComponentModel.DataAnnotations;

namespace UniMarket.Models
{
    public enum ReportTargetType
    {
        Post = 1,
        Video = 2
    }

    public class Report
    {
        [Key]
        public int MaBaoCao { get; set; }

        [Required]
        public string ReporterId { get; set; } = null!;

        // Navigation property to the user who reported
        public ApplicationUser? Reporter { get; set; }

        [Required]
        public ReportTargetType TargetType { get; set; }

        // Id of the target (e.g. MaTinDang)
        public int TargetId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Reason { get; set; } = null!;

        [MaxLength(2000)]
        public string? Details { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public bool IsResolved { get; set; } = false;

        public DateTimeOffset? ResolvedAt { get; set; }
    }
}
