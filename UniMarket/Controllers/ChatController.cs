using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ChatController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST api/chat/start
        [HttpPost("start")]
        public async Task<IActionResult> StartChat([FromBody] StartChatRequest request)
        {
            if (string.IsNullOrEmpty(request.MaNguoiDung1) || string.IsNullOrEmpty(request.MaNguoiDung2))
                return BadRequest("Mã người dùng không được để trống.");

            string GenerateChatId(string u1, string u2)
            {
                var arr = new[] { u1, u2 };
                Array.Sort(arr);
                return string.Join("-", arr);
            }

            var maCuocTroChuyen = GenerateChatId(request.MaNguoiDung1, request.MaNguoiDung2);

            // Kiểm tra cuộc trò chuyện đã tồn tại chưa
            var existingChat = await _context.CuocTroChuyens
                .Include(c => c.NguoiThamGias)
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

            if (existingChat != null)
            {
                // Nếu đã tồn tại, trả về luôn mà không tạo mới
                return Ok(new { MaCuocTroChuyen = existingChat.MaCuocTroChuyen });
            }

            // Nếu chưa tồn tại, tạo mới
            var newChat = new CuocTroChuyen
            {
                MaCuocTroChuyen = maCuocTroChuyen,
                ThoiGianTao = DateTime.UtcNow,
                IsEmpty = true// ✅ đánh dấu là mới mở, chưa có tin nhắn
            };

            _context.CuocTroChuyens.Add(newChat);

            _context.NguoiThamGias.AddRange(new[]
            {
        new NguoiThamGia { MaCuocTroChuyen = maCuocTroChuyen, MaNguoiDung = request.MaNguoiDung1 },
        new NguoiThamGia { MaCuocTroChuyen = maCuocTroChuyen, MaNguoiDung = request.MaNguoiDung2 }
    });

            await _context.SaveChangesAsync();

            return Ok(new { MaCuocTroChuyen = maCuocTroChuyen });
        }
        // GET api/chat/user/{userId}
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserConversations(string userId)
        {
            var userChats = await _context.CuocTroChuyens
                .Where(c => c.NguoiThamGias.Any(n => n.MaNguoiDung == userId))
                .Select(c => new
                {
                    MaCuocTroChuyen = c.MaCuocTroChuyen,
                    ThoiGianTao = c.ThoiGianTao,
                    IsEmpty = c.IsEmpty,
                    TinNhanCuoi = _context.TinNhans
                        .Where(t => t.MaCuocTroChuyen == c.MaCuocTroChuyen)
                        .OrderByDescending(t => t.ThoiGianGui)
                        .Select(t => t.NoiDung)
                        .FirstOrDefault(),
                    MaNguoiConLai = c.NguoiThamGias
                        .Where(n => n.MaNguoiDung != userId)
                        .Select(n => n.MaNguoiDung)
                        .FirstOrDefault(),
                    TenNguoiConLai = c.NguoiThamGias
                        .Where(n => n.MaNguoiDung != userId)
                        .Select(n => n.NguoiDung.FullName)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(userChats);
        }
        // GET api/chat/history/{maCuocTroChuyen}
        [HttpGet("history/{maCuocTroChuyen}")]
        public async Task<IActionResult> GetChatHistory(string maCuocTroChuyen)
        {
            var messages = await _context.TinNhans
                .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen)
                .OrderBy(t => t.ThoiGianGui)
                .Select(t => new
                {
                    t.MaTinNhan,
                    t.MaCuocTroChuyen,
                    t.MaNguoiGui,
                    t.NoiDung,
                    t.ThoiGianGui
                })
                .ToListAsync();

            return Ok(messages);
        }


    }
}
