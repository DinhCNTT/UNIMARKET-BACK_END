using System;
using System.Collections.Generic;

namespace UniMarket.DTO // Hoặc UniMarket.Services tùy project của bạn
{
    public class UserPresenceService
    {
        // Dictionary lưu trạng thái: Key = UserId, Value = (Online?, LastActiveTime)
        private readonly Dictionary<string, (bool IsOnline, DateTime LastActive)> _userStatus = new();
        private readonly object _lock = new();

        public void SetOnline(string userId)
        {
            lock (_lock)
            {
                _userStatus[userId] = (true, DateTime.UtcNow);
            }
        }

        public void SetOffline(string userId)
        {
            lock (_lock)
            {
                _userStatus[userId] = (false, DateTime.UtcNow);
            }
        }

        // ✅ ĐÂY LÀ HÀM BẠN ĐANG THIẾU
        // Hàm này xóa user khỏi bộ nhớ RAM khi họ ngắt kết nối
        public void RemoveUser(string userId)
        {
            lock (_lock)
            {
                if (_userStatus.ContainsKey(userId))
                {
                    _userStatus.Remove(userId);
                }
            }
        }

        public (bool IsOnline, DateTime LastActive)? GetStatus(string userId)
        {
            lock (_lock)
            {
                return _userStatus.TryGetValue(userId, out var status) ? status : null;
            }
        }

        public Dictionary<string, (bool, DateTime)> GetAllStatuses()
        {
            lock (_lock)
            {
                return new(_userStatus);
            }
        }
    }
}