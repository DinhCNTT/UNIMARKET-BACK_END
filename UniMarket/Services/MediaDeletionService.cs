using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using UniMarket.DataAccess;
using UniMarket.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using CloudinaryDotNet.Actions;

namespace UniMarket.Services
{
    // Service này sẽ chạy ngầm
    public class MediaDeletionService : IHostedService, IDisposable
    {
        private Timer _timer;
        private readonly ILogger<MediaDeletionService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public MediaDeletionService(ILogger<MediaDeletionService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Background Media Deletion Service is starting.");

            // Service sẽ chạy ngay lập tức khi khởi động,
            // và lặp lại sau mỗi 1 giờ
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromHours(1));

            return Task.CompletedTask;
        }

        private async void DoWork(object state)
        {
            _logger.LogInformation("Checking for rejected posts to delete media...");

            // Vì service này là "Singleton", chúng ta cần tạo "Scope"
            // để lấy DbContext và PhotoService (là "Scoped")
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var photoService = scope.ServiceProvider.GetRequiredService<PhotoService>();

                // Tìm tất cả tin đăng bị từ chối VÀ đã đến ngày hẹn xóa
                var postsToDelete = await context.TinDangs
                    .Include(p => p.AnhTinDangs)
                    .Where(p => p.TrangThai == TrangThaiTinDang.TuChoi &&
                                p.NgayHenXoa != null &&
                                p.NgayHenXoa <= DateTime.UtcNow) // Đã đến hạn
                    .ToListAsync();

                if (!postsToDelete.Any())
                {
                    _logger.LogInformation("No media to delete.");
                    return;
                }

                _logger.LogInformation($"Found {postsToDelete.Count} posts to process for media deletion.");

                foreach (var post in postsToDelete)
                {
                    // 1. Xóa media trên Cloudinary
                    foreach (var media in post.AnhTinDangs)
                    {
                        if (string.IsNullOrEmpty(media.DuongDan) || !media.DuongDan.StartsWith("http"))
                            continue;

                        var resourceType = (media.LoaiMedia == MediaType.Video)
                            ? ResourceType.Video
                            : ResourceType.Image;

                        await photoService.DeleteMediaByUrlAsync(media.DuongDan, resourceType);
                    }

                    // 2. Xóa record media trong DB
                    context.AnhTinDangs.RemoveRange(post.AnhTinDangs);

                    // 3. Cập nhật lại tin đăng (xóa ngày hẹn đi để không chạy lại)
                    post.NgayHenXoa = null;
                    context.TinDangs.Update(post);

                    _logger.LogInformation($"Successfully deleted media for Post ID: {post.MaTinDang}");
                }

                // 4. Lưu tất cả thay đổi
                await context.SaveChangesAsync();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Background Media Deletion Service is stopping.");
            _timer?.Change(Timeout.Infinite, 0); // Ngừng timer
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}