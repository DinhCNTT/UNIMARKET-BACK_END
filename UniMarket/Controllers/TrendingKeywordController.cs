using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;

namespace UniMarket.Controllers
{
    /// <summary>
    /// API cho tính năng "Từ khóa phổ biến" trên trang chủ
    /// Hiển thị 4 cột × 7 dòng = 28 từ khóa được tìm kiếm nhiều nhất (sorted by time desc)
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class TrendingKeywordController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TrendingKeywordController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Lấy danh sách từ khóa phổ biến (16 items: 4 cột × 4 dòng)
        /// Sorted by time: giờ, ngày, tháng, năm (mới nhất trước)
        /// </summary>
        [HttpGet("trending")]
        public async Task<IActionResult> GetTrendingKeywords()
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                // Nhóm từ khóa theo khoảng thời gian: Hôm nay, Hôm qua, Tuần này, Tháng này, Năm nay, Cũ hơn
                var keywords = await _context.SearchHistories
                    .AsNoTracking()
                    .GroupBy(s => s.Keyword)
                    .Select(g => new
                    {
                        Keyword = g.Key,
                        Count = g.Count(),
                        LatestSearchAt = g.Max(s => s.CreatedAt)
                    })
                    .OrderByDescending(k => k.Count)              // Sắp xếp theo số lần tìm (phổ biến nhất)
                    .ThenByDescending(k => k.LatestSearchAt)      // Rồi theo thời gian mới nhất
                    .Take(16) // 4 cột × 4 dòng
                    .ToListAsync();

                // Phân loại theo thời gian
                var result = keywords.GroupBy(k =>
                {
                    var daysDiff = (now - k.LatestSearchAt).TotalDays;
                    var hoursDiff = (now - k.LatestSearchAt).TotalHours;

                    if (hoursDiff < 24) return "Hôm nay";
                    if (daysDiff < 2) return "Hôm qua";
                    if (daysDiff < 7) return "Tuần này";
                    if (daysDiff < 30) return "Tháng này";
                    if (daysDiff < 365) return "Năm nay";
                    return "Cũ hơn";
                })
                .OrderBy(g =>
                {
                    return g.Key switch
                    {
                        "Hôm nay" => 0,
                        "Hôm qua" => 1,
                        "Tuần này" => 2,
                        "Tháng này" => 3,
                        "Năm nay" => 4,
                        _ => 5
                    };
                })
                .Select(g => new
                {
                    TimePeriod = g.Key,
                    Keywords = g.Select(k => new
                    {
                        Keyword = k.Keyword,
                        SearchCount = k.Count,
                        LastSearchAt = k.LatestSearchAt
                    }).ToList()
                })
                .ToList();

                return Ok(new
                {
                    success = true,
                    data = result,
                    totalCount = keywords.Count,
                    timestamp = now
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy từ khóa phổ biến dạng danh sách đơn giản (16 items)
        /// Dùng cho layout 4 cột × 4 dòng
        /// Chỉ lấy dữ liệu từ 7 ngày qua để trends luôn tươi mới
        /// </summary>
        [HttpGet("trending-simple")]
        public async Task<IActionResult> GetTrendingKeywordsSimple()
        {
            try
            {
                // Lọc dữ liệu 7 ngày gần đây để trend luôn tươi mới
                var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7);

                var keywords = await _context.SearchHistories
                    .AsNoTracking()
                    .Where(s => s.CreatedAt >= sevenDaysAgo)  // Chỉ lấy 7 ngày gần đây
                    .GroupBy(s => s.Keyword)
                    .Select(g => new
                    {
                        Keyword = g.Key,
                        Count = g.Count(),
                        LatestSearchAt = g.Max(s => s.CreatedAt)
                    })
                    .OrderByDescending(k => k.Count)  // Sắp xếp theo số lần tìm kiếm (phổ biến nhất trước)
                    .ThenByDescending(k => k.LatestSearchAt)  // Sau đó là thời gian mới nhất
                    .Take(16)
                    .Select(k => new
                    {
                        keyword = k.Keyword,
                        searchCount = k.Count,
                        lastSearchAt = k.LatestSearchAt
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = keywords,
                    count = keywords.Count
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Lưu lịch sử tìm kiếm của user
        /// Call: POST /api/trendingkeyword/save-search
        /// Body: { "keyword": "iphone" }
        /// </summary>
        [Authorize]
        [HttpPost("save-search")]
        public async Task<IActionResult> SaveSearch([FromBody] SaveSearchRequest request)
        {
            try
            {
                // Validate input
                if (request == null || string.IsNullOrWhiteSpace(request.Keyword))
                {
                    return BadRequest(new { success = false, message = "Keyword cannot be empty" });
                }

                // Lấy userId từ JWT token
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "User not authenticated" });
                }

                // Chuẩn hóa keyword (không convert lowercase để giữ nguyên cách user gõ)
                var trimmedKeyword = request.Keyword.Trim();
                var lowerKeyword = trimmedKeyword.ToLower();

                // Check if exists (case-insensitive) and update if found
                var existing = await _context.SearchHistories
                    .Where(sh => sh.UserId == userId && sh.Keyword.ToLower() == lowerKeyword)
                    .FirstOrDefaultAsync();

                SearchHistory searchHistory;
                if (existing != null)
                {
                    // Update timestamp but keep original case
                    existing.CreatedAt = DateTimeOffset.UtcNow;
                    _context.SearchHistories.Update(existing);
                    searchHistory = existing;
                }
                else
                {
                    // Create new with original case
                    searchHistory = new SearchHistory
                    {
                        UserId = userId,
                        Keyword = trimmedKeyword,
                        CreatedAt = DateTimeOffset.UtcNow,
                        DeviceName = request.DeviceName,
                        IpAddress = request.IpAddress
                    };
                    _context.SearchHistories.Add(searchHistory);
                }
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Search history saved successfully",
                    data = new
                    {
                        keyword = searchHistory.Keyword,
                        savedAt = searchHistory.CreatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Request model cho SaveSearch
        /// </summary>
        public class SaveSearchRequest
        {
            public string Keyword { get; set; } = null!;
            public string? DeviceName { get; set; }
            public string? IpAddress { get; set; }
        }
    }
}