using System;
using System.ComponentModel.DataAnnotations;

namespace UniMarket.Models
{
    public class UserHiddenConversation
    {

        [Required]
        public string UserId { get; set; }

        [Required]
        public string MaCuocTroChuyen { get; set; }

        [Required]
        public DateTime ThoiGianAn { get; set; }


        public bool HasReappeared { get; set; } = false;
        // Flag để phân biệt giữa ẩn và xóa
        public bool IsDeleted { get; set; } = false;
    }
}