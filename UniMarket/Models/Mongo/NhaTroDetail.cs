using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace UniMarket.Models.Mongo
{
    public class NhaTroDetail
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } // ID riêng của Mongo

        public int MaTinDang { get; set; } // FK: Liên kết với SQL Server (TinDang.MaTinDang)

        // Chi tiết phòng trọ:
        // - loaiHinhPhong: "phòng trọ", "căn hộ mini", "nhà nguyên căn"
        // - dienTichPhong: số (m²)
        // - sucChua: số (người)
        // - thoiHanChoThue: số (tháng)
        // - giaThueThang: số (VND)
        // - tienIch: array (["wifi", "máy lạnh", ...])
        [BsonExtraElements]
        public BsonDocument ChiTiet { get; set; } // Lưu JSON động
    }
}