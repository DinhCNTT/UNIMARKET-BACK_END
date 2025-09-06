using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Hubs;

public class PresenceTimeoutService : BackgroundService
{
    private readonly UserPresenceService _presenceService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public PresenceTimeoutService(
        UserPresenceService presenceService,
        IServiceProvider serviceProvider,
        IHubContext<ChatHub> hubContext)
    {
        _presenceService = presenceService;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
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
                // Update in-memory service
                foreach (var userId in usersToSetOffline)
                {
                    _presenceService.SetOffline(userId);
                }

                // Update database
                using (var scope = _serviceProvider.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var users = await context.Users
                        .Where(u => usersToSetOffline.Contains(u.Id))
                        .ToListAsync();

                    foreach (var user in users)
                    {
                        user.IsOnline = false;
                        user.LastOnlineTime = now;
                    }

                    await context.SaveChangesAsync();
                }

                // Broadcast status changes
                foreach (var userId in usersToSetOffline)
                {
                    await _hubContext.Clients.All.SendAsync("UserStatusChanged", new
                    {
                        userId = userId,
                        isOnline = false,
                        lastSeen = now
                    }, stoppingToken);
                }
            }

            await Task.Delay(10000, stoppingToken); // Check every 10 seconds
        }
    }
}