using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;

namespace UniMarket.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(ApplicationDbContext context, ILogger<ChatHub> logger)
        {
            _context = context;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier ?? Context.ConnectionId;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
            await base.OnConnectedAsync();
        }

        public async Task ThamGiaCuocTroChuyen(string maCuocTroChuyen)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, maCuocTroChuyen);
        }

        public async Task RoiKhoiCuocTroChuyen(string maCuocTroChuyen)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, maCuocTroChuyen);
        }

        public async Task GuiTinNhan(string maCuocTroChuyen, string maNguoiGui, string noiDung)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(noiDung))
                    throw new HubException("Nội dung tin nhắn không được để trống.");

                var isParticipant = await _context.NguoiThamGias
                    .AnyAsync(n => n.MaCuocTroChuyen == maCuocTroChuyen && n.MaNguoiDung == maNguoiGui);

                if (!isParticipant)
                {
                    _logger.LogWarning($"Người dùng {maNguoiGui} cố gửi tin nhắn vào cuộc trò chuyện {maCuocTroChuyen} mà không thuộc nhóm.");
                    throw new HubException("Bạn không có quyền gửi tin nhắn trong cuộc trò chuyện này.");
                }

                var tinNhanMoi = new TinNhan
                {
                    MaCuocTroChuyen = maCuocTroChuyen,
                    MaNguoiGui = maNguoiGui,
                    NoiDung = noiDung,
                    ThoiGianGui = DateTime.UtcNow
                };

                _context.TinNhans.Add(tinNhanMoi);
                await _context.SaveChangesAsync();

                var cuocTroChuyen = await _context.CuocTroChuyens
                    .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);

                var wasEmpty = cuocTroChuyen.IsEmpty;

                if (cuocTroChuyen != null && wasEmpty)
                {
                    cuocTroChuyen.IsEmpty = false;
                    await _context.SaveChangesAsync();

                    // Gửi đến user-{người còn lại}
                    var otherUserId = _context.NguoiThamGias
                        .Where(n => n.MaCuocTroChuyen == maCuocTroChuyen && n.MaNguoiDung != maNguoiGui)
                        .Select(n => n.MaNguoiDung)
                        .FirstOrDefault();

                    var otherUserName = _context.Users
                        .Where(u => u.Id == maNguoiGui)
                        .Select(u => u.FullName)
                        .FirstOrDefault();

                    await Clients.Group($"user-{otherUserId}").SendAsync("CapNhatCuocTroChuyen", new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        IsEmpty = false,
                        TieuDeTinDang = cuocTroChuyen.TieuDeTinDang,
                        AnhDaiDienTinDang = cuocTroChuyen.AnhDaiDienTinDang,
                        GiaTinDang = cuocTroChuyen.GiaTinDang,
                        MaNguoiConLai = maNguoiGui,
                        TenNguoiConLai = otherUserName,
                        TinNhanCuoi = tinNhanMoi.NoiDung
                    });
                }

                var dto = new TinNhanDTO
                {
                    MaTinNhan = tinNhanMoi.MaTinNhan,
                    MaCuocTroChuyen = tinNhanMoi.MaCuocTroChuyen,
                    MaNguoiGui = tinNhanMoi.MaNguoiGui,
                    NoiDung = tinNhanMoi.NoiDung,
                    ThoiGianGui = tinNhanMoi.ThoiGianGui
                };

                await Clients.Group(maCuocTroChuyen).SendAsync("NhanTinNhan", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi gửi tin nhắn trong cuộc trò chuyện {maCuocTroChuyen} bởi người dùng {maNguoiGui}.");
                throw;
            }
        }

        public async Task DanhDauDaXem(string maCuocTroChuyen, string maNguoiXem)
        {
            try
            {
                var chuaXem = await _context.TinNhans
                    .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen
                             && t.MaNguoiGui != maNguoiXem
                             && !t.DaXem)
                    .OrderBy(t => t.ThoiGianGui)
                    .ToListAsync();

                if (chuaXem.Any())
                {
                    foreach (var msg in chuaXem)
                    {
                        msg.DaXem = true;
                        msg.ThoiGianXem = DateTime.UtcNow;
                    }

                    await _context.SaveChangesAsync();

                    var tinNhanCuoi = chuaXem.Last();

                    await Clients.Group(maCuocTroChuyen).SendAsync("DaXemTinNhan", new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        MaTinNhanCuoi = tinNhanCuoi.MaTinNhan,
                        NguoiXem = maNguoiXem
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi đánh dấu đã xem cho cuộc trò chuyện {maCuocTroChuyen}");
            }
        }
    }
}
