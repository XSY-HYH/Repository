using Repository.Services;

namespace Repository.Middleware
{
    public class IPBlockingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IPBlockingService _ipBlockingService;
        private readonly Logger _logger;

        public IPBlockingMiddleware(RequestDelegate next, IPBlockingService ipBlockingService, Logger logger)
        {
            _next = next;
            _ipBlockingService = ipBlockingService;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 获取客户端IP地址
            var clientIP = GetClientIP(context);
            
            // 检查IP是否允许访问
            if (!_ipBlockingService.IsIPAllowed(clientIP))
            {
                _logger.LogWarning($"IP Blocking: Access denied for {clientIP} to {context.Request.Path}");
                context.Response.StatusCode = 303; // 暂时重定向状态码
                return;
            }

            // IP允许访问，继续处理请求
            await _next(context);
        }

        private string GetClientIP(HttpContext context)
        {
            // 尝试从X-Forwarded-For头部获取真实IP（如果使用代理）
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                var ips = forwardedFor.Split(',').Select(ip => ip.Trim()).ToList();
                if (ips.Count > 0)
                {
                    return ips[0]; // 返回第一个IP（最接近客户端的IP）
                }
            }

            // 尝试从X-Real-IP头部获取
            var realIP = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(realIP))
            {
                return realIP;
            }

            // 最后使用连接远程IP地址
            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
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