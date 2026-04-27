using Repository.Models;

namespace Repository.Services
{
    public class RequestHeaderFilteringService
    {
        private readonly ConfigManager _configManager;
        private readonly ClientIPService _clientIPService;
        private readonly Logger _logger;

        public RequestHeaderFilteringService(ConfigManager configManager, ClientIPService clientIPService, Logger logger)
        {
            _configManager = configManager;
            _clientIPService = clientIPService;
            _logger = logger;
        }

        public bool ValidateRequestHeaders(HttpContext context)
        {
            var config = _configManager.GetConfig();
            
            if (!config.RequestHeaderFiltering)
            {
                return true;
            }

            // 跳过 WebSocket 端点的头部检查（浏览器无法为 WebSocket 连接添加自定义请求头）
            var path = context.Request.Path.Value ?? "";
            if (path.EndsWith("/ws") || path.StartsWith("/admin/ws"))
            {
                return true;
            }

            var userAgent = context.Request.Headers["User-Agent"].FirstOrDefault();
            var accept = context.Request.Headers["Accept"].FirstOrDefault();

            if (config.RequireUserAgent)
            {
                if (string.IsNullOrWhiteSpace(userAgent))
                {
                    var clientIP = _clientIPService.GetClientIP(context);
                    _logger.LogWarning(I18nService.Instance.T("header_filter.missing_user_agent", clientIP, context.Request.Path));
                    return false;
                }

                if (!IsKnownBrowser(userAgent, config.AllowedBrowsers))
                {
                    var clientIP = _clientIPService.GetClientIP(context);
                    _logger.LogWarning(I18nService.Instance.T("header_filter.unknown_browser", clientIP, context.Request.Path, userAgent));
                    return false;
                }
            }

            if (config.RequireAccept && string.IsNullOrWhiteSpace(accept))
            {
                var clientIP = _clientIPService.GetClientIP(context);
                _logger.LogWarning(I18nService.Instance.T("header_filter.missing_accept", clientIP, context.Request.Path));
                return false;
            }

            return true;
        }

        private bool IsKnownBrowser(string userAgent, string allowedBrowsers)
        {
            var browsers = allowedBrowsers?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToList() ?? new List<string>();

            if (browsers.Count == 0)
            {
                return true;
            }

            foreach (var browser in browsers)
            {
                if (userAgent.Contains(browser))
                {
                    if (browser == "Safari" && userAgent.Contains("Chrome"))
                    {
                        continue;
                    }
                    return true;
                }
            }
            return false;
        }

        public Dictionary<string, object> GetFilteringStatus()
        {
            var config = _configManager.GetConfig();
            return new Dictionary<string, object>
            {
                ["RequestHeaderFilteringEnabled"] = config.RequestHeaderFiltering,
                ["RequireUserAgent"] = config.RequireUserAgent,
                ["RequireAccept"] = config.RequireAccept,
                ["AllowedBrowsers"] = config.AllowedBrowsers
            };
        }
    }
}
