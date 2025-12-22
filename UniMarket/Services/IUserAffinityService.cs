using UniMarket.DTOs; // Giả sử bạn có namespace này

public interface IUserAffinityService
{
    // Hàm ghi: Theo dõi hành động (Có logic chống spam)
    Task TrackInteractionAsync(string sourceUserId, string targetUserId, InteractionType type);

    // Hàm đọc: Lấy danh sách Follow đã sắp xếp thông minh (Dành cho trang chủ/Chat)
    Task<List<SmartUserDTO>> GetSmartSortedFollowingAsync(string currentUserId, int page, int pageSize);

    // Hàm job: Giảm điểm theo thời gian
    Task DecayScoresAsync();
}

// Enum các loại hành động
public enum InteractionType
{
    ViewProfile = 1,
    LikeVideo = 3,
    Comment = 5,
    Share = 7,
    SendMessage = 10,
    ReplyMessage = 12
}

// DTO trả về cho Client
public class SmartUserDTO
{
    public string UserId { get; set; }
    public string FullName { get; set; }
    public string AvatarUrl { get; set; }
    public bool IsOnline { get; set; } // Quan trọng: Hiển thị chấm xanh
    public double MatchScore { get; set; } // Điểm thân thiết (để debug hoặc hiển thị mức độ bạn bè)
    public string PhoneNumber { get; set; }
}