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
            if (string.IsNullOrEmpty(request.MaNguoiDung1) || string.IsNullOrEmpty(request.MaNguoiDung2) || request.MaTinDang <= 0)
                return BadRequest("Thông tin không đầy đủ.");

            string GenerateChatId(string u1, string u2, int maTinDang)
            {
                var arr = new[] { u1, u2 };
                Array.Sort(arr);
                return $"{arr[0]}-{arr[1]}-{maTinDang}";
            }

            var maCuocTroChuyen = GenerateChatId(request.MaNguoiDung1, request.MaNguoiDung2, request.MaTinDang);

            var existingChat = await _context.CuocTroChuyens
                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

            if (existingChat != null)
            {
                return Ok(new { MaCuocTroChuyen = existingChat.MaCuocTroChuyen });
            }

            var tinDang = await _context.TinDangs.Include(t => t.AnhTinDangs).FirstOrDefaultAsync(t => t.MaTinDang == request.MaTinDang);
            if (tinDang == null) return NotFound("Tin đăng không tồn tại.");

            var newChat = new CuocTroChuyen
            {
                MaCuocTroChuyen = maCuocTroChuyen,
                ThoiGianTao = DateTime.UtcNow,
                IsEmpty = true,
                MaTinDang = tinDang.MaTinDang,
                TieuDeTinDang = tinDang.TieuDe,
                AnhDaiDienTinDang = tinDang.AnhTinDangs?.FirstOrDefault()?.DuongDan ?? "",
                GiaTinDang = tinDang.Gia
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
                    c.MaCuocTroChuyen,
                    c.ThoiGianTao,
                    c.IsEmpty,
                    c.MaTinDang,
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
                        .FirstOrDefault(),
                    TieuDeTinDang = c.TieuDeTinDang,
                    AnhDaiDienTinDang = c.AnhDaiDienTinDang,
                    GiaTinDang = c.GiaTinDang,
                    IsSeller = _context.TinDangs.Any(t => t.MaTinDang == c.MaTinDang && t.MaNguoiBan == userId),
                    HasUnreadMessages = _context.TinNhans
                        .Any(t => t.MaCuocTroChuyen == c.MaCuocTroChuyen && t.MaNguoiGui != userId && !t.DaXem)
                })
                .Where(c => !c.IsSeller || (c.IsSeller && !c.IsEmpty))
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
                    ThoiGianGui = t.ThoiGianGui.ToString("O"),
                    t.DaXem,
                    t.ThoiGianXem
                })
                .ToListAsync();

            return Ok(messages);
        }

        [HttpGet("info/{maCuocTroChuyen}")]
        public async Task<IActionResult> GetChatInfo(string maCuocTroChuyen)
        {
            var chat = await _context.CuocTroChuyens
                .Where(c => c.MaCuocTroChuyen == maCuocTroChuyen)
                .Select(c => new
                {
                    c.MaTinDang,
                    c.TieuDeTinDang,
                    c.GiaTinDang,
                    c.AnhDaiDienTinDang
                }).FirstOrDefaultAsync();

            if (chat == null) return NotFound();

            return Ok(chat);
        }
        [HttpGet("unread-count/{userId}")]
        public async Task<IActionResult> GetUnreadCount(string userId)
        {
            // Count messages not sent by the user, in conversations the user participates in, and not yet read
            var count = await _context.TinNhans
                .Where(t => t.MaNguoiGui != userId
                    && !t.DaXem
                    && _context.NguoiThamGias.Any(n => n.MaCuocTroChuyen == t.MaCuocTroChuyen && n.MaNguoiDung == userId))
                .CountAsync();

            return Ok(new { unreadCount = count });
        }

    }
}
