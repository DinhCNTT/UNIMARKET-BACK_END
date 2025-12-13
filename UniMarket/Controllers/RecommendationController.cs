using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.Services.Recommendation;
using UniMarket.DTO;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RecommendationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly VideoRecommendationService _recommendationService;

        public RecommendationController(ApplicationDbContext context, VideoRecommendationService recommendationService)
        {
            _context = context;
            _recommendationService = recommendationService;
        }

        // ==========================================================================================
        // 🎯 API: LẤY DANH SÁCH VIDEO "FOR YOU" (ĐÃ FIX LỖI COUNT VÀ TIME)
        // ==========================================================================================
        [HttpPost("foryou")]
        [AllowAnonymous]
        public async Task<IActionResult> GetForYouVideos([FromBody] ForYouRequestDto request)
        {
            try
            {
                // 1. Lấy UserId nếu đã đăng nhập
                var userId = User.Identity != null && User.Identity.IsAuthenticated
                    ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                    : null;

                // 2. Gọi AI Service để lấy danh sách ID bài đăng phù hợp
                var recommendedIds = await _recommendationService.GetRecommendedPostIds(
                    userId,
                    request.ExcludedIds ?? new List<int>(),
                    request.PageSize,
                    isVideoOnly: true
                );

                // Nếu AI không trả về gì thì return rỗng luôn
                if (!recommendedIds.Any())
                    return Ok(new List<object>());

                // 3. Lấy dữ liệu chi tiết Video từ Database
                // Sử dụng AsNoTracking để tối ưu tốc độ đọc
                var videosData = await _context.TinDangs
                    .AsNoTracking()
                    .Where(t => recommendedIds.Contains(t.MaTinDang))
                    .Include(t => t.NguoiBan)
                    .Include(t => t.AnhTinDangs)
                    .Include(t => t.TinhThanh)
                    .Include(t => t.QuanHuyen)
                    .ToListAsync();

                // Tạo danh sách ID thực tế tìm thấy trong DB (đề phòng AI trả về ID rác)
                var foundIds = videosData.Select(v => v.MaTinDang).ToList();

                // ==================================================================================
                // 4. ĐẾM SỐ LƯỢNG TƯƠNG TÁC (TỐI ƯU HIỆU NĂNG)
                // ==================================================================================

                // Đếm Like (Tym) - Dùng AsNoTracking để lấy số chính xác nhất
                var tymCounts = await _context.VideoLikes
                    .AsNoTracking()
                    .Where(v => foundIds.Contains(v.MaTinDang))
                    .GroupBy(v => v.MaTinDang)
                    .Select(g => new { MaTinDang = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.MaTinDang, x => x.Count);

                // Đếm Bình luận
                var commentCounts = await _context.VideoComments
                    .AsNoTracking()
                    .Where(c => foundIds.Contains(c.MaTinDang))
                    .GroupBy(c => c.MaTinDang)
                    .Select(g => new { MaTinDang = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.MaTinDang, x => x.Count);

                // Đếm Chia sẻ
                var shareCounts = await _context.Shares
                    .AsNoTracking()
                    .Where(s => s.TinDangId.HasValue && foundIds.Contains(s.TinDangId.Value))
                    .GroupBy(s => s.TinDangId.Value)
                    .Select(g => new { MaTinDang = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.MaTinDang, x => x.Count);

                // Đếm Lưu Video
                var videoSaveCounts = await _context.VideoTinDangSaves
                    .AsNoTracking()
                    .Where(s => foundIds.Contains(s.MaTinDang))
                    .GroupBy(s => s.MaTinDang)
                    .Select(g => new { MaTinDang = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.MaTinDang, x => x.Count);

                // Đếm Yêu thích (Favorite)
                var postFavCounts = await _context.TinDangYeuThichs
                    .AsNoTracking()
                    .Where(s => foundIds.Contains(s.MaTinDang))
                    .GroupBy(s => s.MaTinDang)
                    .Select(g => new { MaTinDang = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.MaTinDang, x => x.Count);

                // ==================================================================================
                // 5. LẤY TRẠNG THÁI CÁ NHÂN CỦA USER (Đã Like chưa, Đã Follow chưa...)
                // ==================================================================================
                var userLikedIds = new HashSet<int>();
                var userVideoSavedIds = new HashSet<int>();
                var userPostFavoritedIds = new HashSet<int>();
                var userFollowedAuthors = new HashSet<string>();

                if (!string.IsNullOrEmpty(userId))
                {
                    // User đã like bài nào?
                    userLikedIds = new HashSet<int>(
                        await _context.VideoLikes
                            .AsNoTracking()
                            .Where(v => v.UserId == userId && foundIds.Contains(v.MaTinDang))
                            .Select(v => v.MaTinDang)
                            .ToListAsync()
                    );

                    // User đã lưu video nào?
                    userVideoSavedIds = new HashSet<int>(
                        await _context.VideoTinDangSaves
                            .AsNoTracking()
                            .Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                            .Select(s => s.MaTinDang)
                            .ToListAsync()
                    );

                    // User đã yêu thích bài nào?
                    userPostFavoritedIds = new HashSet<int>(
                        await _context.TinDangYeuThichs
                            .AsNoTracking()
                            .Where(s => s.MaNguoiDung == userId && foundIds.Contains(s.MaTinDang))
                            .Select(s => s.MaTinDang)
                            .ToListAsync()
                    );

                    // User đã follow tác giả nào?
                    var authorIds = videosData.Select(v => v.MaNguoiBan).Distinct().ToList();
                    userFollowedAuthors = new HashSet<string>(
                        await _context.Follows
                            .AsNoTracking()
                            .Where(f => f.FollowerId == userId && authorIds.Contains(f.FollowingId))
                            .Select(f => f.FollowingId)
                            .ToListAsync()
                    );
                }

                // ==================================================================================
                // 6. MAP DỮ LIỆU TRẢ VỀ (GIỮ ĐÚNG THỨ TỰ CỦA recommendedIds)
                // ==================================================================================

                // Sử dụng Join để đảm bảo thứ tự của list kết quả khớp với thứ tự mà AI gợi ý
                var result = recommendedIds
                    .Join(videosData, id => id, v => v.MaTinDang, (id, td) => td)
                    .Select(td =>
                    {
                        // Xử lý Thumbnail (Ưu tiên ảnh Cloudinary generate nếu là video)
                        string? generatedThumb = GetCloudinaryThumbnail(td.VideoUrl);
                        string? productThumb = td.AnhTinDangs?
                            .OrderBy(a => a.Order)
                            .FirstOrDefault()?.DuongDan;

                        string finalImage = !string.IsNullOrEmpty(generatedThumb) ? generatedThumb : (productThumb ?? "");

                        return new
                        {
                            td.MaTinDang,
                            td.TieuDe,
                            td.MoTa,
                            td.VideoUrl,
                            HinhAnh = finalImage,

                            // Thông tin cơ bản
                            td.Gia,
                            td.DiaChi,
                            TinhThanh = td.TinhThanh?.TenTinhThanh ?? "",
                            QuanHuyen = td.QuanHuyen?.TenQuanHuyen ?? "",
                            td.TinhTrang,

                            // Thời gian (Đã sửa logic tiếng Việt)
                            td.NgayDang,
                            TimeAgo = GetVietnameseTimeAgo(td.NgayDang),

                            td.SoLuotXem,
                            AnhCount = td.AnhTinDangs?.Count(a => a.LoaiMedia == MediaType.Image) ?? 0,

                            // Số liệu tương tác (Lấy từ Dictionary đã đếm ở trên)
                            SoTym = tymCounts.ContainsKey(td.MaTinDang) ? tymCounts[td.MaTinDang] : 0,
                            SoBinhLuan = commentCounts.ContainsKey(td.MaTinDang) ? commentCounts[td.MaTinDang] : 0,
                            SoLuotChiaSe = shareCounts.ContainsKey(td.MaTinDang) ? shareCounts[td.MaTinDang] : 0,
                            SoNguoiLuu = videoSaveCounts.ContainsKey(td.MaTinDang) ? videoSaveCounts[td.MaTinDang] : 0,
                            SoLuotYeuThich = postFavCounts.ContainsKey(td.MaTinDang) ? postFavCounts[td.MaTinDang] : 0,

                            // Trạng thái của User đối với Video này
                            IsLiked = userLikedIds.Contains(td.MaTinDang),
                            IsSaved = userVideoSavedIds.Contains(td.MaTinDang),
                            IsFavorited = userPostFavoritedIds.Contains(td.MaTinDang),

                            // Thông tin người đăng
                            NguoiDang = td.NguoiBan != null
                                ? new
                                {
                                    td.NguoiBan.Id,
                                    td.NguoiBan.FullName,
                                    td.NguoiBan.AvatarUrl,
                                    IsFollowed = userFollowedAuthors.Contains(td.NguoiBan.Id)
                                }
                                : null
                        };
                    })
                    .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                // Log lỗi ra console để debug nếu cần
                Console.WriteLine($"Error in GetForYouVideos: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi hệ thống khi lấy danh sách video đề xuất." });
            }
        }

        // ==========================================================
        // 🛠️ HÀM PHỤ TRỢ: TÍNH THỜI GIAN TIẾNG VIỆT
        // ==========================================================
        private string GetVietnameseTimeAgo(DateTime date)
        {
            // Lấy thời gian hiện tại
            var now = DateTime.Now;
            var timeSpan = now - date;

            // Xử lý trường hợp thời gian bị âm (do lệch giờ server/client) -> Coi như vừa xong
            if (timeSpan.TotalSeconds < 0) return "Vừa xong";

            if (timeSpan.TotalMinutes < 1)
                return "Vừa xong";

            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} phút trước";

            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} giờ trước";

            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} ngày trước";

            // Nếu quá 7 ngày, hiển thị ngày tháng năm
            return date.ToString("dd/MM/yyyy");
        }

        // ==========================================================
        // 🛠️ HÀM PHỤ TRỢ: TẠO THUMBNAIL CLOUDINARY
        // ==========================================================
        private string? GetCloudinaryThumbnail(string? videoUrl)
        {
            if (string.IsNullOrEmpty(videoUrl)) return null;

            // Logic lấy ảnh thumbnail tự động từ Cloudinary bằng cách đổi đuôi file
            if (videoUrl.Contains("cloudinary.com"))
            {
                if (videoUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                    videoUrl.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
                {
                    return videoUrl[..videoUrl.LastIndexOf('.')] + ".jpg";
                }

                if (!videoUrl.EndsWith(".jpg") && !videoUrl.EndsWith(".png"))
                {
                    return videoUrl + ".jpg";
                }
            }

            return null;
        }
    }
}