namespace UniMarket.DTO
{
    public class UserChatStateResponse
    {
        public string ChatId { get; set; }
        public bool IsHidden { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
