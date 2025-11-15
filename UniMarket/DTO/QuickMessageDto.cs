using System;

namespace UniMarket.DTO
{
    public class QuickMessageDto
    {
        public int Id { get; set; }
        public string? Content { get; set; }
        public int Order { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public class CreateQuickMessageDto
    {
        public string? Content { get; set; }
        public int Order { get; set; }
    }

    public class UpdateQuickMessageDto
    {
        public int Id { get; set; }
        public string? Content { get; set; }
        public int Order { get; set; }
    }
}
