using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using UniMarket.Models;
using UniMarket.Models.Mongo;

namespace UniMarket.DataAccess // <--- Lưu ý namespace này
{
    public class MongoDbContext
    {
        private readonly IMongoDatabase _database;

        public MongoDbContext(IConfiguration configuration)
        {
            // Lấy chuỗi kết nối từ appsettings.json
            var connectionString = configuration.GetConnectionString("MongoDbConnection");

            // Lấy tên DB từ cấu hình (UniMarketMongoDB)
            var databaseName = configuration["MongoDbSettings:DatabaseName"];

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }

        // --- KHAI BÁO CÁC BẢNG (COLLECTION) ---

        // 1. Collection lưu Log tìm kiếm (Cái chúng ta đang cần)
        public IMongoCollection<SearchLog> SearchLogs => _database.GetCollection<SearchLog>("SearchLogs");

        // 2. Collection chi tiết tin đăng (Của tính năng cũ, nếu cần dùng lại)
        public IMongoCollection<dynamic> TinDangDetails => _database.GetCollection<dynamic>("TinDangDetails");
    }
}