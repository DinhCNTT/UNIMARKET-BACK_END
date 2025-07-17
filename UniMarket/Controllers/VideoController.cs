using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.DTO;
namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VideoController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public VideoController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetVideos([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var userId = User.Identity != null && User.Identity.IsAuthenticated
                ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;

            // Lấy tất cả tin đăng có video đã duyệt
            var tinDangsQuery = _context.TinDangs
                .Where(td => td.VideoUrl != null && td.TrangThai == TrangThaiTinDang.DaDuyet)
                .Include(td => td.NguoiBan)
                .Include(td => td.TinhThanh)
                .Include(td => td.QuanHuyen);

            var tinDangs = await tinDangsQuery.ToListAsync();
            var maTinDangList = tinDangs.Select(td => td.MaTinDang).ToList();

            // Lấy số tym và số bình luận
            var tymCounts = await _context.VideoLikes
                .Where(v => maTinDangList.Contains(v.MaTinDang))
                .GroupBy(v => v.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            var binhLuanCounts = await _context.VideoComments
                .Where(c => maTinDangList.Contains(c.MaTinDang))
                .GroupBy(c => c.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            var likedVideoIds = !string.IsNullOrEmpty(userId)
                ? await _context.VideoLikes
                    .Where(v => v.UserId == userId && maTinDangList.Contains(v.MaTinDang))
                    .Select(v => v.MaTinDang)
                    .ToListAsync()
                : new List<int>();

            // Tạo danh sách kết quả kèm theo số tym + cmt để sort
            var result = tinDangs
                .Select(td => new
                {
                    td.MaTinDang,
                    td.TieuDe,
                    td.VideoUrl,
                    td.Gia,
                    DiaChi = td.DiaChi,
                    TinhThanh = td.TinhThanh?.TenTinhThanh,
                    QuanHuyen = td.QuanHuyen?.TenQuanHuyen,
                    SoTym = tymCounts.GetValueOrDefault(td.MaTinDang, 0),
                    SoBinhLuan = binhLuanCounts.GetValueOrDefault(td.MaTinDang, 0),
                    TongScore = tymCounts.GetValueOrDefault(td.MaTinDang, 0) + binhLuanCounts.GetValueOrDefault(td.MaTinDang, 0),
                    NguoiDang = td.NguoiBan != null ? new
                    {
                        td.NguoiBan.Id,
                        td.NguoiBan.FullName,
                        td.NguoiBan.AvatarUrl
                    } : null,
                    IsLiked = likedVideoIds.Contains(td.MaTinDang)
                })
                .OrderByDescending(x => x.TongScore)
                .ThenByDescending(x => x.MaTinDang) // fallback nếu điểm bằng nhau
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(result);
        }
        [AllowAnonymous]
        [HttpGet("{maTinDang}")]
        public async Task<IActionResult> GetVideoDetail(int maTinDang)
        {
            var tin = await _context.TinDangs
                .Include(td => td.NguoiBan)
                .Include(td => td.TinhThanh)
                .Include(td => td.QuanHuyen)
                .FirstOrDefaultAsync(td => td.MaTinDang == maTinDang && td.VideoUrl != null);

            if (tin == null)
                return NotFound();

            // Mặc định là chưa tym
            bool isLiked = false;

            // Nếu người dùng đã đăng nhập, kiểm tra đã tym hay chưa
            if (User.Identity.IsAuthenticated)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(userId))
                {
                    isLiked = await _context.VideoLikes
                        .AnyAsync(v => v.MaTinDang == maTinDang && v.UserId == userId);
                }
            }

            // Trả về thông tin video
            var result = new
            {
                tin.MaTinDang,
                tin.TieuDe,
                tin.VideoUrl,
                tin.Gia,
                DiaChi = tin.DiaChi,
                TinhThanh = tin.TinhThanh?.TenTinhThanh,
                QuanHuyen = tin.QuanHuyen?.TenQuanHuyen,
                SoTym = await _context.VideoLikes.CountAsync(v => v.MaTinDang == tin.MaTinDang),
                SoBinhLuan = await _context.VideoComments.CountAsync(c => c.MaTinDang == tin.MaTinDang),
                IsLiked = isLiked,
                NguoiDang = tin.NguoiBan != null ? new
                {
                    tin.NguoiBan.Id,
                    tin.NguoiBan.FullName,
                    tin.NguoiBan.AvatarUrl
                } : null,
                BinhLuans = await _context.VideoComments
                    .Where(c => c.MaTinDang == tin.MaTinDang)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => new
                    {
                        c.Content,
                        c.CreatedAt,
                        NguoiDung = new
                        {
                            c.User.Id,
                            c.User.FullName,
                            c.User.AvatarUrl
                        }
                    }).ToListAsync()
            };

            return Ok(result);
        }


        [Authorize]
        [HttpPost("{maTinDang}/like")]
        public async Task<IActionResult> LikeOrUnlikeVideo(int maTinDang)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var existing = await _context.VideoLikes
                .FirstOrDefaultAsync(x => x.MaTinDang == maTinDang && x.UserId == userId);

            bool isLiked;

            if (existing != null)
            {
                _context.VideoLikes.Remove(existing);
                isLiked = false;
            }
            else
            {
                _context.VideoLikes.Add(new VideoLike
                {
                    MaTinDang = maTinDang,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
                isLiked = true;
            }

            await _context.SaveChangesAsync();

            var soTym = await _context.VideoLikes.CountAsync(x => x.MaTinDang == maTinDang);

            return Ok(new
            {
                isLiked,
                soTym
            });
        }

        [Authorize]
        [HttpPost("{maTinDang}/comment")]
        public async Task<IActionResult> CommentVideo(int maTinDang, [FromBody] CreateVideoCommentDto model)
        {
            if (string.IsNullOrWhiteSpace(model.Content))
                return BadRequest("Nội dung bình luận không được để trống.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var tinDang = await _context.TinDangs.FindAsync(maTinDang);
            if (tinDang == null)
                return NotFound("Tin đăng không tồn tại.");

            if (model.ParentCommentId.HasValue)
            {
                var parent = await _context.VideoComments.FindAsync(model.ParentCommentId.Value);
                if (parent == null)
                    return NotFound("Bình luận cha không tồn tại.");
                if (parent.MaTinDang != maTinDang)
                    return BadRequest("Bình luận cha không thuộc về tin đăng này.");
            }

            var comment = new VideoComment
            {
                MaTinDang = maTinDang,
                UserId = userId,
                Content = model.Content,
                CreatedAt = DateTime.UtcNow,
                ParentCommentId = model.ParentCommentId
            };

            _context.VideoComments.Add(comment);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                comment.Id,
                comment.Content,
                comment.CreatedAt,
                comment.ParentCommentId,
                IsReply = comment.ParentCommentId.HasValue,
                message = "Đã thêm bình luận thành công."
            });
        }

        [HttpGet("{maTinDang}/comments")]
        [AllowAnonymous]
        public async Task<IActionResult> GetComments(int maTinDang)
        {
            var allComments = await _context.VideoComments
                .Where(c => c.MaTinDang == maTinDang)
                .Include(c => c.User)
                .ToListAsync();

            var commentTree = BuildCommentTree(allComments);

            return Ok(commentTree);
        }

        // ✅ Hàm dựng bình luận nhiều cấp lồng nhau (cha -> con -> cháu...)
        private List<VideoCommentDto> BuildCommentTree(List<VideoComment> allComments, int? parentId = null)
        {
            return allComments
                .Where(c => c.ParentCommentId == parentId)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new VideoCommentDto
                {
                    Id = c.Id,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt,
                    UserId = c.UserId,
                    UserName = c.User.FullName ?? c.User.UserName,
                    AvatarUrl = c.User.AvatarUrl,
                    Replies = BuildCommentTree(allComments, c.Id)
                })
                .ToList();
        }
        [Authorize]
        [HttpDelete("comment/{commentId}")]
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Load comment kèm replies (bình luận con)
            var comment = await _context.VideoComments
                .Include(c => c.Replies)
                .FirstOrDefaultAsync(c => c.Id == commentId);

            if (comment == null)
                return NotFound("Bình luận không tồn tại.");

            // Kiểm tra quyền xóa: chỉ người tạo mới được xóa
            if (comment.UserId != userId)
                return Forbid("Bạn không có quyền xóa bình luận này.");

            // Xóa đệ quy các bình luận con
            await DeleteCommentRecursive(comment);

            // Lưu thay đổi vào DB
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa bình luận thành công." });
        }

        // Hàm đệ quy xóa comment và toàn bộ reply của nó
        private async Task DeleteCommentRecursive(VideoComment comment)
        {
            // Dùng ToList() để tránh lỗi sửa đổi collection khi duyệt
            foreach (var reply in comment.Replies.ToList())
            {
                // Load replies của reply
                var replyWithChildren = await _context.VideoComments
                    .Include(r => r.Replies)
                    .FirstOrDefaultAsync(r => r.Id == reply.Id);

                if (replyWithChildren != null)
                {
                    await DeleteCommentRecursive(replyWithChildren);
                }
            }

            _context.VideoComments.Remove(comment);
        }
        // tìm kiếm video
        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchVideos([FromQuery] string keyword, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Từ khóa tìm kiếm không được để trống.");

            var userId = User.Identity != null && User.Identity.IsAuthenticated
                ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;

            if (!string.IsNullOrEmpty(userId))
            {
                var searchHistory = new SearchHistory
                {
                    UserId = userId,
                    Keyword = keyword,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SearchHistories.Add(searchHistory);
                await _context.SaveChangesAsync();
            }

            keyword = keyword.ToLower();

            // 1. Lấy toàn bộ video phù hợp (chưa phân trang)
            var tinDangsRaw = await _context.TinDangs
                .Where(td => td.VideoUrl != null &&
                             td.TrangThai == TrangThaiTinDang.DaDuyet &&
                             EF.Functions.Like(td.TieuDe.ToLower(), $"%{keyword}%"))
                .Include(td => td.NguoiBan)
                .Include(td => td.TinhThanh)
                .Include(td => td.QuanHuyen)
                .ToListAsync();

            var maTinDangList = tinDangsRaw.Select(td => td.MaTinDang).ToList();

            // 2. Lấy số tym và số bình luận
            var tymCounts = await _context.VideoLikes
                .Where(v => maTinDangList.Contains(v.MaTinDang))
                .GroupBy(v => v.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            var binhLuanCounts = await _context.VideoComments
                .Where(c => maTinDangList.Contains(c.MaTinDang))
                .GroupBy(c => c.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // 3. Sắp xếp lại theo (tym + bình luận) giảm dần
            var tinDangsSorted = tinDangsRaw
                .OrderByDescending(td => tymCounts.GetValueOrDefault(td.MaTinDang, 0) + binhLuanCounts.GetValueOrDefault(td.MaTinDang, 0))
                .ToList();

            // 4. Phân trang
            var pagedTinDangs = tinDangsSorted
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var likedVideoIds = !string.IsNullOrEmpty(userId)
                ? await _context.VideoLikes
                    .Where(v => v.UserId == userId && maTinDangList.Contains(v.MaTinDang))
                    .Select(v => v.MaTinDang)
                    .ToListAsync()
                : new List<int>();

            var result = pagedTinDangs.Select(td => new VideoSearchResultDto
            {
                MaTinDang = td.MaTinDang,
                TieuDe = td.TieuDe,
                VideoUrl = td.VideoUrl,
                Gia = td.Gia,
                DiaChi = td.DiaChi,
                TinhThanh = td.TinhThanh?.TenTinhThanh,
                QuanHuyen = td.QuanHuyen?.TenQuanHuyen,
                SoTym = tymCounts.GetValueOrDefault(td.MaTinDang, 0),
                SoBinhLuan = binhLuanCounts.GetValueOrDefault(td.MaTinDang, 0),
                NguoiDang = td.NguoiBan == null ? null : new UserSummaryDto
                {
                    Id = td.NguoiBan.Id,
                    FullName = td.NguoiBan.FullName,
                    AvatarUrl = td.NguoiBan.AvatarUrl
                },
                IsLiked = likedVideoIds.Contains(td.MaTinDang)
            });

            return Ok(new
            {
                TotalItems = tinDangsSorted.Count,
                Page = page,
                PageSize = pageSize,
                Items = result
            });
        }
        // hàm gợi ý tìm kiếm từ khóa 
        [HttpGet("suggest-keywords")]
        [AllowAnonymous]
        public async Task<IActionResult> SuggestKeywords([FromQuery] string keyword, [FromQuery] int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Từ khóa không được để trống.");

            var suggestions = await _context.TinDangs
                .Where(td => td.VideoUrl != null &&
                             td.TrangThai == TrangThaiTinDang.DaDuyet &&
                             td.TieuDe.Contains(keyword))
                .Select(td => td.TieuDe)
                .Distinct()
                .OrderBy(t => t)
                .Take(limit)
                .ToListAsync();

            return Ok(suggestions);
        }

        [HttpGet("search-users")]
        [AllowAnonymous]
        public async Task<IActionResult> SearchUsersByVideoKeyword([FromQuery] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Từ khóa tìm kiếm không được để trống.");

            keyword = keyword.ToLower();

            // Truy vấn người dùng đã đăng video có tiêu đề chứa keyword
            var users = await _context.TinDangs
                .Where(td =>
                    td.VideoUrl != null &&
                    td.TrangThai == TrangThaiTinDang.DaDuyet &&
                    EF.Functions.Like(td.TieuDe.ToLower(), $"%{keyword}%") &&
                    td.NguoiBan != null)
                .Select(td => new
                {
                    td.NguoiBan.Id,
                    td.NguoiBan.FullName,
                    td.NguoiBan.AvatarUrl
                })
                .Distinct()
                .ToListAsync();

            return Ok(users);
        }

        [AllowAnonymous]
        [HttpGet("search-users-sorted")]
        public async Task<IActionResult> SearchUsersByVideoKeywordSorted([FromQuery] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Từ khóa tìm kiếm không được để trống.");

            keyword = keyword.ToLower();

            // Lấy danh sách video thỏa điều kiện
            var videosQuery = _context.TinDangs
                .Where(td =>
                    !string.IsNullOrEmpty(td.VideoUrl) &&
                    td.TrangThai == TrangThaiTinDang.DaDuyet &&
                    !string.IsNullOrEmpty(td.TieuDe) &&
                    EF.Functions.Like(td.TieuDe.ToLower(), $"%{keyword}%") &&
                    td.NguoiBan != null);

            var videoInfos = await videosQuery
                .Select(td => new
                {
                    td.MaTinDang,
                    UserId = td.NguoiBan.Id
                })
                .ToListAsync();

            var maTinDangList = videoInfos.Select(v => v.MaTinDang).ToList();

            // Đếm lượt tym cho từng video
            var tymCounts = await _context.VideoLikes
                .Where(v => maTinDangList.Contains(v.MaTinDang))
                .GroupBy(v => v.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Đếm lượt bình luận cho từng video
            var binhLuanCounts = await _context.VideoComments
                .Where(c => maTinDangList.Contains(c.MaTinDang))
                .GroupBy(c => c.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Tính tổng lượt tym + bình luận cho mỗi người dùng
            var userScores = videoInfos
                .GroupBy(v => v.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    TotalScore = g.Sum(v => tymCounts.GetValueOrDefault(v.MaTinDang, 0) + binhLuanCounts.GetValueOrDefault(v.MaTinDang, 0))
                })
                .OrderByDescending(u => u.TotalScore)
                .ToList();

            var userIdsOrdered = userScores.Select(u => u.UserId).ToList();

            var users = await _context.Users
                .Where(u => userIdsOrdered.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.AvatarUrl
                })
                .ToListAsync();

            // Sắp xếp lại danh sách users theo thứ tự điểm đã tính
            var result = userIdsOrdered
                .Join(users, id => id, u => u.Id, (id, u) => u)
                .ToList();

            return Ok(result);
        }

    }
}