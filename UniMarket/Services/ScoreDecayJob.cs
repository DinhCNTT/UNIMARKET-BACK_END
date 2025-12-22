public class ScoreDecayJob : BackgroundService
{
    private readonly IServiceProvider _services;

    public ScoreDecayJob(IServiceProvider services)
    {
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Chờ 24 giờ
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);

            try
            {
                using (var scope = _services.CreateScope())
                {
                    var affinityService = scope.ServiceProvider.GetRequiredService<IUserAffinityService>();
                    await affinityService.DecayScoresAsync();
                }
            }
            catch (Exception ex)
            {
                // Log lỗi nếu có
            }
        }
    }
}
