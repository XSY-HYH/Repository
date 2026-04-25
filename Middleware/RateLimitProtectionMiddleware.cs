using Repository.Services;

namespace Repository.Middleware
{
    public class RateLimitProtectionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RateLimitProtectionService _rateLimitService;
        private readonly Logger _logger;

        public RateLimitProtectionMiddleware(
            RequestDelegate next, 
            RateLimitProtectionService rateLimitService,
            Logger logger)
        {
            _next = next;
            _rateLimitService = rateLimitService;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_rateLimitService.CheckRequest())
            {
                var remainingSeconds = _rateLimitService.RemainingPauseSeconds;
                var retryAfter = remainingSeconds > 0 ? remainingSeconds : 60;
                
                context.Response.StatusCode = 503;
                context.Response.Headers["Retry-After"] = retryAfter.ToString();
                context.Response.ContentType = "text/plain; charset=utf-8";
                
                await context.Response.WriteAsync(
                    I18nService.Instance.T("rate_limit.service_unavailable", retryAfter));
                return;
            }

            await _next(context);
        }
    }

    public static class RateLimitProtectionMiddlewareExtensions
    {
        public static IApplicationBuilder UseRateLimitProtection(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RateLimitProtectionMiddleware>();
        }
    }
}
