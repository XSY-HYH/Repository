namespace Repository.Services
{
    public static class AccessDeniedHandler
    {
        public static async Task HandleAsync(HttpContext context, string reason = "")
        {
            context.Abort();
            await Task.CompletedTask;
        }
    }
}
