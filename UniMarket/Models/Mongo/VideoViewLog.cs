using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System;

namespace UniMarket.Models.Mongo
{
    public class VideoViewLog
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } // Mongo dùng String ObjectId làm Key

        [BsonElement("maTinDang")]
        public int MaTinDang { get; set; } // Vẫn giữ int để map với SQL

        [BsonElement("userId")]
        public string? UserId { get; set; } // Nullable

        [BsonElement("startedAt")]
        public DateTime StartedAt { get; set; }

        [BsonElement("watchedSeconds")]
        public int WatchedSeconds { get; set; } = 0;

        [BsonElement("isCompleted")]
        public bool IsCompleted { get; set; } = false;

        [BsonElement("rewatchCount")]
        public int RewatchCount { get; set; } = 0;

        [BsonElement("ipAddress")]
        public string? IpAddress { get; set; }

        [BsonElement("deviceName")]
        public string? DeviceName { get; set; }
    }
}