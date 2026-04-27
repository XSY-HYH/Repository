using Repository.Models;

namespace Repository.Services
{
    public class ClientIPService
    {
        private readonly ConfigManager _configManager;
        private readonly ProxyProtocolService? _proxyProtocolService;

        public ClientIPService(ConfigManager configManager, ProxyProtocolService? proxyProtocolService = null)
        {
            _configManager = configManager;
            _proxyProtocolService = proxyProtocolService;
        }

        public string GetClientIP(HttpContext context)
        {
            var config = _configManager.GetConfig();

            if (config.ProxyProtocolEnabled && _proxyProtocolService != null)
            {
                var connectionId = context.Connection.Id;
                if (_proxyProtocolService.ConnectionIPs.TryGetValue(connectionId, out var proxyIP))
                {
                    return proxyIP;
                }
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }
    }
}
