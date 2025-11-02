using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.DTO;
using Microsoft.AspNetCore.Identity;
using UniMarket.Helpers;
using UniMarket.Extensions;
namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VideoController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public VideoController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;

        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetVideos([FromQuery] int page = 1,
                                                 [FromQuery] int pageSize = 15,
                                                 [FromQuery] int? categoryId = null,
                                                 [FromQuery] decimal? minPrice = null,
                                                 [FromQuery] decimal? maxPrice = null)
        {
            var userId = User.Identity != null && User.Identity.IsAuthenticated
                ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;

            // ✅ LỖI ĐƯỢC SỬA TẠI ĐÂY
            // Khai báo rõ kiểu 'IQueryable<TinDang>' thay vì dùng 'var'
            IQueryable<TinDang> tinDangsQuery = _context.TinDangs
                .Where(td => td.VideoUrl != null && td.TrangThai == TrangThaiTinDang.DaDuyet)
                .Include(td => td.NguoiBan)
                .Include(td => td.TinhThanh)
                .Include(td => td.QuanHuyen)
                .Include(td => td.AnhTinDangs)
                .Include(td => td.DanhMuc); // Phải Include DanhMuc ở đây

            // Áp dụng các bộ lọc (filter)
            if (categoryId.HasValue)
            {
                // Giờ đây .Where() trả về IQueryable<TinDang>, khớp với kiểu của biến
                tinDangsQuery = tinDangsQuery.Where(td =>
                    td.MaDanhMuc == categoryId.Value ||
                    (td.DanhMuc != null && td.DanhMuc.MaDanhMucCha == categoryId.Value)
                );
            }

            if (minPrice.HasValue && minPrice.Value > 0)
            {
                tinDangsQuery = tinDangsQuery.Where(td => td.Gia >= minPrice.Value);
            }

            if (maxPrice.HasValue && maxPrice.Value < 100000000)
            {
                tinDangsQuery = tinDangsQuery.Where(td => td.Gia <= maxPrice.Value);
            }

            // (Toàn bộ code còn lại giữ nguyên...)

            var tinDangs = await tinDangsQuery.ToListAsync();
            var maTinDangList = tinDangs.Select(td => td.MaTinDang).ToList();

            // Lấy số lượt like
            var tymCounts = await _context.VideoLikes
                .Where(v => maTinDangList.Contains(v.MaTinDang))
                .GroupBy(v => v.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Lấy số lượt bình luận
            var binhLuanCounts = await _context.VideoComments
                .Where(c => maTinDangList.Contains(c.MaTinDang))
                .GroupBy(c => c.MaTinDang)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Lấy danh sách video đã like của user
            var likedVideoIds = !string.IsNullOrEmpty(userId)
                ? await _context.VideoLikes
                    .Where(v => v.UserId == userId && maTinDangList.Contains(v.MaTinDang))
                    .Select(v => v.MaTinDang)
                    .ToListAsync()
                : new List<int>();

            // Chuẩn bị dữ liệu trả về
            var result = tinDangs
                .Select(td => new
                {
                    td.MaTinDang,
                    td.TieuDe,
                    td.MoTa,
                    td.VideoUrl,
                    td.Gia,
                    DiaChi = td.DiaChi,
                    TinhThanh = td.TinhThanh?.TenTinhThanh,
                    QuanHuyen = td.QuanHuyen?.TenQuanHuyen,
                    td.TinhTrang,
                    td.NgayDang,

                    AnhCount = td.AnhTinDangs != null
                        ? td.AnhTinDangs.Count(a => a.LoaiMedia == MediaType.Image)
                        : 0,

                    AnhUrls = td.AnhTinDangs != null
                        ? td.AnhTinDangs
                            .Where(a => a.LoaiMedia == MediaType.Image)
                            .Select(a => a.DuongDan.StartsWith("http")
                                ? a.DuongDan
                                : $"http://localhost:5133{a.DuongDan}")
                            .ToList()
                        : new List<string>(),

                    SoTym = tymCounts.GetValueOrDefault(td.MaTinDang, 0),
                    SoBinhLuan = binhLuanCounts.GetValueOrDefault(td.MaTinDang, 0),
                    SoLuotXem = td.SoLuotXem,
                    TongScore = tymCounts.GetValueOrDefault(td.MaTinDang, 0) +
                                binhLuanCounts.GetValueOrDefault(td.MaTinDang, 0) +
                                td.SoLuotXem,
                    NguoiDang = td.NguoiBan != null ? new
                    {
                        td.NguoiBan.Id,
                        td.NguoiBan.FullName,
                        td.NguoiBan.AvatarUrl
                    } : null,
                    IsLiked = likedVideoIds.Contains(td.MaTinDang)
                })
                .OrderByDescending(x => x.TongScore)
                .ThenByDescending(x => x.MaTinDang)
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

            bool isLiked = false;

            if (User.Identity.IsAuthenticated)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(userId))
                {
                    isLiked = await _context.VideoLikes
                        .AnyAsync(v => v.MaTinDang == maTinDang && v.UserId == userId);
                }
            }

            var result = new
            {
                tin.MaTinDang,
                tin.TieuDe,
                tin.MoTa, // Thêm trường này
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
        // ham lay video video da tym
        [HttpGet("liked")]
        [Authorize]
        public async Task<IActionResult> GetLikedVideos()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var likedVideos = await _context.VideoLikes
                .Where(v => v.UserId == user.Id)
                .OrderByDescending(v => v.CreatedAt)
                .Select(v => new
                {
                    v.MaTinDang,
                    v.TinDang.TieuDe,
                    v.TinDang.VideoUrl,
                    v.TinDang.Gia,
                    v.TinDang.DiaChi,
                    TinhThanh = v.TinDang.TinhThanh != null ? v.TinDang.TinhThanh.TenTinhThanh : null,
                    QuanHuyen = v.TinDang.QuanHuyen != null ? v.TinDang.QuanHuyen.TenQuanHuyen : null,
                    SoTym = _context.VideoLikes.Count(x => x.MaTinDang == v.MaTinDang),
                    SoBinhLuan = _context.VideoComments.Count(x => x.MaTinDang == v.MaTinDang),
                    NguoiDang = new
                    {
                        v.TinDang.NguoiBan.Id,
                        v.TinDang.NguoiBan.FullName,
                        v.TinDang.NguoiBan.AvatarUrl
                    },
                    CurrentUser = new
                    {
                        user.Id,
                        user.FullName,
                        user.AvatarUrl
                    }
                })
                .ToListAsync();

            return Ok(likedVideos);
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

        [HttpPost("ToggleSave")]
        [Authorize]
        public async Task<IActionResult> ToggleSaveVideo([FromBody] ToggleSaveRequest request)
        {
            var maTinDang = request.MaTinDang;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized("Bạn cần đăng nhập để lưu/bỏ lưu video.");

            // ✅ Chỉ check tồn tại tin đăng thay vì load full object
            bool tinDangExists = await _context.TinDangs
                .AnyAsync(t => t.MaTinDang == maTinDang);
            if (!tinDangExists)
                return NotFound("Tin đăng không tồn tại.");

            // ✅ Lấy dữ liệu lưu của user này + count cùng lúc
            var saves = await _context.VideoTinDangSaves
                .Where(v => v.MaTinDang == maTinDang)
                .ToListAsync();

            var videoSave = saves.FirstOrDefault(v => v.MaNguoiDung == userId);

            bool saved;
            if (videoSave == null)
            {
                _context.VideoTinDangSaves.Add(new VideoTinDangSave
                {
                    MaTinDang = maTinDang,
                    MaNguoiDung = userId,
                    NgayLuu = DateTime.Now
                });
                saved = true;
            }
            else
            {
                _context.VideoTinDangSaves.Remove(videoSave);
                saved = false;
            }

            await _context.SaveChangesAsync();

            // ✅ Tính total ngay tại memory, không query DB lần 3
            int totalSaves = saved ? saves.Count + 1 : saves.Count - 1;

            return Ok(new { saved, totalSaves });
        }



        [HttpGet("{maTinDang}/savedinfo")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSavedInfo(int maTinDang)
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);

            var result = await _context.VideoTinDangSaves
                .Where(v => v.MaTinDang == maTinDang)
                .GroupBy(v => 1)
                .Select(g => new
                {
                    soNguoiLuu = g.Count(),
                    isSaved = userId != null && g.Any(v => v.MaNguoiDung == userId)
                })
                .FirstOrDefaultAsync();

            return Ok(result ?? new { soNguoiLuu = 0, isSaved = false });
        }
        [HttpGet("saved")]
        [Authorize]
        public async Task<IActionResult> GetSavedVideos()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var savedVideos = await _context.VideoTinDangSaves
                .Where(v => v.MaNguoiDung == user.Id)
                .OrderByDescending(v => v.NgayLuu)
                .Select(v => new
                {
                    v.MaTinDang,
                    v.TinDang.TieuDe,
                    v.TinDang.VideoUrl,
                    v.TinDang.Gia,
                    v.TinDang.DiaChi,
                    TinhThanh = v.TinDang.TinhThanh != null ? v.TinDang.TinhThanh.TenTinhThanh : null,
                    QuanHuyen = v.TinDang.QuanHuyen != null ? v.TinDang.QuanHuyen.TenQuanHuyen : null,
                    SoNguoiLuu = _context.VideoTinDangSaves.Count(x => x.MaTinDang == v.MaTinDang),
                    SoBinhLuan = _context.VideoComments.Count(x => x.MaTinDang == v.MaTinDang),
                    NguoiDang = new
                    {
                        v.TinDang.NguoiBan.Id,
                        v.TinDang.NguoiBan.FullName,
                        v.TinDang.NguoiBan.AvatarUrl
                    },
                    CurrentUser = new
                    {
                        user.Id,
                        user.FullName,
                        user.AvatarUrl
                    }
                })
                .ToListAsync();

            return Ok(savedVideos);
        }
        // Thêm các API này vào VideoController

        [HttpGet("search-history")]
        [Authorize]
        public async Task<IActionResult> GetSearchHistory()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var history = await _context.SearchHistories
                .Where(sh => sh.UserId == userId)
                .OrderByDescending(sh => sh.CreatedAt)
                .Take(10) // Giới hạn 10 lịch sử gần nhất
                .Select(sh => new { sh.Keyword, sh.CreatedAt })
                .ToListAsync();

            return Ok(history);
        }

        [HttpPost("search-history")]
        [Authorize]
        public async Task<IActionResult> SaveSearchHistory([FromBody] SearchHistoryRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Keyword))
                return BadRequest("Từ khóa không được để trống.");

            // Kiểm tra xem từ khóa đã tồn tại chưa
            var existing = await _context.SearchHistories
                .FirstOrDefaultAsync(sh => sh.UserId == userId && sh.Keyword == request.Keyword);

            if (existing != null)
            {
                // Cập nhật thời gian tìm kiếm
                existing.CreatedAt = DateTime.UtcNow;
                _context.SearchHistories.Update(existing);
            }
            else
            {
                // Tạo mới
                var newHistory = new SearchHistory
                {
                    UserId = userId,
                    Keyword = request.Keyword,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SearchHistories.Add(newHistory);
            }

            // Giới hạn số lượng lịch sử tìm kiếm (chỉ giữ 10 cái gần nhất)
            var historyCount = await _context.SearchHistories
                .CountAsync(sh => sh.UserId == userId);

            if (historyCount >= 10)
            {
                var oldestHistories = await _context.SearchHistories
                    .Where(sh => sh.UserId == userId)
                    .OrderBy(sh => sh.CreatedAt)
                    .Take(historyCount - 9) // Xóa để chỉ còn 9, add thêm 1 thành 10
                    .ToListAsync();

                _context.SearchHistories.RemoveRange(oldestHistories);
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("search-history")]
        [Authorize]
        public async Task<IActionResult> DeleteSearchHistory([FromQuery] string keyword)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("Từ khóa không được để trống.");

            var history = await _context.SearchHistories
                .FirstOrDefaultAsync(sh => sh.UserId == userId && sh.Keyword == keyword);

            if (history != null)
            {
                _context.SearchHistories.Remove(history);
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpDelete("search-history/clear")]
        [Authorize]
        public async Task<IActionResult> ClearAllSearchHistory()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var histories = await _context.SearchHistories
                .Where(sh => sh.UserId == userId)
                .ToListAsync();

            if (histories.Any())
            {
                _context.SearchHistories.RemoveRange(histories);
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        // DTO class
        public class SearchHistoryRequest
        {
            public string Keyword { get; set; } = null!;
        }
        [HttpGet("detail/{maTinDang}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetVideoDetailInfo(int maTinDang)
        {
            var tin = await _context.TinDangs
                .Include(td => td.NguoiBan)
                .Include(td => td.TinhThanh)
                .Include(td => td.QuanHuyen)
                .Include(td => td.AnhTinDangs)
                .Include(td => td.DanhMuc)
                    .ThenInclude(dm => dm.DanhMucCha)
                .FirstOrDefaultAsync(td => td.MaTinDang == maTinDang);

            if (tin == null)
                return NotFound(new { message = "Tin đăng không tồn tại" });

            // Lấy danh sách ảnh (MediaType.Image)
            var danhSachAnh = tin.AnhTinDangs != null
                ? tin.AnhTinDangs
                    .Where(a => a.LoaiMedia == MediaType.Image)
                    .OrderBy(a => a.Order)
                    .Select(a => a.DuongDan)
                    .ToList()
                : new List<string>();

            var result = new
            {
                tin.MaTinDang,
                tin.TieuDe,
                tin.MoTa,
                tin.Gia,
                tin.CoTheThoaThuan,
                tin.TinhTrang,
                tin.DiaChi,
                NgayDang = tin.NgayDang.ToString("dd/MM/yyyy"),
                TinhThanh = tin.TinhThanh?.TenTinhThanh,
                QuanHuyen = tin.QuanHuyen?.TenQuanHuyen,

                DanhSachAnh = danhSachAnh,

                NguoiDang = tin.NguoiBan != null ? new
                {
                    tin.NguoiBan.Id,
                    tin.NguoiBan.FullName,
                    tin.NguoiBan.AvatarUrl,
                    tin.NguoiBan.PhoneNumber
                } : null,

                DanhMuc = tin.DanhMuc != null ? new
                {
                    MaDanhMuc = tin.DanhMuc.MaDanhMuc,
                    TenDanhMuc = tin.DanhMuc.TenDanhMuc,
                    DanhMucCha = tin.DanhMuc.DanhMucCha != null ? new
                    {
                        MaDanhMucCha = tin.DanhMuc.DanhMucCha.MaDanhMucCha,
                        TenDanhMucCha = tin.DanhMuc.DanhMucCha.TenDanhMucCha,
                        tin.DanhMuc.DanhMucCha.Icon
                    } : null
                } : null
            };

            return Ok(result);
        }
        [HttpPost("track-view")]
        [AllowAnonymous]
        public async Task<IActionResult> TrackVideoView([FromBody] TrackViewRequest request)
        {
            try
            {
                // ✅ Lưu giờ Việt Nam luôn
                var now = DateTime.UtcNow.AddHours(7);

                var userId = User.Identity?.IsAuthenticated == true
                    ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                    : null;

                var userAgent = Request.Headers["User-Agent"].FirstOrDefault() ?? "";
                var deviceName = UserAgentHelper.GetDeviceName(userAgent); // ✅ parse gọn
                var ipAddress = Request.GetClientIp();

                Console.WriteLine("=== TRACK VIEW DEBUG ===");
                Console.WriteLine($"MaTinDang: {request.MaTinDang}");
                Console.WriteLine($"UserId: {userId ?? "ANONYMOUS"}");
                Console.WriteLine($"IP: {ipAddress}");
                Console.WriteLine($"Device: {deviceName}");

                VideoView lastView = null;
                bool createNew;

                if (!string.IsNullOrEmpty(userId))
                {
                    lastView = await _context.VideoViews
                        .Where(v => v.MaTinDang == request.MaTinDang && v.UserId == userId)
                        .OrderByDescending(v => v.StartedAt)
                        .FirstOrDefaultAsync();

                    createNew = lastView == null || (now - lastView.StartedAt).TotalMinutes >= 30;
                }
                else
                {
                    lastView = await _context.VideoViews
                        .Where(v => v.MaTinDang == request.MaTinDang
                                 && v.UserId == null
                                 && v.IpAddress == ipAddress
                                 && v.DeviceName == deviceName) // ✅ dùng deviceName gọn
                        .OrderByDescending(v => v.StartedAt)
                        .FirstOrDefaultAsync();

                    createNew = lastView == null || (now - lastView.StartedAt).TotalMinutes >= 30;
                }

                if (createNew)
                {
                    var newView = new VideoView
                    {
                        MaTinDang = request.MaTinDang,
                        UserId = userId,
                        IpAddress = ipAddress,
                        DeviceName = deviceName,
                        StartedAt = now, // ✅ giờ VN
                        WatchedSeconds = request.WatchedSeconds,
                        IsCompleted = request.IsCompleted,
                        RewatchCount = request.RewatchCount
                    };

                    _context.VideoViews.Add(newView);

                    if (request.WatchedSeconds >= 3 && !request.SkipViewCount)
                    {
                        var tinDang = await _context.TinDangs.FindAsync(request.MaTinDang);
                        if (tinDang != null)
                        {
                            tinDang.SoLuotXem += 1;
                            Console.WriteLine($"✅ Tăng view cho video {request.MaTinDang}: {tinDang.SoLuotXem}");
                        }
                    }

                    await _context.SaveChangesAsync();
                    var totalViews = await _context.TinDangs
                        .Where(t => t.MaTinDang == request.MaTinDang)
                        .Select(t => t.SoLuotXem)
                        .FirstOrDefaultAsync();

                    return Ok(new
                    {
                        success = true,
                        message = $"New view tracked ({(userId != null ? "User" : "Anonymous")})",
                        isNewView = true,
                        totalViews,
                        userType = userId != null ? "authenticated" : "anonymous",
                        startedAt = now.ToString("dd/MM/yyyy HH:mm:ss") // ✅ format đẹp khi trả ra API
                    });
                }
                else
                {
                    lastView.WatchedSeconds = Math.Max(lastView.WatchedSeconds, request.WatchedSeconds);
                    if (request.RewatchCount > lastView.RewatchCount)
                        lastView.RewatchCount = request.RewatchCount;
                    if (request.IsCompleted && !lastView.IsCompleted)
                        lastView.IsCompleted = true;

                    await _context.SaveChangesAsync();

                    var totalViews = await _context.TinDangs
                        .Where(t => t.MaTinDang == request.MaTinDang)
                        .Select(t => t.SoLuotXem)
                        .FirstOrDefaultAsync();

                    return Ok(new
                    {
                        success = true,
                        message = $"Existing view updated ({(userId != null ? "User" : "Anonymous")})",
                        isNewView = false,
                        isCompleted = lastView.IsCompleted,
                        rewatchCount = lastView.RewatchCount,
                        totalViews,
                        userType = userId != null ? "authenticated" : "anonymous",
                        startedAt = now.ToString("dd/MM/yyyy HH:mm:ss") // ✅ format đẹp khi trả ra API
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Track view error: {ex.Message}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        public class TrackViewRequest
        {
            public int MaTinDang { get; set; }
            public int WatchedSeconds { get; set; }
            public bool IsCompleted { get; set; }
            public int RewatchCount { get; set; } = 0;
            public bool SkipViewCount { get; set; } = false;
        }
    }
}