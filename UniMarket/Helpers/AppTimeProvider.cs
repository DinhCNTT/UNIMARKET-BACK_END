using System;

namespace UniMarket.Helpers // Đảm bảo namespace này đúng
{
    // Tên class đã được đổi thành AppTimeProvider
    public static class AppTimeProvider
    {
        // Luôn trả về UTC
        public static DateTime GetUtcNow()
        {
            return DateTime.UtcNow;
        }
    }

}