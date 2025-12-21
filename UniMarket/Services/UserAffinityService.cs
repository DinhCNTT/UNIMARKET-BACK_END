using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection; // Cần thiết để tạo Scope
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.DTOs; // Đảm bảo đã import namespace chứa SmartUserDTO

namespace UniMarket.Services
{
    public class UserAffinityService : IUserAffinityService
    {
        // 1. Factory để tạo DbContext mới cho các tác vụ Ghi (Write)
        private readonly IServiceScopeFactory _scopeFactory;

        // 2. DbContext thường để dùng cho hàm Đọc (Read) nhanh
        private readonly ApplicationDbContext _readContext;

        // 3. Cache để chống spam click
        private readonly IMemoryCache _cache;

        public UserAffinityService(
            IServiceScopeFactory scopeFactory,
            ApplicationDbContext readContext,
            IMemoryCache cache)
        {
            _scopeFactory = scopeFactory;
            _readContext = readContext;
            _cache = cache;
        }

        // =========================================================================
        // HÀM GHI (WRITE): SỬ DỤNG SCOPE MỚI ĐỂ TRÁNH LỖI CONCURRENCY
        // =========================================================================
        public async Task TrackInteractionAsync(string sourceUserId, string targetUserId, InteractionType type)
        {
            if (sourceUserId == targetUserId) return;

            // --- 1. LOGIC CACHE (Chống Spam) ---
            string cacheKey = $"intr:{sourceUserId}:{targetUserId}:{(int)type}";

            int interactionCount = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return 0;
            });

            // Tính điểm cộng thêm (Giảm dần nếu spam nhiều)
            double scoreToAdd = (int)type;
            if (interactionCount > 5) scoreToAdd = scoreToAdd * 0.5;
            if (interactionCount > 20) scoreToAdd = 0;

            // Tăng bộ đếm trong Cache
            _cache.Set(cacheKey, interactionCount + 1);

            if (scoreToAdd <= 0) return;

            // --- 2. XỬ LÝ DB AN TOÀN VỚI SCOPE ---
            // Tạo một phạm vi (scope) mới hoàn toàn độc lập để ghi dữ liệu
            using (var scope = _scopeFactory.CreateScope())
            {
                // Trong phạm vi này, xin cấp mới một ApplicationDbContext riêng
                var dbContextRieng = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Dùng dbContextRieng để thao tác, KHÔNG dùng _readContext chung
                var affinity = await dbContextRieng.UserAffinities
                    .FirstOrDefaultAsync(x => x.SourceUserId == sourceUserId && x.TargetUserId == targetUserId);

                if (affinity == null)
                {
                    affinity = new UserAffinity
                    {
                        SourceUserId = sourceUserId,
                        TargetUserId = targetUserId,
                        AffinityScore = scoreToAdd,
                        LastInteraction = DateTime.UtcNow
                    };
                    dbContextRieng.UserAffinities.Add(affinity);
                }
                else
                {
                    affinity.AffinityScore += scoreToAdd;
                    affinity.LastInteraction = DateTime.UtcNow;
                }

                // Lưu lại bằng context riêng này
                await dbContextRieng.SaveChangesAsync();
            }
            // Kết thúc using -> dbContextRieng tự hủy -> An toàn tuyệt đối
        }

        // =========================================================================
        // HÀM ĐỌC (READ): ĐÃ CẬP NHẬT LOGIC BƯỚC 3 (TRÁNH LỖI NULL)
        // =========================================================================
        public async Task<List<SmartUserDTO>> GetSmartSortedFollowingAsync(string currentUserId, int page, int pageSize)
        {
            var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);

            // 1. Lấy danh sách ID những người mình đang follow (Status = Accepted)
            // Dùng _readContext vì đây là tác vụ đọc đơn giản
            var followingIds = await _readContext.Follows
                .AsNoTracking() // Tối ưu hiệu năng đọc
                .Where(f => f.FollowerId == currentUserId && f.Status == FollowStatus.Accepted)
                .Select(f => f.FollowingId)
                .ToListAsync();

            // Nếu không follow ai, trả về rỗng ngay để đỡ tốn tài nguyên
            if (!followingIds.Any()) return new List<SmartUserDTO>();

            // 2. Query chính: Lấy User + Điểm Affinity (Dùng Subquery thay vì Join để tránh lỗi Null)
            var query = _readContext.Users
                .AsNoTracking()
                .Where(u => followingIds.Contains(u.Id))
                .Select(u => new
                {
                    User = u,
                    // Lấy điểm thân thiết (nếu không có bản ghi thì mặc định là 0 hoặc null, EF tự xử lý)
                    AffinityScore = _readContext.UserAffinities
                        .Where(ua => ua.SourceUserId == currentUserId && ua.TargetUserId == u.Id)
                        .Select(ua => ua.AffinityScore)
                        .FirstOrDefault()
                });

            // 3. Sắp xếp và Phân trang
            var sortedUsers = await query
                .OrderByDescending(x => x.User.LastOnlineTime >= fiveMinutesAgo) // Ưu tiên Online trước
                .ThenByDescending(x => x.AffinityScore) // Sau đó đến điểm thân thiết
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new SmartUserDTO
                {
                    UserId = x.User.Id,
                    FullName = x.User.FullName,
                    AvatarUrl = x.User.AvatarUrl,
                    IsOnline = x.User.LastOnlineTime >= fiveMinutesAgo,
                    MatchScore = x.AffinityScore,
                    PhoneNumber = x.User.PhoneNumber

                })
                .ToListAsync();

            return sortedUsers;
        }

        // =========================================================================
        // HÀM JOB: GIẢM ĐIỂM THEO THỜI GIAN
        // =========================================================================
        public async Task DecayScoresAsync()
        {
            // Hàm này chạy trong Background Service, nên bắt buộc phải tạo Scope
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Giảm 5% điểm của những ai có điểm > 1
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE UserAffinities SET AffinityScore = AffinityScore * 0.95 WHERE AffinityScore > 1");
            }
        }
    }
}