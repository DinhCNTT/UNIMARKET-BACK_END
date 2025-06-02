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
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Chạy mỗi 5 phút
                    _logger.LogInformation("🧼 CleanUp job running at: {time}", DateTime.UtcNow);

                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var cutoff = DateTime.UtcNow.AddMinutes(-3); // Chỉ xóa cuộc trò chuyện rỗng hơn 3 phút

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

                        _logger.LogInformation("🧹 Đã xoá {count} cuộc trò chuyện rỗng hơn 3 phút", emptyChats.Count);
                    }
                    else
                    {
                        _logger.LogInformation("✅ Không có cuộc trò chuyện rỗng nào cần xoá.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Lỗi khi xoá cuộc trò chuyện rỗng.");
                }
            }
        }
    }
}
