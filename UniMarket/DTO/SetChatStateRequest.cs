namespace UniMarket.DTO
{
    public class SetChatStateRequest
    {
        public string UserId { get; set; }
        public string ChatId { get; set; }
        public bool IsHidden { get; set; }
        public bool IsDeleted { get; set; }
    }

}
