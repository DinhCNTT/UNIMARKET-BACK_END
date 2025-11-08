using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace UniMarket.Hubs // <-- Đảm bảo namespace này khớp với dự án của bạn
{
    public class VideoHub : Hub
    {
        // User tham gia "phòng" xem video
        public async Task JoinVideoGroup(string maTinDang)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, maTinDang);
        }

        // User rời "phòng"
        public async Task LeaveVideoGroup(string maTinDang)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, maTinDang);
        }
    }
}