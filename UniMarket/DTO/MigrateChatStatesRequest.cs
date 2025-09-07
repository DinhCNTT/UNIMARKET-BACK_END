namespace UniMarket.DTO
{
    public class MigrateChatStatesRequest
    {
        public string UserId { get; set; }
        public List<string>? HiddenChatIds { get; set; }
        public List<string>? DeletedChatIds { get; set; }
    }
}
