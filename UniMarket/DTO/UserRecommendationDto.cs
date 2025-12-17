namespace UniMarket.Models.DTOs
{
    public class UserRecommendationDto
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string UserName { get; set; }
        public string AvatarUrl { get; set; }
        public bool IsTickBlue { get; set; }
        public string Reason { get; set; }
        public double Score { get; set; }

        // ✅ THÊM TRƯỜNG NÀY
        public bool IsFollowed { get; set; }
    }
}