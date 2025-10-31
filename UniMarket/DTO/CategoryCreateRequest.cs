using Microsoft.AspNetCore.Http; // Cần cho IFormFile

namespace UniMarket.DTO // Đảm bảo namespace này khớp với dự án của bạn
{
    public class CategoryCreateRequest
    {
        public string TenDanhMucCha { get; set; }
        public IFormFile? AnhDanhMucCha { get; set; }
        public IFormFile? Icon { get; set; }
    }
}