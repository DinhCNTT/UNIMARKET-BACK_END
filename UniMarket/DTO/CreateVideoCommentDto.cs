namespace UniMarket.DTO
{
    public class CreateVideoCommentDto
    {
        public string Content { get; set; }
        public int? ParentCommentId { get; set; } // null nếu là bình luận gốc
    }

}
