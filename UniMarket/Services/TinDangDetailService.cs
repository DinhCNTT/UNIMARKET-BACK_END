using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using UniMarket.Models.Mongo;
using System.Threading.Tasks;

namespace UniMarket.Services
{
    // 1. Class cấu hình (Model hứng dữ liệu từ appsettings.json)
    public class MongoDbSettings
    {
        public string ConnectionString { get; set; } = null!;
        public string DatabaseName { get; set; } = null!;
        public string CollectionName { get; set; } = null!;
    }

    // 2. Service chính xử lý MongoDB
    public class TinDangDetailService
    {
        private readonly IMongoCollection<TinDangDetail> _detailsCollection;

        public TinDangDetailService(IConfiguration config)
        {
            // Lấy config từ appsettings.json
            var mongoSettings = config.GetSection("MongoDbSettings").Get<MongoDbSettings>();
            var connectionString = config.GetConnectionString("MongoDbConnection");

            // Khởi tạo Client và Database
            var mongoClient = new MongoClient(connectionString);
            var mongoDatabase = mongoClient.GetDatabase(mongoSettings.DatabaseName);

            // Lấy Collection (Bảng)
            _detailsCollection = mongoDatabase.GetCollection<TinDangDetail>(mongoSettings.CollectionName);
        }

        // =========================================================
        // CÁC HÀM TRUY VẤN CƠ BẢN
        // =========================================================

        /// <summary>
        /// Lấy chi tiết tin đăng dựa theo Mã Tin Đăng (SQL ID)
        /// </summary>
        public async Task<TinDangDetail?> GetByMaTinDangAsync(int maTinDang) =>
            await _detailsCollection.Find(x => x.MaTinDang == maTinDang).FirstOrDefaultAsync();

        /// <summary>
        /// Tạo mới một chi tiết tin đăng
        /// </summary>
        public async Task CreateAsync(TinDangDetail newDetail) =>
            await _detailsCollection.InsertOneAsync(newDetail);

        /// <summary>
        /// Cập nhật nội dung chi tiết (Dùng cho Update thông thường)
        /// </summary>
        public async Task UpdateAsync(int maTinDang, BsonDocument updatedChiTiet)
        {
            var filter = Builders<TinDangDetail>.Filter.Eq(x => x.MaTinDang, maTinDang);
            var update = Builders<TinDangDetail>.Update.Set(x => x.ChiTiet, updatedChiTiet);
            await _detailsCollection.UpdateOneAsync(filter, update);
        }

        // =========================================================
        // CÁC HÀM XÓA (DELETE)
        // =========================================================

        /// <summary>
        /// Xóa chi tiết dựa theo Mã Tin Đăng (Dùng khi xóa tin bên SQL)
        /// </summary>
        public async Task RemoveAsync(int maTinDang) =>
            await _detailsCollection.DeleteOneAsync(x => x.MaTinDang == maTinDang);

        /// <summary>
        /// ✅ [QUAN TRỌNG] Xóa chi tiết dựa theo ID MongoDB (Chuỗi string _id)
        /// Hàm này dùng để xóa document cũ bị lỗi trước khi tạo mới trong hàm Update PutTinDang
        /// </summary>
        /// <param name="id">ID của document trong MongoDB (ObjectId dạng string)</param>
        public async Task DeleteByIdAsync(string id)
        {
            await _detailsCollection.DeleteOneAsync(x => x.Id == id);
        }
    }
}