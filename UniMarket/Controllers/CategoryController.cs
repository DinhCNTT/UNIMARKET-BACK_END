using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CategoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/category
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetCategories()
        {
            var categories = await _context.DanhMucChas
                .Select(dm => new
                {
                    TenDanhMucCha = dm.TenDanhMucCha, // Lấy tên danh mục cha
                    AnhDanhMucCha = !string.IsNullOrEmpty(dm.AnhDanhMucCha)
                        ? $"/images/categories/{Path.GetFileName(dm.AnhDanhMucCha)}" // Trả về đường dẫn tương đối
                        : null
                })
                .ToListAsync();

            return Ok(categories);
        }

        [HttpGet("get-categories-with-icon")]
        public async Task<ActionResult<IEnumerable<object>>> GetCategoriesWithIcon()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var categories = await _context.DanhMucChas
                .Include(dm => dm.DanhMucs) // Bao gồm danh mục con
                .Select(dm => new
                {
                    Id = dm.MaDanhMucCha, // Sử dụng MaDanhMucCha làm Id của danh mục cha
                    TenDanhMucCha = dm.TenDanhMucCha,
                    Icon = !string.IsNullOrEmpty(dm.Icon)
                        ? $"{baseUrl}/images/categories/{Path.GetFileName(dm.Icon)}"
                        : null,
                    DanhMucCon = dm.DanhMucs.Select(dmc => new
                    {
                        Id = dmc.MaDanhMuc, // Thêm Id của danh mục con
                        TenDanhMucCon = dmc.TenDanhMuc
                    }).ToList()
                })
                .ToListAsync();

            return Ok(categories);
        }




    }
}
