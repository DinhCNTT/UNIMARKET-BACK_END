using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory; // <-- Đã có 'using'
using UniMarket.DataAccess;
using UniMarket.Models;
using System.IO; // <-- Thêm using này để Path.GetFileName hoạt động
using System.Threading.Tasks; // <-- Thêm using này
using System.Collections.Generic; // <-- Thêm using này
using System.Linq; // <-- Thêm using này

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        // 1. KHAI BÁO BIẾN CACHE
        private readonly IMemoryCache _memoryCache;

        // 2. SỬA CONSTRUCTOR ĐỂ NHẬN CACHE
        public CategoryController(ApplicationDbContext context, IMemoryCache memoryCache)
        {
            _context = context;
            _memoryCache = memoryCache; // <-- Gán giá trị
        }

        // GET: api/category
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetCategories()
        {
            var categories = await _context.DanhMucChas
                .Select(dm => new
                {
                    TenDanhMucCha = dm.TenDanhMucCha,
                    AnhDanhMucCha = !string.IsNullOrEmpty(dm.AnhDanhMucCha)
                        ? $"/images/categories/{Path.GetFileName(dm.AnhDanhMucCha)}"
                        : null
                })
                .ToListAsync();

            return Ok(categories);
        }

        [HttpGet("get-categories-with-icon")]
        public async Task<ActionResult<IEnumerable<object>>> GetCategoriesWithIcon()
        {
            // Đặt một tên key duy nhất cho cache
            const string cacheKey = "categories-with-icon";

            // 1. Thử lấy từ cache trước
            // Lỗi của bạn ở đây, _memoryCache chưa tồn tại
            if (!_memoryCache.TryGetValue(cacheKey, out var categories))
            {
                // 2. Nếu cache không có (cache miss)
                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                categories = await _context.DanhMucChas
                    .AsNoTracking()
                    .Select(dm => new
                    {
                        Id = dm.MaDanhMucCha,
                        TenDanhMucCha = dm.TenDanhMucCha,
                        Icon = !string.IsNullOrEmpty(dm.Icon)
                            ? $"{baseUrl}/images/categories/{Path.GetFileName(dm.Icon)}"
                            : null,
                        DanhMucCon = dm.DanhMucs.Select(dmc => new
                        {
                            Id = dmc.MaDanhMuc,
                            TenDanhMucCon = dmc.TenDanhMuc
                        })
                    })
                    .ToListAsync();

                // 3. Lưu kết quả vào cache
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromHours(1)); // Đặt cache sống trong 1 giờ

                // Lỗi của bạn cũng ở đây
                _memoryCache.Set(cacheKey, categories, cacheOptions);
            }

            // 4. Trả về kết quả (từ cache hoặc từ DB)
            return Ok(categories);
        }
    }
}