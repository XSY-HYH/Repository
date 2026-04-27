using Repository.Services;

namespace Repository.Middleware
{
    public class RequestHeaderFilteringMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RequestHeaderFilteringService _filteringService;
        private readonly Logger _logger;

        public RequestHeaderFilteringMiddleware(
            RequestDelegate next,
            RequestHeaderFilteringService filteringService,
            Logger logger)
        {
            _next = next;
            _filteringService = filteringService;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_filteringService.ValidateRequestHeaders(context))
            {
                await AccessDeniedHandler.HandleAsync(context, "Invalid Headers");
                return;
            }

            await _next(context);
        }
    }

    public static class RequestHeaderFilteringMiddlewareExtensions
    {
        public static IApplicationBuilder UseRequestHeaderFiltering(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RequestHeaderFilteringMiddleware>();
        }
    }
}
