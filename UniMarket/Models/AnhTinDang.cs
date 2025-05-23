using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using Newtonsoft.Json;  // Đảm bảo bạn đã thêm thư viện này

namespace UniMarket.Models
{
    public enum MediaType
    {
        Image = 0,
        Video = 1
    }

    public class AnhTinDang
    {
        [Key]
        public int MaAnh { get; set; }

        [Required]
        public int MaTinDang { get; set; }

        [Required]
        [StringLength(255)]
        public string DuongDan { get; set; }

        public int Order { get; set; }

        public MediaType LoaiMedia { get; set; } = MediaType.Image; // mới thêm

        [ForeignKey("MaTinDang")]
        [JsonIgnore]
        public TinDang? TinDang { get; set; }
    }

}
