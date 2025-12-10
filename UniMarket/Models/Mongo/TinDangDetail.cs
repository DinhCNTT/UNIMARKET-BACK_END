using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace UniMarket.Models.Mongo
{
    public class TinDangDetail
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } // ID riêng của Mongo

        public int MaTinDang { get; set; } // FK: Liên kết với SQL Server

        // BsonExtraElements giúp lưu trữ mọi trường JSON động (Hang, Ram, MauSac...) 
        // mà không cần khai báo cứng trong C#.
        [BsonExtraElements]
        public BsonDocument ChiTiet { get; set; }
    }
}