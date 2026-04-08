using Microsoft.AspNetCore.Http;
using Repository.Services;

namespace Repository.Middleware
{
    public class SubdirectoryRoutingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Logger _logger;

        public SubdirectoryRoutingMiddleware(RequestDelegate next, Logger logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";
            
            _logger.LogInfo($"SubdirectoryRoutingMiddleware: Processing path: {path}");
            
            // 检查是否是子目录请求（不是API请求，不是下载请求，不是根目录）
            if (!path.StartsWith("/api/") && 
                !path.StartsWith("/download/") && 
                !path.StartsWith("/admin") &&
                path != "/" && 
                !path.StartsWith("/swagger") &&
                !path.StartsWith("/@vite"))
            {
                // 移除开头的斜杠和结尾的斜杠
                var cleanPath = path.Trim('/');
                
                // 如果路径包含多个部分，说明是子目录
                if (cleanPath.Contains('/') || !string.IsNullOrEmpty(cleanPath))
                {
                    // 重写路径为简单的查询参数格式
                    context.Request.Path = "/";
                    
                    // 添加路径到查询参数
                    var query = context.Request.QueryString.HasValue 
                        ? context.Request.QueryString.Value + "&" 
                        : "?";
                    
                    context.Request.QueryString = new QueryString(query + $"path={Uri.EscapeDataString(cleanPath)}");
                    
                    _logger.LogInfo($"Subdirectory routing: {path} -> /?path={Uri.EscapeDataString(cleanPath)}");
                }
            }
            else
            {
                _logger.LogInfo($"SubdirectoryRoutingMiddleware: Skipping path: {path}");
            }

            await _next(context);
        }
    }

    public static class SubdirectoryRoutingMiddlewareExtensions
    {
        public static IApplicationBuilder UseSubdirectoryRouting(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SubdirectoryRoutingMiddleware>();
        }
    }
}