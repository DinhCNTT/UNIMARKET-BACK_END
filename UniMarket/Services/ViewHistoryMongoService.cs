using MongoDB.Bson;
using MongoDB.Driver;
using UniMarket.Models.Mongo;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UniMarket.Services
{
    /// <summary>
    /// Service quản lý View History sử dụng MongoDB
    /// </summary>
    public class ViewHistoryMongoService
    {
        private readonly IMongoCollection<ViewHistory> _viewHistoryCollection;

        public ViewHistoryMongoService(IConfiguration config)
        {
            // Lấy connection string từ appsettings.json
            var mongoUrl = config.GetConnectionString("MongoDb") ?? "mongodb://localhost:27017";
            var dbName = "UniMarketDb";

            var client = new MongoClient(mongoUrl);
            var database = client.GetDatabase(dbName);
            _viewHistoryCollection = database.GetCollection<ViewHistory>("ViewHistory");

            // Tạo index cho các truy vấn thường xuyên (UserId và LastViewedAt)
            var indexKeys = Builders<ViewHistory>.IndexKeys
                .Descending(x => x.UserId)
                .Descending(x => x.LastViewedAt);
            
            _viewHistoryCollection.Indexes.CreateOneAsync(
                new CreateIndexModel<ViewHistory>(indexKeys)
            ).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Lấy lịch sử xem của user (giới hạn số lượng)
        /// </summary>
        public async Task<List<ViewHistory>> GetUserViewHistoryAsync(string userId, int limit = 10)
        {
            return await _viewHistoryCollection
                .Find(x => x.UserId == userId)
                .SortByDescending(x => x.LastViewedAt)
                .Limit(limit)
                .ToListAsync();
        }

        /// <summary>
        /// Lấy lịch sử xem phân trang
        /// </summary>
        public async Task<(List<ViewHistory> history, long totalCount)> GetUserViewHistoryPagedAsync(
            string userId, 
            int page = 1, 
            int pageSize = 12)
        {
            var filter = Builders<ViewHistory>.Filter.Eq(x => x.UserId, userId);
            var totalCount = await _viewHistoryCollection.CountDocumentsAsync(filter);

            var history = await _viewHistoryCollection
                .Find(filter)
                .SortByDescending(x => x.LastViewedAt)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();
            
            return (history, totalCount);
        }

        /// <summary>
        /// Track khi user xem tin
        /// Nếu đã xem cùng một tin trong hôm nay thì cập nhật, không tạo mới
        /// </summary>
        public async Task<ViewHistory> TrackViewAsync(
            int maTinDang,
            string userId,
            int watchedSeconds = 0,
            bool isCompleted = false,
            string? ipAddress = null,
            string? deviceName = null)
        {
            var today = DateTime.UtcNow.Date;
            var startOfDay = new DateTime(today.Year, today.Month, today.Day, 0, 0, 0, DateTimeKind.Utc);
            var endOfDay = startOfDay.AddDays(1).AddSeconds(-1);

            // Kiểm tra xem có view cùng post trong hôm nay không
            var filter = Builders<ViewHistory>.Filter.And(
                Builders<ViewHistory>.Filter.Eq(x => x.UserId, userId),
                Builders<ViewHistory>.Filter.Eq(x => x.MaTinDang, maTinDang),
                Builders<ViewHistory>.Filter.Gte(x => x.StartedAt, startOfDay),
                Builders<ViewHistory>.Filter.Lte(x => x.StartedAt, endOfDay)
            );

            var existingView = await _viewHistoryCollection.Find(filter).FirstOrDefaultAsync();
            if (existingView != null)
            {
                // Cập nhật view hiện có
                var update = Builders<ViewHistory>.Update
                    .Set(x => x.LastViewedAt, DateTime.UtcNow)
                    .Inc(x => x.RewatchCount, 1)
                    .Set(x => x.WatchedSeconds, watchedSeconds)
                    .Set(x => x.IsCompleted, isCompleted);
                
                await _viewHistoryCollection.UpdateOneAsync(filter, update);
                return await _viewHistoryCollection.Find(filter).FirstOrDefaultAsync();
            }

            // Tạo view mới
            var newView = new ViewHistory
            {
                UserId = userId,
                MaTinDang = maTinDang,
                StartedAt = DateTime.UtcNow,
                LastViewedAt = DateTime.UtcNow,
                WatchedSeconds = watchedSeconds,
                IsCompleted = isCompleted,
                IpAddress = ipAddress,
                DeviceName = deviceName,
                RewatchCount = 0
            };

            await _viewHistoryCollection.InsertOneAsync(newView);
            return newView;
        }

        /// <summary>
        /// Xóa 1 record view history
        /// </summary>
        public async Task<bool> DeleteViewHistoryAsync(string viewHistoryId)
        {
            if (!ObjectId.TryParse(viewHistoryId, out var objectId))
                return false;

            var result = await _viewHistoryCollection.DeleteOneAsync(
                Builders<ViewHistory>.Filter.Eq(x => x.Id, viewHistoryId)
            );
            return result.DeletedCount > 0;
        }

        /// <summary>
        /// Xóa toàn bộ view history của user
        /// </summary>
        public async Task<long> ClearUserViewHistoryAsync(string userId)
        {
            var result = await _viewHistoryCollection.DeleteManyAsync(
                Builders<ViewHistory>.Filter.Eq(x => x.UserId, userId)
            );

            return result.DeletedCount;
        }

        /// <summary>
        /// Lấy view history cho một tin đăng cụ thể
        /// </summary>
        public async Task<ViewHistory?> GetViewHistoryAsync(string id)
        {
            if (!ObjectId.TryParse(id, out _))
                return null;
            
            return await _viewHistoryCollection.Find(
                Builders<ViewHistory>.Filter.Eq(x => x.Id, id)
            ).FirstOrDefaultAsync();
        }
    }
}