using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniMarket.Models
{
    public class NguoiThamGia
    {
        [Key]
        public int MaThamGia { get; set; }

        [Required]
        public string MaCuocTroChuyen { get; set; }

        [Required]
        public string MaNguoiDung { get; set; }

        [ForeignKey("MaCuocTroChuyen")]
        public CuocTroChuyen? CuocTroChuyen { get; set; }

        [ForeignKey("MaNguoiDung")]
        public ApplicationUser? NguoiDung { get; set; }
    }
}
