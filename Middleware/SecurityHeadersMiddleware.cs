using Repository.Services;

namespace Repository.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Logger _logger;

        public SecurityHeadersMiddleware(RequestDelegate next, Logger logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                if (!headers.ContainsKey("X-Frame-Options"))
                {
                    headers["X-Frame-Options"] = "SAMEORIGIN";
                }

                if (!headers.ContainsKey("X-Content-Type-Options"))
                {
                    headers["X-Content-Type-Options"] = "nosniff";
                }

                if (!headers.ContainsKey("X-XSS-Protection"))
                {
                    headers["X-XSS-Protection"] = "1; mode=block";
                }

                if (!headers.ContainsKey("Content-Security-Policy"))
                {
                    headers["Content-Security-Policy"] = GetCspPolicy(context);
                }

                if (context.Request.IsHttps && !headers.ContainsKey("Strict-Transport-Security"))
                {
                    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                }

                if (!headers.ContainsKey("Referrer-Policy"))
                {
                    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                }

                if (!headers.ContainsKey("Permissions-Policy"))
                {
                    headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
                }

                return Task.CompletedTask;
            });

            await _next(context);
        }

        private static string GetCspPolicy(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";
            
            if (path.StartsWith("/admin"))
            {
                return "default-src 'self'; " +
                       "script-src 'self' 'unsafe-inline'; " +
                       "style-src 'self' 'unsafe-inline'; " +
                       "img-src 'self' data:; " +
                       "font-src 'self'; " +
                       "connect-src 'self' wss:; " +
                       "frame-ancestors 'self'; " +
                       "base-uri 'self'; " +
                       "form-action 'self'";
            }
            
            if (path.StartsWith("/swagger"))
            {
                return "default-src 'self'; " +
                       "script-src 'self' 'unsafe-inline'; " +
                       "style-src 'self' 'unsafe-inline'; " +
                       "img-src 'self' data:; " +
                       "font-src 'self' data:; " +
                       "connect-src 'self'; " +
                       "frame-ancestors 'self'";
            }

            return "default-src 'self'; " +
                   "script-src 'self'; " +
                   "style-src 'self' 'unsafe-inline'; " +
                   "img-src 'self' data: blob:; " +
                   "font-src 'self' data:; " +
                   "media-src 'self' blob:; " +
                   "connect-src 'self'; " +
                   "frame-ancestors 'self'; " +
                   "base-uri 'self'; " +
                   "form-action 'self'; " +
                   "object-src 'none'";
        }
    }

    public static class SecurityHeadersMiddlewareExtensions
    {
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SecurityHeadersMiddleware>();
        }
    }
}
