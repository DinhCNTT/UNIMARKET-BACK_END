namespace UniMarket.DTO
{
    public class BulkSetChatStateRequest
    {
        public string UserId { get; set; }
        public List<string> ChatIds { get; set; }
        public bool IsHidden { get; set; }
        public bool IsDeleted { get; set; }
    }
}
