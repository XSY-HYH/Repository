using Repository.Services;

namespace Repository.Middleware
{
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly DDoSProtectionService _ddosProtectionService;
        private readonly Logger _logger;

        public RateLimitingMiddleware(RequestDelegate next, DDoSProtectionService ddosProtectionService, Logger logger)
        {
            _next = next;
            _ddosProtectionService = ddosProtectionService;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var clientIP = GetClientIP(context);
            
            if (_ddosProtectionService.IsIPBlocked(clientIP))
            {
                _logger.LogWarning($"Rate Limiting: Access denied for blocked client {clientIP} to {context.Request.Path}");
                context.Response.StatusCode = 429;
                await context.Response.WriteAsync("Too many requests. Your client has been temporarily blocked due to excessive requests.");
                return;
            }

            if (!_ddosProtectionService.CheckAndUpdateRequestRate(clientIP))
            {
                _logger.LogWarning($"Rate Limiting: Access denied for {clientIP} to {context.Request.Path} due to rate limiting");
                
                context.Response.StatusCode = 429;
                await context.Response.WriteAsync("Too many requests. Please try again later.");
                return;
            }

            await _next(context);
        }

        private string GetClientIP(HttpContext context)
        {
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                var ips = forwardedFor.Split(',').Select(ip => ip.Trim()).ToList();
                if (ips.Count > 0)
                {
                    return ips[0];
                }
            }

            var realIP = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(realIP))
            {
                return realIP;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
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
