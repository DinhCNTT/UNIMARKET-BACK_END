using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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

                var isParticipant = await _context.NguoiThamGias.AnyAsync(n => n.MaCuocTroChuyen == maCuocTroChuyen && n.MaNguoiDung == maNguoiGui);

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

                // ✅ Cập nhật IsEmpty = false nếu là tin đầu tiên
                var cuocTroChuyen = await _context.CuocTroChuyens.FirstOrDefaultAsync(c => c.MaCuocTroChuyen == maCuocTroChuyen);
                if (cuocTroChuyen != null && cuocTroChuyen.IsEmpty)
                {
                    cuocTroChuyen.IsEmpty = false;
                    await _context.SaveChangesAsync();
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

        public async Task<List<TinNhanDTO>> LayLichSuChat(string maCuocTroChuyen)
        {
            try
            {
                var lichSu = await _context.TinNhans
                    .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen)
                    .OrderBy(t => t.ThoiGianGui)
                    .Select(t => new TinNhanDTO
                    {
                        MaTinNhan = t.MaTinNhan,
                        MaCuocTroChuyen = t.MaCuocTroChuyen,
                        MaNguoiGui = t.MaNguoiGui,
                        NoiDung = t.NoiDung,
                        ThoiGianGui = t.ThoiGianGui
                    })
                    .ToListAsync();

                return lichSu;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi lấy lịch sử chat của cuộc trò chuyện {maCuocTroChuyen}");
                throw new HubException("Lỗi khi lấy lịch sử chat.");
            }
        }
        public async Task DanhDauDaXem(string maCuocTroChuyen, string maNguoiXem)
        {
            try
            {
                _logger.LogInformation($"Bắt đầu đánh dấu đã xem: MaCuocTroChuyen={maCuocTroChuyen}, MaNguoiXem={maNguoiXem}");

                var chuaXem = await _context.TinNhans
                    .Where(t => t.MaCuocTroChuyen == maCuocTroChuyen
                             && t.MaNguoiGui != maNguoiXem
                             && !t.DaXem)
                    .OrderBy(t => t.ThoiGianGui)
                    .ToListAsync();

                _logger.LogInformation($"Số tin nhắn chưa xem cần đánh dấu: {chuaXem.Count}");

                if (chuaXem.Any())
                {
                    foreach (var msg in chuaXem)
                    {
                        msg.DaXem = true;
                        msg.ThoiGianXem = DateTime.UtcNow;
                        _logger.LogDebug($"Đánh dấu tin nhắn {msg.MaTinNhan} là đã xem");
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Lưu thay đổi đánh dấu đã xem thành công");

                    var tinNhanCuoi = chuaXem.Last();

                    _logger.LogInformation($"Gửi event DaXemTinNhan với MaTinNhanCuoi={tinNhanCuoi.MaTinNhan}");

                    await Clients.Group(maCuocTroChuyen).SendAsync("DaXemTinNhan", new
                    {
                        MaCuocTroChuyen = maCuocTroChuyen,
                        MaTinNhanCuoi = tinNhanCuoi.MaTinNhan,
                        NguoiXem = maNguoiXem
                    });
                }
                else
                {
                    _logger.LogInformation("Không có tin nhắn nào chưa xem để đánh dấu.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi đánh dấu đã xem cho cuộc trò chuyện {maCuocTroChuyen}");
                // Tùy anh em muốn throw hay swallow, thường nên swallow để Hub không crash
            }
        }


    }
}
