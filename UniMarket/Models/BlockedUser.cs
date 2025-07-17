namespace UniMarket.Models
{
    public class BlockedUser
    {
        public int Id { get; set; }
        public string BlockerId { get; set; }
        public string BlockedId { get; set; }
        public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
    }
}