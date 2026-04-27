using Repository.Services;

namespace Repository.Middleware
{
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RequestThrottlingService _requestThrottlingService;
        private readonly ClientIPService _clientIPService;
        private readonly Logger _logger;

        public RateLimitingMiddleware(RequestDelegate next, RequestThrottlingService requestThrottlingService, ClientIPService clientIPService, Logger logger)
        {
            _next = next;
            _requestThrottlingService = requestThrottlingService;
            _clientIPService = clientIPService;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var clientIP = _clientIPService.GetClientIP(context);
            
            if (_requestThrottlingService.IsIPblocked(clientIP))
            {
                _logger.LogWarning(I18nService.Instance.T("rate_limiting.access_denied_blocked", clientIP, context.Request.Path));
                await AccessDeniedHandler.HandleAsync(context, "IP Blocked");
                return;
            }

            if (!_requestThrottlingService.CheckAndUpdateRequestRate(clientIP))
            {
                _logger.LogWarning(I18nService.Instance.T("rate_limiting.access_denied_limited", clientIP, context.Request.Path));
                await AccessDeniedHandler.HandleAsync(context, "Rate Limited");
                return;
            }

            await _next(context);
        }
    }

    public static class RateLimitingMiddlewareExtensions
    {
        public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RateLimitingMiddleware>();
        }
    }
}
