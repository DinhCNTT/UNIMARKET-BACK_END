using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace UniMarket.Hubs
{
    [Authorize] // Bắt buộc đăng nhập mới kết nối được
    public class UserNotificationHub : Hub
    {
        // 1. Khi người dùng mở App/Web -> Kết nối vào Hub này
        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;

            // Console.WriteLine($"User {userId} đã kết nối UserNotificationHub");

            // SignalR tự động gom ConnectionId theo UserId.
            // Chúng ta không cần code gì thêm, chỉ cần giữ kết nối là được.
            await base.OnConnectedAsync();
        }

        // 2. Khi người dùng tắt App -> Ngắt kết nối
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Console.WriteLine($"User {Context.UserIdentifier} đã ngắt kết nối.");
            await base.OnDisconnectedAsync(exception);
        }
    }
}
