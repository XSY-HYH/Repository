using Repository.Models;
using Repository.Services;

namespace Repository.Services
{
    public class IPBlockingService
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;

        public IPBlockingService(ConfigManager configManager, Logger logger)
        {
            _configManager = configManager;
            _logger = logger;
        }

        public bool IsIPAllowed(string clientIP)
        {
            var config = _configManager.GetConfig();
            
            // 如果IP Blocking未启用，允许所有IP访问
            if (!config.IPBlocking)
            {
                return true;
            }

            // 解析IP白名单
            var allowedIPs = ParseIPBlockingList(config.IPBlockingList);
            
            // 检查客户端IP是否在白名单中
            foreach (var allowedIP in allowedIPs)
            {
                if (IsIPMatch(clientIP, allowedIP))
                {
                    _logger.LogInfo(I18nService.Instance.T("ipblocking.ip_allowed", clientIP, allowedIP));
                    return true;
                }
            }

            _logger.LogWarning(I18nService.Instance.T("ipblocking.ip_blocked", clientIP));
            return false;
        }

        private List<string> ParseIPBlockingList(string ipBlockingList)
        {
            var allowedIPs = new List<string>();
            
            if (string.IsNullOrWhiteSpace(ipBlockingList))
            {
                return allowedIPs;
            }

            var entries = ipBlockingList.Split(';', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var entry in entries)
            {
                var trimmedEntry = entry.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedEntry))
                {
                    allowedIPs.Add(trimmedEntry);
                }
            }

            return allowedIPs;
        }

        private bool IsIPMatch(string clientIP, string allowedIP)
        {
            // 如果允许的IP包含端口号，移除端口号进行比较
            var allowedIPWithoutPort = allowedIP;
            if (allowedIP.Contains(':'))
            {
                var parts = allowedIP.Split(':');
                if (parts.Length == 2)
                {
                    allowedIPWithoutPort = parts[0];
                }
            }

            // 支持通配符匹配
            if (allowedIPWithoutPort.Contains('*'))
            {
                var pattern = allowedIPWithoutPort.Replace(".", "\\.").Replace("*", ".*");
                return System.Text.RegularExpressions.Regex.IsMatch(clientIP, $"^{pattern}$");
            }

            // 精确匹配
            return clientIP == allowedIPWithoutPort;
        }

        public void UpdateIPBlocking(bool enabled, string ipList)
        {
            var config = _configManager.GetConfig();
            config.IPBlocking = enabled;
            config.IPBlockingList = ipList;
            _configManager.SaveConfig(config);
            
            _logger.LogInfo(I18nService.Instance.T("ipblocking.updated", enabled, ipList));
        }
    }
}