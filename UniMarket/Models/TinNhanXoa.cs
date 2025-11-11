using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class TinNhanXoa
    {
        [Key]
        public int Id { get; set; }

        public int MaTinNhan { get; set; }

        [Required]
        public string UserId { get; set; }

        public DateTime ThoiGianXoa { get; set; }

        [ForeignKey("MaTinNhan")]
        public TinNhan TinNhan { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }
    }
}