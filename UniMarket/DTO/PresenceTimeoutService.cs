using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Hubs;
using Microsoft.Extensions.DependencyInjection; // Cần thêm
using Microsoft.Extensions.Logging; // Cần thêm để log lỗi

public class PresenceTimeoutService : BackgroundService
{
    private readonly UserPresenceService _presenceService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<PresenceTimeoutService> _logger; // Thêm Logger
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public PresenceTimeoutService(
        UserPresenceService presenceService,
        IServiceProvider serviceProvider,
        IHubContext<ChatHub> hubContext,
        ILogger<PresenceTimeoutService> logger) // Thêm Logger
    {
        _presenceService = presenceService;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try // Thêm try-catch để dịch vụ không bị sập
            {
                var now = DateTime.UtcNow;
                var statuses = _presenceService.GetAllStatuses();
                var usersToSetOffline = new List<string>();

                foreach (var (userId, (isOnline, lastActive)) in statuses)
                {
                    if (isOnline && (now - lastActive) > _timeout)
                    {
                        usersToSetOffline.Add(userId);
                    }
                }

                if (usersToSetOffline.Count > 0)
                {
                    // === 1. Update in-memory service ===
                    foreach (var userId in usersToSetOffline)
                    {
                        _presenceService.SetOffline(userId);
                    }

                    // === 2. Update database (ĐÃ TỐI ƯU) ===
                    // Dùng ExecuteUpdateAsync để chạy 1 lệnh SQL duy nhất
                    // Tốc độ nhanh hơn 100x so với cách cũ
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                        await context.Users
                            .Where(u => usersToSetOffline.Contains(u.Id))
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(u => u.IsOnline, false)
                                .SetProperty(u => u.LastOnlineTime, now),
                                stoppingToken);
                    }

                    // === 3. Broadcast status changes (ĐÃ TỐI ƯU) ===
                    // Gửi MỘT tin nhắn chứa TẤT CẢ user thay vì N tin nhắn
                    // Bạn cần cập nhật code Phía Client để xử lý mảng "UserStatusesChanged"
                    var offlineDtos = usersToSetOffline.Select(userId => new
                    {
                        userId = userId,
                        isOnline = false,
                        lastSeen = now
                    }).ToList();

                    await _hubContext.Clients.All.SendAsync("UserStatusesChanged", offlineDtos, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                // Ghi log lỗi và tiếp tục vòng lặp
                // Tránh để lỗi (như Connection Timeout) làm sập toàn bộ BackgroundService
                _logger.LogError(ex, "Lỗi xảy ra trong PresenceTimeoutService.");
            }

            await Task.Delay(10000, stoppingToken); // Check every 10 seconds
        }
    }
}