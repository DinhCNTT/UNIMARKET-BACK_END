using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace UniMarket.Services
{
    /// <summary>
    /// ExternalToolService: Xử lý các công cụ ngoài (tính ship, thời tiết, lấy chi tiết sản phẩm từ Mongo...)
    /// </summary>
    public class ExternalToolService
    {
        private readonly IMongoCollection<BsonDocument> _mongoProductCollection;
        private readonly ILogger<ExternalToolService> _logger;

        public ExternalToolService(IMongoClient mongoClient, ILogger<ExternalToolService> logger)
        {
            _logger = logger;
            try
            {
                var db = mongoClient.GetDatabase("UniMarketDB");
                _mongoProductCollection = db.GetCollection<BsonDocument>("TinDang_Details");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ExternalTool] Failed to connect to MongoDB: {msg}", ex.Message);
            }
        }

        // ============ TOOL 1: Calculate Shipping ============
        public string CalculateShipping(string location)
        {
            // Mock implementation - có thể thay bằng API thực (GiaoHangNhanh, GHN, vv)
            if (string.IsNullOrEmpty(location))
                return "Dạ vị trí bác chưa rõ ạ. Bác nói cụ thể tỉnh thành nhé!";

            var basePrice = 15000; // Giá base
            var locationMultiplier = location.ToLower() switch
            {
                "tphcm" or "tp. hcm" => 1.0m,
                "hà nội" => 1.0m,
                "hải phòng" => 1.2m,
                "đà nẵng" => 1.5m,
                _ => 2.0m
            };

            var totalPrice = (decimal)(basePrice * locationMultiplier);
            return $"Dạ phí ship tới {location} khoảng {totalPrice:N0}đ ạ.";
        }

        // ============ TOOL 2: Check Weather ============
        public string CheckWeather(string location)
        {
            // Mock implementation - có thể thay bằng API OpenWeatherMap
            return $"Dạ thời tiết tại {location} hiện tại khá đẹp, nhiệt độ khoảng 25-28°C ạ.";
        }

        // ============ TOOL 3: Check Exchange Rate ============
        public string CheckExchangeRate()
        {
            // Mock implementation - có thể thay bằng API tỷ giá thực
            return "Dạ tỷ giá USD/VND hiện tại khoảng 24,500 - 24,600đ ạ.";
        }

        // ============ TOOL 4: Get Product Rating ============
        public string GetProductRating(int productId)
        {
            return $"Dạ sản phẩm ID {productId} có đánh giá 4.5/5 sao từ 120 khách hàng ạ.";
        }

        // ============ TOOL 5: Check Stock ============
        public string CheckStock(int productId)
        {
            return $"Dạ sản phẩm ID {productId} hiện có sẵn hàng ạ.";
        }

        // ============ TOOL 6: Get Product Details (từ Mongo) - LAZY LOADING ============
        /// <summary>
        /// Lấy chi tiết sản phẩm từ Mongo khi khách hỏi.
        /// VD: Màu sắc, dung lượng, bảo hành, tình trạng...
        /// </summary>
        public async Task<string> GetProductDetailAsync(int maTinDang)
        {
            try
            {
                if (_mongoProductCollection == null)
                {
                    _logger.LogWarning("[ExternalTool] MongoDB not connected, cannot fetch details");
                    return "Dạ hiện không thể lấy chi tiết sản phẩm, bác vui lòng thử lại sau ạ.";
                }

                // Chỉ query 1 record từ Mongo
                var filter = Builders<BsonDocument>.Filter.Eq("MaTinDang", maTinDang);
                var doc = await _mongoProductCollection.Find(filter).FirstOrDefaultAsync();

                if (doc == null)
                {
                    _logger.LogWarning("[ExternalTool] Product {id} not found in Mongo", maTinDang);
                    return "Dạ không tìm thấy thông tin chi tiết của sản phẩm này ạ.";
                }

                // Trích xuất các trường từ BsonDocument
                var hang = doc.Contains("Hang") ? doc["Hang"].AsString : 
                           (doc.Contains("hang") ? doc["hang"].AsString : "Không rõ");
                
                var dongMay = doc.Contains("DongMay") ? doc["DongMay"].AsString : 
                              (doc.Contains("dongMay") ? doc["dongMay"].AsString : "Không rõ");
                
                var mauSac = doc.Contains("MauSac") ? doc["MauSac"].AsString : 
                             (doc.Contains("mauSac") ? doc["mauSac"].AsString : "Không rõ");
                
                var dungLuong = doc.Contains("DungLuong") ? doc["DungLuong"].AsString : 
                                (doc.Contains("dungLuong") ? doc["dungLuong"].AsString : "Không rõ");
                
                var baoHanh = doc.Contains("BaoHanh") ? doc["BaoHanh"].AsString : 
                              (doc.Contains("baoHanh") ? doc["baoHanh"].AsString : "Không rõ");
                
                var xuatXu = doc.Contains("XuatXu") ? doc["XuatXu"].AsString : 
                             (doc.Contains("xuatXu") ? doc["xuatXu"].AsString : "Không rõ");

                var details = $"{hang} {dongMay} - Màu: {mauSac}, Dung lượng: {dungLuong}, Bảo hành: {baoHanh}, Xuất xứ: {xuatXu}";
                
                _logger.LogInformation("[ExternalTool] Fetched product detail: {details}", details);
                return details;
            }
            catch (Exception ex)
            {
                _logger.LogError("[ExternalTool] Error fetching product detail: {msg}", ex.Message);
                return "Dạ có lỗi khi lấy thông tin chi tiết ạ.";
            }
        }
    }
}
