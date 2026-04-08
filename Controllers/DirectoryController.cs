using Microsoft.AspNetCore.Mvc;
using Repository.Models;
using Repository.Services;
using System.IO;

namespace Repository.Controllers
{
    public class DirectoryController : Controller
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly BlacklistService _blacklistService;
        private readonly ProtectionService _protectionService;

        public DirectoryController(ConfigManager configManager, Logger logger, BlacklistService blacklistService, ProtectionService protectionService)
        {
            _configManager = configManager;
            _logger = logger;
            _blacklistService = blacklistService;
            _protectionService = protectionService;
        }

        [HttpGet("api/files")]
        public async Task<IActionResult> GetFiles([FromQuery(Name = "path")] string path = "", [FromQuery(Name = "token")] string? token = null, [FromQuery(Name = "session")] string? session = null)
        {
            var clientIP = GetClientIP();
            
            try
            {
                if (Request.Query.ContainsKey("path[]") || Request.Query["path"].Count > 1)
                {
                    _logger.LogWarning($"检测到参数污染攻击，客户端IP: {clientIP}，查询字符串: {Request.QueryString}");
                    return BadRequest("参数不合法");
                }
                
                if (!string.IsNullOrEmpty(path) && ContainsIllegalCharacters(path))
                {
                    _logger.LogWarning($"检测到非法字符的路径请求，客户端IP: {clientIP}，路径: {path}");
                    return BadRequest("路径不合法");
                }
                
                var repoPath = _configManager.GetConfig().RepositoryPath;
                var fullPath = BuildFullPath(repoPath, path);
                
                if (!IsPathValid(repoPath, fullPath))
                {
                    _logger.LogWarning($"有人想搞事情！客户端IP: {clientIP}，尝试访问仓库外的路径: {path}");
                    return BadRequest("路径不合法");
                }

                if (ProtectionService.IsSystemPath(path))
                {
                    _logger.LogWarning($"尝试访问系统路径，客户端IP: {clientIP}，路径: {path}");
                    return NotFound("目录不存在");
                }

                if (_protectionService.IsPathProtected(path))
                {
                    var authMethod = _protectionService.GetAuthMethod(path);
                    
                    if (authMethod == "secure")
                    {
                        if (!_protectionService.VerifySession(path, session))
                        {
                            _logger.LogWarning($"Secure保护目录访问被拒绝，客户端IP: {clientIP}，路径: {path}");
                            return NotFound("目录不存在");
                        }
                    }
                    else
                    {
                        if (!_protectionService.VerifyToken(path, token))
                        {
                            _logger.LogWarning($"Token保护目录访问被拒绝，客户端IP: {clientIP}，路径: {path}");
                            return NotFound("目录不存在");
                        }
                    }
                }

                bool dirExists = await Task.Run(() => Directory.Exists(fullPath));
                if (!dirExists)
                {
                    _logger.LogWarning($"目录不存在，客户端IP: {clientIP}，路径: {fullPath}");
                    return NotFound("目录不存在");
                }

                var listing = new DirectoryListing { CurrentPath = path };
                
                var config = _configManager.GetConfig();
                
                await Task.WhenAll(
                    ProcessDirectoriesAsync(repoPath, fullPath, path, listing, config),
                    ProcessFilesAsync(repoPath, fullPath, path, listing, config)
                );

                _logger.LogInfo($"成功返回目录列表，客户端IP: {clientIP}，路径: {path}");
                return Ok(listing);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, $"权限不足，无法访问路径: {path}");
                return StatusCode(403, "没有访问权限");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, $"IO错误，路径: {path}");
                return StatusCode(500, "文件系统错误");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取文件列表时出错: {path}");
                return StatusCode(500, "服务器内部错误");
            }
        }

        private async Task ProcessDirectoriesAsync(string repoPath, string fullPath, string relativePath, DirectoryListing listing, Config config)
        {
            var directories = await Task.Run(() => Directory.EnumerateDirectories(fullPath).ToList());
            var hiddenPaths = config.HiddenPaths?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                var relPath = GetRelativePath(repoPath, dir);
                
                if (IsSystemDirectory(dirName))
                {
                    continue;
                }
                
                if (_blacklistService.IsPathBlacklisted(relPath))
                {
                    continue;
                }
                
                if (IsPathHidden(relPath, hiddenPaths))
                {
                    continue;
                }
                
                if (_protectionService.IsPathProtected(relPath))
                {
                    continue;
                }
                
                var dirUrl = GetRelativeUrl(relativePath, dirName, true);
                var dirInfo = await Task.Run(() => new System.IO.DirectoryInfo(dir));
                
                lock (listing.Directories)
                {
                    listing.Directories.Add(new Models.DirectoryInfo
                    {
                        Name = dirName,
                        Url = dirUrl,
                        LastModified = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        CanView = true,
                        CanDownload = false
                    });
                }
            }
        }
        
        private bool IsSystemDirectory(string dirName)
        {
            if (string.IsNullOrEmpty(dirName))
                return false;
            
            var systemDirs = new[] { ".keys" };
            return systemDirs.Any(sd => string.Equals(dirName, sd, StringComparison.OrdinalIgnoreCase));
        }

        private async Task ProcessFilesAsync(string repoPath, string fullPath, string relativePath, DirectoryListing listing, Config config)
        {
            var files = await Task.Run(() => Directory.EnumerateFiles(fullPath).ToList());
            
            var textExtensions = config.PreviewExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var imageExtensions = config.ImagePreviewExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var audioExtensions = config.AudioPreviewExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var videoExtensions = config.VideoPreviewExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            
            var allPreviewableExtensions = new HashSet<string>(
                textExtensions.Concat(imageExtensions).Concat(audioExtensions).Concat(videoExtensions)
                    .Select(ext => ext.Trim().ToLower())
                    .Where(ext => !string.IsNullOrEmpty(ext)),
                StringComparer.OrdinalIgnoreCase
            );
            
            var hiddenPaths = config.HiddenPaths?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var relPath = GetRelativePath(repoPath, file);
                
                if (fileName.EndsWith(".Uploadlnk", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                if (fileName.Equals("data.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                if (fileName.Equals("Protectionlock.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                if (_blacklistService.IsPathBlacklisted(relPath))
                {
                    continue;
                }
                
                if (IsPathHidden(relPath, hiddenPaths))
                {
                    continue;
                }
                
                if (_protectionService.IsPathProtected(relPath))
                {
                    continue;
                }
                
                var fileExtension = Path.GetExtension(file).ToLower();
                var isPreviewable = allPreviewableExtensions.Contains(fileExtension);
                
                var relativeUrl = GetRelativeUrl(relativePath, fileName, false);
                var fileInfo = await Task.Run(() => new System.IO.FileInfo(file));
                
                lock (listing.Files)
                {
                    listing.Files.Add(new Models.FileInfo
                    {
                        Name = fileName,
                        Url = relativeUrl,
                        LastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Size = fileInfo.Length,
                        Previewable = isPreviewable,
                        CanView = true,
                        CanDownload = true,
                        CanPreview = isPreviewable
                    });
                }
            }
        }

        /// <summary>
        /// 获取客户端IP地址
        /// </summary>
        private string GetClientIP()
        {
            try
            {
                var forwardedFor = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(forwardedFor))
                {
                    var ips = forwardedFor.Split(',').Select(ip => ip.Trim());
                    return ips.FirstOrDefault() ?? "Unknown";
                }
                
                var realIP = HttpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(realIP))
                {
                    return realIP;
                }
                
                return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取客户端IP地址时发生错误");
                return "Unknown";
            }
        }

        /// <summary>
        /// 构建完整路径
        /// </summary>
        private string BuildFullPath(string repoPath, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return repoPath;
            }
            
            path = path.Replace('/', Path.DirectorySeparatorChar);
            
            if (path.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                path = path.Substring(1);
            }
            
            return Path.Combine(repoPath, path);
        }

        /// <summary>
        /// 获取相对路径
        /// </summary>
        private string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(fullPath))
            {
                return fullPath;
            }
            
            basePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar);
            fullPath = Path.GetFullPath(fullPath);
            
            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = fullPath.Substring(basePath.Length);
                
                if (!relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    relativePath = Path.DirectorySeparatorChar + relativePath;
                }
                
                return relativePath.Replace(Path.DirectorySeparatorChar, '/');
            }
            
            return fullPath;
        }

        /// <summary>
        /// 获取相对URL
        /// </summary>
        private string GetRelativeUrl(string currentPath, string name, bool isDirectory)
        {
            var normalizedPath = NormalizePath(currentPath);
            
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return isDirectory ? $"{name}/" : name;
            }
            
            var segments = normalizedPath.Split('/');
            if (segments.Length > 0 && segments[segments.Length - 1] == name)
            {
                return isDirectory ? $"{normalizedPath}/" : normalizedPath;
            }
            
            var url = $"{normalizedPath}/{name}";
            if (isDirectory)
            {
                url += "/";
            }
            return url;
        }

        /// <summary>
        /// 规范化路径
        /// </summary>
        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }
            
            if (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }
            
            if (path.EndsWith("/"))
            {
                path = path.Substring(0, path.Length - 1);
            }
            
            return path;
        }

        /// <summary>
        /// 检查路径是否合法
        /// </summary>
        private bool IsPathValid(string basePath, string fullPath)
        {
            try
            {
                var baseDir = Path.GetFullPath(basePath);
                var fullDir = Path.GetFullPath(fullPath);
                return fullDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查路径是否包含非法字符
        /// </summary>
        private bool ContainsIllegalCharacters(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            
            var illegalPatterns = new[] { "..", "~", "$", "&", "|", ";", "<", ">", "\"", "'", "`", "\n", "\r", "\t" };
            
            return illegalPatterns.Any(pattern => path.Contains(pattern));
        }

        /// <summary>
        /// 检查路径是否在隐藏列表中
        /// </summary>
        private bool IsPathHidden(string relativePath, string[] hiddenPaths)
        {
            foreach (var hiddenPath in hiddenPaths)
            {
                var trimmedPath = hiddenPath.Trim();
                
                if (relativePath.Equals(trimmedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                if (trimmedPath.EndsWith("/"))
                {
                    if (relativePath.StartsWith(trimmedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else if (relativePath.Equals(trimmedPath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }
    }
}
