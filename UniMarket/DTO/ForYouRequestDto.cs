using System.Collections.Generic;

namespace UniMarket.DTO
{
    public class ForYouRequestDto
    {
        // Danh sách ID các video đã hiển thị (để không lặp lại)
        public List<int>? ExcludedIds { get; set; } = new List<int>();

        // Số lượng video muốn tải thêm (mặc định 5 hoặc 10)
        public int PageSize { get; set; } = 10;
    }
}