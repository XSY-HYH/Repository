using Microsoft.Extensions.FileProviders;
using Repository.Services;

namespace Repository.Middleware
{
    public class DynamicStaticFileMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly BlacklistService _blacklistService;
        private NetworkFileService _networkFileService;

        public DynamicStaticFileMiddleware(RequestDelegate next, ConfigManager configManager, Logger logger, BlacklistService blacklistService)
        {
            _next = next;
            _configManager = configManager;
            _logger = logger;
            _blacklistService = blacklistService;
            _networkFileService = new NetworkFileService(logger);
            
            _networkFileService.Initialize(_configManager.GetConfig().RepositoryPath);
            
            _configManager.OnConfigChanged += OnConfigChanged;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? I18nService.Instance.T("dynamic_static.unknown_ip");
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            var method = context.Request.Method;
            var requestPath = context.Request.Path.HasValue ? context.Request.Path.Value : "";
            
            _logger.LogAccess(ipAddress, userAgent, requestPath, method);
            
            if (context.Request.Path.StartsWithSegments("/download"))
            {
                _logger.LogSeparator();
                
                var path = context.Request.Path.Value?.Substring("/download".Length) ?? "/";
                
                var relativePath = path.TrimStart('/');
                
                if (relativePath.Contains('?'))
                {
                    relativePath = relativePath.Substring(0, relativePath.IndexOf('?'));
                }
                
                try
                {
                    relativePath = Uri.UnescapeDataString(relativePath);
                }
                catch (UriFormatException ex)
                {
                    _logger.LogError(ex, I18nService.Instance.T("dynamic_static.path_decode_failed"));
                    context.Response.StatusCode = 400;
                    _logger.LogSeparator();
                    return;
                }
                
                relativePath = relativePath.Replace("\\", "/");
                
                _logger.LogInfo($"Path processing - Original: {path}, Relative: {relativePath}");
                
                _logger.LogInfo($"DynamicStaticFileMiddleware: Processing download request - Original path: {path}, Relative path: {relativePath}");
                
                var fileInfo = await Task.Run(() => _networkFileService.GetFileInfo(relativePath));
                
                if (fileInfo != null)
                {
                    _logger.LogInfo($"File info - Exists: {fileInfo.Exists}, IsDirectory: {fileInfo.IsDirectory}, Name: {fileInfo.Name}, PhysicalPath: {fileInfo.PhysicalPath}");
                }
                else
                {
                    _logger.LogWarning($"File info is null for path: {relativePath}");
                }
                
                if (fileInfo != null && fileInfo.Exists && !fileInfo.IsDirectory)
                {
                    var blacklistRelativePath = await Task.Run(() => GetRelativePath(_configManager.GetConfig().RepositoryPath, fileInfo.PhysicalPath ?? ""));
                    if (_blacklistService.IsPathBlacklisted(blacklistRelativePath))
                    {
                        _logger.LogWarning(I18nService.Instance.T("dynamic_static.download_blocked_blacklist", blacklistRelativePath));
                        context.Response.StatusCode = 404;
                        return;
                    }
                    
                    var config = _configManager.GetConfig();
                    var isForbidden = await Task.Run(() => IsDownloadForbidden(relativePath, config.ForbiddenDownloadPaths));
                    var resultText = isForbidden ? I18nService.Instance.T("dynamic_static.download_blocked_config") : I18nService.Instance.T("dynamic_static.check_forbidden");
                    _logger.LogInfo(I18nService.Instance.T("dynamic_static.download_forbidden_check", relativePath, config.ForbiddenDownloadPaths, resultText));
                    
                    if (isForbidden)
                    {
                        _logger.LogWarning(I18nService.Instance.T("dynamic_static.download_blocked_config", relativePath));
                        context.Response.StatusCode = 403;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync($"{{\"error\":\"{I18nService.Instance.T("dynamic_static.download_forbidden_response")}\"}}");
                        _logger.LogSeparator();
                        return;
                    }
                    
                    context.Response.ContentType = GetContentType(fileInfo.Name);
                    
                    var fileName = Path.GetFileName(fileInfo.Name);
                    var encodedFileName = Uri.EscapeDataString(fileName);
                    context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{encodedFileName}\"; filename*=UTF-8''{encodedFileName}";
                    
                    await context.Response.SendFileAsync(fileInfo);
                    
                    _logger.LogSeparator();
                    
                    return;
                }
                else
                {
                    context.Response.StatusCode = 404;
                    
                    _logger.LogSeparator();
                    
                    return;
                }
            }
            
            await _next(context);
        }

        private void OnConfigChanged(object? sender, Models.Config newConfig)
        {
            try
            {
                var currentPath = _networkFileService.GetCurrentPath();
                if (currentPath != newConfig.RepositoryPath)
                {
                    _logger.LogInfo($"Updating network file service from {currentPath} to {newConfig.RepositoryPath}");
                    
                    _networkFileService.UpdatePath(newConfig.RepositoryPath);
                    
                    _logger.LogInfo("Network file service updated successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update network file service");
            }
        }

        private static string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".txt" => "text/plain",
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".mp4" => "video/mp4",
                ".mp3" => "audio/mpeg",
                _ => "application/octet-stream"
            };
        }

        private string GetRelativePath(string repositoryPath, string fullPath)
        {
            var relativePath = Path.GetRelativePath(repositoryPath, fullPath);
            return relativePath.Replace("\\", "/");
        }

        private bool IsDownloadForbidden(string relativePath, string forbiddenPaths)
        {
            if (string.IsNullOrWhiteSpace(forbiddenPaths))
                return false;

            _logger.LogInfo(I18nService.Instance.T("dynamic_static.check_forbidden", relativePath));
            
            var normalizedPath = relativePath.TrimStart('/');
            _logger.LogInfo(I18nService.Instance.T("dynamic_static.normalized_path", normalizedPath));
            
            var forbiddenList = forbiddenPaths.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .ToList();
            
            _logger.LogInfo(I18nService.Instance.T("dynamic_static.forbidden_list", string.Join(", ", forbiddenList)));
            
            foreach (var forbiddenPath in forbiddenList)
            {
                var normalizedForbiddenPath = forbiddenPath.TrimStart('/');
                _logger.LogInfo(I18nService.Instance.T("dynamic_static.check_path", normalizedForbiddenPath));
                
                if (string.Equals(normalizedPath, normalizedForbiddenPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInfo(I18nService.Instance.T("dynamic_static.exact_match", normalizedPath, normalizedForbiddenPath));
                    return true;
                }
                
                if (normalizedForbiddenPath.EndsWith("/"))
                {
                    var directoryPath = normalizedForbiddenPath.TrimEnd('/');
                    if (normalizedPath.StartsWith(directoryPath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInfo(I18nService.Instance.T("dynamic_static.directory_match", normalizedPath, directoryPath));
                        return true;
                    }
                }
                
                if (!normalizedForbiddenPath.Contains("/"))
                {
                    var fileName = Path.GetFileName(normalizedPath);
                    if (string.Equals(fileName, normalizedForbiddenPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInfo(I18nService.Instance.T("dynamic_static.filename_match", fileName, normalizedForbiddenPath));
                        return true;
                    }
                }
            }
            
            _logger.LogInfo(I18nService.Instance.T("dynamic_static.no_match", normalizedPath));
            return false;
        }

        public void Dispose()
        {
            _networkFileService?.Dispose();
            _configManager.OnConfigChanged -= OnConfigChanged;
        }
    }

    public static class DynamicStaticFileMiddlewareExtensions
    {
        public static IApplicationBuilder UseDynamicStaticFiles(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<DynamicStaticFileMiddleware>();
        }
    }
}
