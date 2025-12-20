using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace UniMarket.Models.Mongo
{
    /// <summary>
    /// MongoDB model cho View History
    /// Dùng để lưu trữ lịch sử xem của user
    /// </summary>
    public class ViewHistory
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        public string UserId { get; set; }

        public int MaTinDang { get; set; }

        // Thời điểm bắt đầu xem
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        // Thời gian xem (tính bằng giây)
        public int WatchedSeconds { get; set; } = 0;

        // Đánh dấu user có xem hết hay không
        public bool IsCompleted { get; set; } = false;

        // Số lần xem lại
        public int RewatchCount { get; set; } = 0;

        // IP của client
        public string? IpAddress { get; set; }

        // Tên thiết bị
        public string? DeviceName { get; set; }

        // Lần xem cuối cùng (để xếp hạng)
        public DateTime LastViewedAt { get; set; } = DateTime.UtcNow;
    }
}