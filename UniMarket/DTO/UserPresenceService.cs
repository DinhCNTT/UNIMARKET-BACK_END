namespace UniMarket.DTO
{
    public class UserPresenceService
    {
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
