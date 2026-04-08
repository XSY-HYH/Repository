using Repository.Models;
using System.Collections.Concurrent;

namespace Repository.Services
{
    public class DDoSProtectionService
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, RequestCounter> _requestCounters;
        private readonly ConcurrentDictionary<string, DateTime> _blockedIPs;
        private Timer _cleanupTimer;

        public DDoSProtectionService(ConfigManager configManager, Logger logger)
        {
            _configManager = configManager;
            _logger = logger;
            _requestCounters = new ConcurrentDictionary<string, RequestCounter>();
            _blockedIPs = new ConcurrentDictionary<string, DateTime>();
            
            _cleanupTimer = new Timer(CleanupExpiredData, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        public bool IsIPBlocked(string clientIP)
        {
            var config = _configManager.GetConfig();
            
            if (!config.DDoSProtection)
            {
                return false;
            }

            if (IsInPermanentBlockList(clientIP, config.BlockedIPs))
            {
                _logger.LogWarning(I18nService.Instance.T("ddos.permanent_block", clientIP));
                return true;
            }

            if (_blockedIPs.TryGetValue(clientIP, out var blockTime))
            {
                var blockDuration = TimeSpan.FromMinutes(config.BlockDurationMinutes);
                if (DateTime.Now - blockTime < blockDuration)
                {
                    _logger.LogWarning(I18nService.Instance.T("ddos.temp_block", clientIP, blockTime.Add(blockDuration)));
                    return true;
                }
                else
                {
                    _blockedIPs.TryRemove(clientIP, out _);
                    _requestCounters.TryRemove(clientIP, out _);
                    _logger.LogInfo(I18nService.Instance.T("ddos.unblocked", clientIP));
                }
            }

            return false;
        }

        public bool CheckAndUpdateRequestRate(string clientIP)
        {
            var config = _configManager.GetConfig();
            
            if (!config.DDoSProtection)
            {
                return true;
            }

            var now = DateTime.Now;
            var counter = _requestCounters.GetOrAdd(clientIP, ip => new RequestCounter());
            
            lock (counter)
            {
                counter.Requests.RemoveAll(r => now - r > TimeSpan.FromMinutes(1));
                
                counter.Requests.Add(now);
                
                if (counter.Requests.Count > config.MaxRequestsPerMinute)
                {
                    _blockedIPs[clientIP] = now;
                    _logger.LogWarning(I18nService.Instance.T("ddos.blocked_excessive", clientIP, counter.Requests.Count, config.MaxRequestsPerMinute));
                    
                    UpdatePermanentBlockList(clientIP, config);
                    
                    return false;
                }
            }

            return true;
        }

        private bool IsInPermanentBlockList(string clientIP, string blockedIPs)
        {
            if (!string.IsNullOrWhiteSpace(blockedIPs))
            {
                var blockedIPList = blockedIPs?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ip => ip.Trim())
                    .Where(ip => !string.IsNullOrWhiteSpace(ip)) ?? Enumerable.Empty<string>();

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

        private void UpdatePermanentBlockList(string clientIP, Config config)
        {
            if (!string.IsNullOrWhiteSpace(config.BlockedIPs))
            {
                var blockedIPs = config.BlockedIPs?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ip => ip.Trim())
                    .Where(ip => !string.IsNullOrWhiteSpace(ip))
                    .ToList() ?? new List<string>();

                if (!blockedIPs.Any(ip => IsIPMatch(clientIP, ip)))
                {
                    blockedIPs.Add(clientIP);
                    config.BlockedIPs = string.Join(",", blockedIPs);
                    _configManager.SaveConfig(config);
                    _logger.LogInfo(I18nService.Instance.T("ddos.added_permanent", clientIP));
                }
            }
            else
            {
                config.BlockedIPs = clientIP;
                _configManager.SaveConfig(config);
                _logger.LogInfo(I18nService.Instance.T("ddos.added_permanent", clientIP));
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
            var now = DateTime.Now;
            var config = _configManager.GetConfig();
            var blockDuration = TimeSpan.FromMinutes(config.BlockDurationMinutes);

            var expiredIPs = _blockedIPs.Where(kvp => now - kvp.Value > blockDuration)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var ip in expiredIPs)
            {
                _blockedIPs.TryRemove(ip, out _);
                _requestCounters.TryRemove(ip, out _);
            }

            foreach (var kvp in _requestCounters)
            {
                lock (kvp.Value)
                {
                    kvp.Value.Requests.RemoveAll(r => now - r > TimeSpan.FromMinutes(1));
                }
            }
        }

        public void UnblockIP(string clientIP)
        {
            _blockedIPs.TryRemove(clientIP, out _);
            _requestCounters.TryRemove(clientIP, out _);
            
            var config = _configManager.GetConfig();
            if (!string.IsNullOrWhiteSpace(config.BlockedIPs))
            {
                var blockedIPs = config.BlockedIPs?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(ip => ip.Trim())
                    .Where(ip => !string.IsNullOrWhiteSpace(ip) && !IsIPMatch(clientIP, ip))
                    .ToList() ?? new List<string>();

                config.BlockedIPs = string.Join(",", blockedIPs);
                _configManager.SaveConfig(config);
            }
            
            _logger.LogInfo(I18nService.Instance.T("ddos.manually_unblocked", clientIP));
        }

        public Dictionary<string, object> GetProtectionStatus()
        {
            var config = _configManager.GetConfig();
            var status = new Dictionary<string, object>
            {
                ["DDoSProtectionEnabled"] = config.DDoSProtection,
                ["MaxRequestsPerMinute"] = config.MaxRequestsPerMinute,
                ["BlockDurationMinutes"] = config.BlockDurationMinutes,
                ["CurrentlyBlockedIPs"] = _blockedIPs.Count,
                ["ActiveRequestCounters"] = _requestCounters.Count
            };

            return status;
        }

        private class RequestCounter
        {
            public List<DateTime> Requests { get; } = new List<DateTime>();
        }
    }
}
