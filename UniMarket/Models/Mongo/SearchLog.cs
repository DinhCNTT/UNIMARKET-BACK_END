using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace UniMarket.Models.Mongo
{
    public class SearchLog
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        public string Keyword { get; set; }          // Từ khóa gốc
        public string NormalizedKeyword { get; set; } // Từ khóa viết thường

        public string? UserId { get; set; }          // Null nếu khách vãng lai
        public string? SessionId { get; set; }       // Session ID

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int ResultCount { get; set; }         // Số kết quả tìm thấy

        public string? Platform { get; set; } = "Web";
    }
}