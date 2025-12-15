using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniMarket.Services.Recommendation;

namespace UniMarket.Services
{
    public class AITrainingWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AITrainingWorker> _logger;

        public AITrainingWorker(IServiceProvider serviceProvider, ILogger<AITrainingWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // ⏳ Đợi 5 giây để Server khởi động hoàn tất và mở cổng kết nối trước
            await Task.Delay(5000, stoppingToken);

            _logger.LogInformation("🤖 [Background Worker] Bắt đầu quy trình Train AI ngầm...");

            try
            {
                // Tạo scope mới (Bắt buộc khi gọi Service trong Background)
                using (var scope = _serviceProvider.CreateScope())
                {
                    // Lấy RecommendationEngine (Singleton) ra để dùng
                    var aiEngine = scope.ServiceProvider.GetRequiredService<RecommendationEngine>();

                    // Gọi hàm Train (Chạy ngầm, không làm đơ web của người dùng)
                    await aiEngine.TrainModel();
                }

                _logger.LogInformation("✅ [Background Worker] Train AI hoàn tất! Hệ thống đề xuất đã sẵn sàng.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ [Background Worker] Lỗi khi Train AI: {ex.Message}");
            }
        }
    }
}