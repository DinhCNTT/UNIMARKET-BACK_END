namespace UniMarket.DTO
{
    public class UserSearchResultDto
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string AvatarUrl { get; set; }
        public string PhoneNumber { get; set; }

        // --- Stats ---
        public int FollowersCount { get; set; }
        public int TotalVideos { get; set; }
        public int TotalLikes { get; set; }
        public int TotalViews { get; set; }
        public int TotalSaves { get; set; }

        // --- Status ---
        public bool IsFollowed { get; set; }

        // --- Ranking Score ---
        public double RankingScore { get; set; }
    }
}