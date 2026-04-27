using Repository.Models;
using System.Collections.Concurrent;

namespace Repository.Services
{
    public class RequestThrottlingService
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, RequestCounter> _requestCounters;
        private readonly ConcurrentDictionary<string, DateTime> _tempBlockTimes;
        private Timer _cleanupTimer;

        public RequestThrottlingService(ConfigManager configManager, Logger logger)
        {
            _configManager = configManager;
            _logger = logger;
            _requestCounters = new ConcurrentDictionary<string, RequestCounter>();
            _tempBlockTimes = new ConcurrentDictionary<string, DateTime>();
            
            _cleanupTimer = new Timer(CleanupExpiredData, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            
            InitializeTempBlockTimes();
        }

        private void InitializeTempBlockTimes()
        {
            var config = _configManager.GetConfig();
            if (!string.IsNullOrWhiteSpace(config.TemporarilyBlockedIPs))
            {
                var ips = config.TemporarilyBlockedIPs.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ip => ip.Trim())
                    .Where(ip => !string.IsNullOrWhiteSpace(ip));
                
                var now = DateTime.Now;
                foreach (var ip in ips)
                {
                    _tempBlockTimes.TryAdd(ip, now);
                }
            }
        }

        public bool IsIPblocked(string clientIP)
        {
            var config = _configManager.GetConfig();
            
            if (!config.RequestThrottling)
            {
                return false;
            }

            if (IsInPermanentBlockList(clientIP, config.BlockedIPs))
            {
                return true;
            }

            if (IsInTemporaryBlockList(clientIP, config))
            {
                return true;
            }

            return false;
        }

        public bool CheckAndUpdateRequestRate(string clientIP)
        {
            var config = _configManager.GetConfig();
            
            if (!config.RequestThrottling)
            {
                return true;
            }

            var now = DateTime.Now;
            var counter = _requestCounters.GetOrAdd(clientIP, ip => new RequestCounter());
            
            lock (counter)
            {
                counter.Requests.RemoveAll(r => now - r > TimeSpan.FromSeconds(1));
                
                counter.Requests.Add(now);
                
                int requestsPerSecond = counter.Requests.Count;

                if (requestsPerSecond > config.MaximumRequestsPerSecond)
                {
                    AddToPermanentBlockList(clientIP, config);
                    RemoveFromTemporaryBlockList(clientIP, config);
                    _logger.LogWarning(I18nService.Instance.T("throttling.permanent_block", clientIP));
                    return false;
                }

                if (requestsPerSecond >= config.AlarmRequestsPerSecond)
                {
                    AddToTemporaryBlockList(clientIP, config);
                    _logger.LogWarning(I18nService.Instance.T("throttling.temp_block", clientIP));
                    return false;
                }
            }

            return true;
        }

        private bool IsInPermanentBlockList(string clientIP, string blockedIPs)
        {
            if (!string.IsNullOrWhiteSpace(blockedIPs))
            {
                var blockedIPList = blockedIPs.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ip => ip.Trim())
                    .Where(ip => !string.IsNullOrWhiteSpace(ip));

                foreach (var blockedIP in blockedIPList)
                {
                    if (IsIPMatch(clientIP, blockedIP))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsInTemporaryBlockList(string clientIP, Config config)
        {
            if (_tempBlockTimes.IsEmpty)
            {
                return false;
            }

            if (_tempBlockTimes.TryGetValue(clientIP, out var blockTime))
            {
                var blockDuration = TimeSpan.FromMinutes(config.BlockDurationMinutes);
                if (DateTime.Now - blockTime < blockDuration)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddToPermanentBlockList(string clientIP, Config config)
        {
            if (IsInPermanentBlockList(clientIP, config.BlockedIPs))
            {
                return;
            }

            var blockedIPs = string.IsNullOrWhiteSpace(config.BlockedIPs)
                ? new List<string>()
                : config.BlockedIPs.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ip => ip.Trim())
                    .Where(ip => !string.IsNullOrWhiteSpace(ip))
                    .ToList();

            blockedIPs.Add(clientIP);
            config.BlockedIPs = string.Join(",", blockedIPs);
            _configManager.SaveConfig(config);
            _logger.LogInfo(I18nService.Instance.T("throttling.added_permanent", clientIP));
        }

        private void AddToTemporaryBlockList(string clientIP, Config config)
        {
            if (_tempBlockTimes.ContainsKey(clientIP) && IsInTemporaryBlockList(clientIP, config))
            {
                return;
            }

            var ips = string.IsNullOrWhiteSpace(config.TemporarilyBlockedIPs)
                ? new List<string>()
                : config.TemporarilyBlockedIPs.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ip => ip.Trim())
                    .Where(ip => !string.IsNullOrWhiteSpace(ip))
                    .ToList();

            if (!ips.Any(ip => IsIPMatch(clientIP, ip)))
            {
                ips.Add(clientIP);
                config.TemporarilyBlockedIPs = string.Join(",", ips);
                _configManager.SaveConfig(config);
            }

            _tempBlockTimes[clientIP] = DateTime.Now;
            _logger.LogInfo(I18nService.Instance.T("throttling.added_temporary", clientIP));
        }

        private void RemoveFromTemporaryBlockList(string clientIP, Config config)
        {
            _tempBlockTimes.TryRemove(clientIP, out _);

            if (string.IsNullOrWhiteSpace(config.TemporarilyBlockedIPs))
            {
                return;
            }

            var ips = config.TemporarilyBlockedIPs.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ip => ip.Trim())
                .Where(ip => !string.IsNullOrWhiteSpace(ip) && !IsIPMatch(clientIP, ip))
                .ToList();

            var newTempBlockedIPs = string.Join(",", ips);
            if (newTempBlockedIPs != config.TemporarilyBlockedIPs)
            {
                config.TemporarilyBlockedIPs = newTempBlockedIPs;
                _configManager.SaveConfig(config);
                _logger.LogInfo(I18nService.Instance.T("throttling.removed_from_temporary", clientIP));
            }
        }

        private bool IsIPMatch(string clientIP, string blockedIP)
        {
            if (blockedIP.Contains('*'))
            {
                var pattern = blockedIP.Replace(".", "\\.").Replace("*", ".*");
                return System.Text.RegularExpressions.Regex.IsMatch(clientIP, $"^{pattern}$");
            }

            return clientIP == blockedIP;
        }

        private void CleanupExpiredData(object? state)
        {
            var config = _configManager.GetConfig();
            var now = DateTime.Now;
            var blockDuration = TimeSpan.FromMinutes(config.BlockDurationMinutes);

            var expiredIPs = _tempBlockTimes.Where(kvp => now - kvp.Value >= blockDuration)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var ip in expiredIPs)
            {
                _tempBlockTimes.TryRemove(ip, out _);
            }

            if (expiredIPs.Count > 0 && !string.IsNullOrWhiteSpace(config.TemporarilyBlockedIPs))
            {
                var remainingIPs = config.TemporarilyBlockedIPs.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ip => ip.Trim())
                    .Where(ip => !string.IsNullOrWhiteSpace(ip) && !expiredIPs.Any(e => IsIPMatch(e, ip)))
                    .ToList();

                var newTempBlockedIPs = string.Join(",", remainingIPs);
                if (newTempBlockedIPs != config.TemporarilyBlockedIPs)
                {
                    config.TemporarilyBlockedIPs = newTempBlockedIPs;
                    _configManager.SaveConfig(config);
                }
            }

            foreach (var kvp in _requestCounters)
            {
                lock (kvp.Value)
                {
                    kvp.Value.Requests.RemoveAll(r => now - r > TimeSpan.FromSeconds(1));
                }
            }
        }

        public Dictionary<string, object> GetThrottlingStatus()
        {
            var config = _configManager.GetConfig();
            var status = new Dictionary<string, object>
            {
                ["RequestThrottlingEnabled"] = config.RequestThrottling,
                ["MaximumRequestsPerSecond"] = config.MaximumRequestsPerSecond,
                ["AlarmRequestsPerSecond"] = config.AlarmRequestsPerSecond,
                ["BlockDurationMinutes"] = config.BlockDurationMinutes,
                ["ActiveRequestCounters"] = _requestCounters.Count,
                ["TemporarilyBlockedIPs"] = _tempBlockTimes.Count
            };

            return status;
        }

        private class RequestCounter
        {
            public List<DateTime> Requests { get; } = new List<DateTime>();
        }
    }
}
