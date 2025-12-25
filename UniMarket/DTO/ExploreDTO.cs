// DTO/ExploreDTO.cs
namespace UniMarket.DTO
{
    public class CategoryHierarchyDto
    {
        public int Id { get; set; }
        public string Ten { get; set; }
        public string Icon { get; set; } // Nếu có
        public List<ChildCategoryDto> DanhMucCons { get; set; }
    }

    public class ChildCategoryDto
    {
        public int Id { get; set; }
        public string Ten { get; set; }
    }

    public class ExploreVideoDto
    {
        public int MaTinDang { get; set; }
        public string TieuDe { get; set; }
        public string VideoUrl { get; set; } // Link video
        public string ThumbnailUrl { get; set; } // Ảnh bìa (nếu có, hoặc dùng frame đầu video)
        public int SoLuotXem { get; set; }
        public int SoLuotTim { get; set; } // Số lượt thích (nếu tính năng tim đã có)

        // Thông tin người dùng
        public string TenNguoiBan { get; set; }
        public string AvatarNguoiBan { get; set; }
        public string UserId { get; set; }
    }
}