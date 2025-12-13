namespace UniMarket.DTO
{
    public class VideoCommentDto
    {
        public int Id { get; set; }

        public string Content { get; set; }

        public DateTime CreatedAt { get; set; }   // ✅ Thời gian bình luận

        public string UserId { get; set; }        // ✅ ID người bình luận (dùng so sánh với currentUserId ở frontend)

        public string UserName { get; set; }

        public string AvatarUrl { get; set; }

        public int? ParentCommentId { get; set; }  // ✅ Để biết bình luận này là con của ai

        public bool IsReply => ParentCommentId.HasValue; // ✅ Dùng để hiển thị có phải là reply không
        public string TimeAgo { get; set; }
        public List<VideoCommentDto> Replies { get; set; } = new();
    }
}
