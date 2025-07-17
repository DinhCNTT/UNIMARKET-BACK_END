public class SearchHistory
{
    public int Id { get; set; }
    public string? UserId { get; set; }  // null nếu user chưa đăng nhập
    public string Keyword { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
