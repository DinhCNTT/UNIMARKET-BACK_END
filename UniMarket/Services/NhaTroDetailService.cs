using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using UniMarket.Models.Mongo;
using System.Threading.Tasks;

namespace UniMarket.Services
{
    public class NhaTroDetailService
    {
        private readonly IMongoCollection<NhaTroDetail> _nhaTroCollection;

        public NhaTroDetailService(IConfiguration config)
        {
            // Lấy config từ appsettings.json
            var mongoSettings = config.GetSection("MongoDbSettings").Get<MongoDbSettings>();
            var connectionString = config.GetConnectionString("MongoDbConnection");

            // Khởi tạo Client và Database
            var mongoClient = new MongoClient(connectionString);
            var mongoDatabase = mongoClient.GetDatabase(mongoSettings?.DatabaseName ?? "UniMarket");

            // Lấy Collection (Tên collection: "NhaTroDetails")
            _nhaTroCollection = mongoDatabase.GetCollection<NhaTroDetail>("NhaTroDetails");
        }

        // =========================================================
        // CÁC HÀM TRUY VẤN CƠ BẢN
        // =========================================================

        /// <summary>
        /// Lấy chi tiết nhà trọ dựa theo Mã Tin Đăng (SQL ID)
        /// </summary>
        public async Task<NhaTroDetail?> GetByMaTinDangAsync(int maTinDang) =>
            await _nhaTroCollection.Find(x => x.MaTinDang == maTinDang).FirstOrDefaultAsync();

        /// <summary>
        /// Tạo mới một chi tiết nhà trọ
        /// </summary>
        public async Task CreateAsync(NhaTroDetail newDetail) =>
            await _nhaTroCollection.InsertOneAsync(newDetail);

        /// <summary>
        /// Cập nhật nội dung chi tiết
        /// </summary>
        public async Task UpdateAsync(int maTinDang, BsonDocument updatedChiTiet)
        {
            var filter = Builders<NhaTroDetail>.Filter.Eq(x => x.MaTinDang, maTinDang);
            var update = Builders<NhaTroDetail>.Update.Set(x => x.ChiTiet, updatedChiTiet);
            await _nhaTroCollection.UpdateOneAsync(filter, update);
        }

        // =========================================================
        // CÁC HÀM XÓA (DELETE)
        // =========================================================

        /// <summary>
        /// Xóa chi tiết dựa theo Mã Tin Đăng
        /// </summary>
        public async Task RemoveAsync(int maTinDang) =>
            await _nhaTroCollection.DeleteOneAsync(x => x.MaTinDang == maTinDang);

        /// <summary>
        /// Xóa chi tiết dựa theo ID MongoDB
        /// </summary>
        public async Task DeleteByIdAsync(string id)
        {
            await _nhaTroCollection.DeleteOneAsync(x => x.Id == id);
        }
    }
}