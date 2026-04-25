namespace Repository.Services
{
    public class RateLimitProtectionService
    {
        private readonly Logger _logger;
        private readonly ConfigManager _configManager;
        private readonly object _lockObject = new object();
        private DateTime _currentSecond = DateTime.UtcNow;
        private int _requestsInCurrentSecond = 0;
        private bool _isPaused = false;
        private DateTime _pauseEndTime = DateTime.MinValue;
        private Timer? _pauseTimer;

        public bool IsPaused => _isPaused;
        public DateTime PauseEndTime => _pauseEndTime;
        public int RemainingPauseSeconds => _isPaused ? Math.Max(0, (int)(_pauseEndTime - DateTime.UtcNow).TotalSeconds) : 0;

        public event EventHandler<RateLimitPauseEventArgs>? PauseStatusChanged;

        public RateLimitProtectionService(Logger logger, ConfigManager configManager)
        {
            _logger = logger;
            _configManager = configManager;
        }

        public bool CheckRequest()
        {
            var config = _configManager.GetConfig();
            
            if (!config.RateLimitProtection)
            {
                return true;
            }

            lock (_lockObject)
            {
                if (_isPaused)
                {
                    return false;
                }

                var now = DateTime.UtcNow;
                
                if (now.Second != _currentSecond.Second || now.Minute != _currentSecond.Minute || now.Hour != _currentSecond.Hour)
                {
                    _currentSecond = now;
                    _requestsInCurrentSecond = 0;
                }

                _requestsInCurrentSecond++;

                if (_requestsInCurrentSecond > config.RateLimitRequestsPerSecond)
                {
                    TriggerPause(config.RateLimitPauseMinutes);
                    return false;
                }

                return true;
            }
        }

        private void TriggerPause(int pauseMinutes)
        {
            lock (_lockObject)
            {
                _isPaused = true;
                _pauseEndTime = DateTime.UtcNow.AddMinutes(pauseMinutes);
                
                _logger.LogWarning(I18nService.Instance.T("rate_limit.triggered", pauseMinutes));
                
                PauseStatusChanged?.Invoke(this, new RateLimitPauseEventArgs(true, _pauseEndTime));
                
                _pauseTimer?.Dispose();
                _pauseTimer = new Timer(EndPause, null, TimeSpan.FromMinutes(pauseMinutes), Timeout.InfiniteTimeSpan);
            }
        }

        private void EndPause(object? state)
        {
            lock (_lockObject)
            {
                _isPaused = false;
                _pauseEndTime = DateTime.MinValue;
                _requestsInCurrentSecond = 0;
                _currentSecond = DateTime.UtcNow;
                
                _logger.LogInfo(I18nService.Instance.T("rate_limit.resumed"));
                
                PauseStatusChanged?.Invoke(this, new RateLimitPauseEventArgs(false, DateTime.MinValue));
            }
        }

        public void ForceResume()
        {
            _pauseTimer?.Dispose();
            EndPause(null);
        }

        public (int RequestsInCurrentSecond, int Limit) GetCurrentStats()
        {
            var config = _configManager.GetConfig();
            return (_requestsInCurrentSecond, config.RateLimitRequestsPerSecond);
        }
    }

    public class RateLimitPauseEventArgs : EventArgs
    {
        public bool IsPaused { get; }
        public DateTime PauseEndTime { get; }

        public RateLimitPauseEventArgs(bool isPaused, DateTime pauseEndTime)
        {
            IsPaused = isPaused;
            PauseEndTime = pauseEndTime;
        }
    }
}
