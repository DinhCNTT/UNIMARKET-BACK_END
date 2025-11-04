// File: DTO/DeleteMessageRequest.cs
namespace UniMarket.DTO
{
    public class DeleteMessageRequest
    {
        public string MessageId { get; set; }
        public string ConversationId { get; set; }
    }
}