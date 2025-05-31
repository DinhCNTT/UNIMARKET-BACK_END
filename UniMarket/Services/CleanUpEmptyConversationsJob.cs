using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;

namespace UniMarket.Services
{
    public class CleanUpEmptyConversationsJob : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CleanUpEmptyConversationsJob> _logger;

        public CleanUpEmptyConversationsJob(IServiceProvider serviceProvider, ILogger<CleanUpEmptyConversationsJob> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken); // Chạy mỗi 1 giờ
                _logger.LogInformation("🧼 CleanUp job running at: {time}", DateTime.UtcNow);

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var cutoff = DateTime.UtcNow.AddHours(-1); // Xóa các cuộc trò chuyện rỗng cũ hơn 1 giờ

                var emptyChats = await context.CuocTroChuyens
                    .Where(c => c.IsEmpty && c.ThoiGianTao < cutoff)
                    .ToListAsync();

                if (emptyChats.Any())
                {
                    var ids = emptyChats.Select(c => c.MaCuocTroChuyen).ToList();

                    var thamGia = await context.NguoiThamGias
                        .Where(t => ids.Contains(t.MaCuocTroChuyen))
                        .ToListAsync();

                    context.NguoiThamGias.RemoveRange(thamGia);
                    context.CuocTroChuyens.RemoveRange(emptyChats);

                    await context.SaveChangesAsync();
                    _logger.LogInformation($"🧹 Đã xoá {emptyChats.Count} cuộc trò chuyện rỗng quá 1 giờ");
                }
                else
                {
                    _logger.LogInformation("✅ Không có cuộc trò chuyện nào cần xoá.");
                }
            }
        }

    }
}
