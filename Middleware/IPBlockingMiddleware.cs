using Repository.Services;

namespace Repository.Middleware
{
    public class IPBlockingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IPBlockingService _ipBlockingService;
        private readonly ClientIPService _clientIPService;
        private readonly Logger _logger;

        public IPBlockingMiddleware(RequestDelegate next, IPBlockingService ipBlockingService, ClientIPService clientIPService, Logger logger)
        {
            _next = next;
            _ipBlockingService = ipBlockingService;
            _clientIPService = clientIPService;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var clientIP = _clientIPService.GetClientIP(context);
            
            if (!_ipBlockingService.IsIPAllowed(clientIP))
            {
                _logger.LogWarning(I18nService.Instance.T("ip_blocking.access_denied", clientIP, context.Request.Path));
                await AccessDeniedHandler.HandleAsync(context, "IP Blocked");
                return;
            }

            await _next(context);
        }
    }

    public static class IPBlockingMiddlewareExtensions
    {
        public static IApplicationBuilder UseIPBlocking(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<IPBlockingMiddleware>();
        }
    }
}