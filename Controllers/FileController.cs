using Microsoft.AspNetCore.Mvc;
using Repository.Models;
using Repository.Services;
using System.IO;

namespace Repository.Controllers
{
    public class FileController : Controller
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly BlacklistService _blacklistService;
        private readonly VideoStreamingService _videoStreamingService;
        private readonly ProtectionService _protectionService;

        public FileController(ConfigManager configManager, Logger logger, BlacklistService blacklistService, VideoStreamingService videoStreamingService, ProtectionService protectionService)
        {
            _configManager = configManager;
            _logger = logger;
            _blacklistService = blacklistService;
            _videoStreamingService = videoStreamingService;
            _protectionService = protectionService;
        }

        [HttpGet("api/preview/{**filePath}")]
        public async Task<IActionResult> PreviewFile(string filePath, [FromQuery(Name = "token")] string? token = null)
        {
            var clientIP = GetClientIP();
            
            _logger.LogInfo($"开始处理文件预览请求，客户端IP: {clientIP}，文件路径: {filePath}");
            
            try
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        filePath = Uri.UnescapeDataString(filePath);
                        _logger.LogInfo($"URL解码后文件路径: {filePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"URL解码失败，使用原始路径: {filePath}，错误: {ex.Message}");
                    }
                }
                
                if (!string.IsNullOrEmpty(filePath) && ContainsIllegalCharacters(filePath))
                {
                    _logger.LogWarning($"检测到非法字符的文件预览请求，客户端IP: {clientIP}，文件路径: {filePath}");
                    return BadRequest("文件路径不合法");
                }
                
                if (Path.GetFileName(filePath).Equals("data.ini", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"尝试预览权限配置文件，客户端IP: {clientIP}，文件路径: {filePath}");
                    return NotFound("文件不存在");
                }
                
                if (Path.GetFileName(filePath).Equals("Protectionlock.json", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"尝试预览保护锁文件，客户端IP: {clientIP}，文件路径: {filePath}");
                    return NotFound("文件不存在");
                }
                
                if (ProtectionService.IsSystemPath(filePath))
                {
                    _logger.LogWarning($"尝试预览系统路径文件，客户端IP: {clientIP}，文件路径: {filePath}");
                    return NotFound("文件不存在");
                }
                
                var config = _configManager.GetConfig();
                
                if (!config.PreviewEnabled)
                {
                    _logger.LogWarning($"文件预览功能已禁用，客户端IP: {clientIP}，文件路径: {filePath}");
                    return StatusCode(403, "预览功能已禁用");
                }
                
                var repoPath = config.RepositoryPath;
                var fullPath = Path.Combine(repoPath, filePath);

                if (!IsPathValid(repoPath, fullPath))
                {
                    _logger.LogWarning($"安全警告！客户端IP: {clientIP}，尝试预览仓库外的文件: {filePath}");
                    return BadRequest("文件路径不合法");
                }

                var relPath = GetRelativePath(repoPath, fullPath);
                
                if (_protectionService.IsPathProtected(relPath) && !_protectionService.VerifyToken(relPath, token))
                {
                    _logger.LogWarning($"受保护文件预览被拒绝，客户端IP: {clientIP}，文件路径: {relPath}");
                    return NotFound("文件不存在");
                }
                
                if (IsPreviewForbidden(relPath))
                {
                    _logger.LogWarning($"文件预览被禁止预览列表阻止，客户端IP: {clientIP}，文件路径: {relPath}");
                    return NotFound("文件不存在");
                }
                
                if (IsDownloadForbidden(relPath))
                {
                    _logger.LogWarning($"文件预览被禁止下载列表阻止，客户端IP: {clientIP}，文件路径: {relPath}");
                    return NotFound("文件不存在");
                }
                
                if (_blacklistService.IsPathBlacklisted(relPath))
                {
                    _logger.LogWarning($"文件预览被黑名单阻止，客户端IP: {clientIP}，文件路径: {relPath}");
                    return NotFound("文件不存在");
                }

                bool fileExists = await Task.Run(() => System.IO.File.Exists(fullPath));
                if (!fileExists)
                {
                    _logger.LogWarning($"文件不存在，客户端IP: {clientIP}，文件路径: {fullPath}");
                    return NotFound("文件不存在");
                }

                var fileExtension = Path.GetExtension(fullPath).ToLowerInvariant();
                var fileName = Path.GetFileName(fullPath);
                
                var imageExtensions = new HashSet<string>(
                    config.ImagePreviewExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                var audioExtensions = new HashSet<string>(
                    config.AudioPreviewExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                var videoExtensions = new HashSet<string>(
                    config.VideoPreviewExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                var textExtensions = new HashSet<string>(
                    config.PreviewExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                
                var isVideo = videoExtensions.Contains(fileExtension);
                
                var fileInfo = await Task.Run(() => new System.IO.FileInfo(fullPath));
                if (!isVideo && fileInfo.Length > 10 * 1024 * 1024)
                {
                    _logger.LogWarning($"文件过大，拒绝预览，客户端IP: {clientIP}，文件路径: {filePath} ({FormatFileSize(fileInfo.Length)})");
                    return StatusCode(413, "文件过大");
                }
                
                if (imageExtensions.Contains(fileExtension))
                {
                    _logger.LogInfo($"图片预览成功，客户端IP: {clientIP}，文件路径: {filePath} ({FormatFileSize(fileInfo.Length)})");
                    var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                    return File(bytes, GetImageContentType(fileExtension));
                }
                
                if (audioExtensions.Contains(fileExtension))
                {
                    _logger.LogInfo($"音频预览成功，客户端IP: {clientIP}，文件路径: {filePath} ({FormatFileSize(fileInfo.Length)})");
                    var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                    return File(bytes, GetAudioContentType(fileExtension));
                }
                
                if (isVideo)
                {
                    _logger.LogInfo($"视频预览成功，客户端IP: {clientIP}，文件路径: {filePath} ({FormatFileSize(fileInfo.Length)})");
                    
                    var contentType = GetVideoContentType(fileExtension);
                    
                    var rangeHeader = Request.Headers["Range"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(rangeHeader))
                    {
                        var range = ParseRangeHeader(rangeHeader, fileInfo.Length);
                        if (range != null)
                        {
                            var (start, end) = range.Value;
                            var length = end - start + 1;
                            
                            Response.StatusCode = 206;
                            Response.Headers["Content-Range"] = $"bytes {start}-{end}/{fileInfo.Length}";
                            Response.Headers["Content-Length"] = length.ToString();
                            Response.Headers["Accept-Ranges"] = "bytes";
                            Response.ContentType = contentType;
                            
                            var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            fileStream.Seek(start, SeekOrigin.Begin);
                            
                            await CopyRangeAsync(fileStream, Response.Body, start, end);
                            return new EmptyResult();
                        }
                    }
                    
                    var videoBytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                    return File(videoBytes, contentType, enableRangeProcessing: true);
                }
                
                if (textExtensions.Contains(fileExtension))
                {
                    var fileContent = await System.IO.File.ReadAllTextAsync(fullPath);
                    _logger.LogInfo($"文本预览成功，客户端IP: {clientIP}，文件路径: {filePath} ({FormatFileSize(fileInfo.Length)})");
                    return Ok(new { name = fileName, content = fileContent });
                }
                
                _logger.LogWarning($"文件类型不支持预览，客户端IP: {clientIP}，文件路径: {filePath}");
                return StatusCode(415, "不支持的文件类型");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, $"权限不足，无法预览文件: {filePath}");
                return StatusCode(403, "没有访问权限");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, $"文件读取错误: {filePath}");
                return StatusCode(500, "文件读取失败");
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, $"内存不足，无法处理大文件: {filePath}");
                return StatusCode(500, "服务器内存不足");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"预览文件时发生未知错误: {filePath}");
                return StatusCode(500, "服务器内部错误");
            }
        }

        [HttpGet("api/download/{**filePath}")]
        public async Task<IActionResult> DownloadFile(string filePath, [FromQuery(Name = "token")] string? token = null)
        {
            var clientIP = GetClientIP();
            
            _logger.LogInfo($"开始处理文件下载请求，客户端IP: {clientIP}，文件路径: {filePath}");
            
            try
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        filePath = Uri.UnescapeDataString(filePath);
                        _logger.LogInfo($"URL解码后文件路径: {filePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"URL解码失败，使用原始路径: {filePath}，错误: {ex.Message}");
                    }
                }
                
                if (!string.IsNullOrEmpty(filePath) && ContainsIllegalCharacters(filePath))
                {
                    _logger.LogWarning($"检测到非法字符的文件下载请求，客户端IP: {clientIP}，文件路径: {filePath}");
                    return BadRequest("文件路径不合法");
                }
                
                if (Path.GetFileName(filePath).Equals("data.ini", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"尝试下载权限配置文件，客户端IP: {clientIP}，文件路径: {filePath}");
                    return NotFound("文件不存在");
                }
                
                if (Path.GetFileName(filePath).Equals("Protectionlock.json", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"尝试下载保护锁文件，客户端IP: {clientIP}，文件路径: {filePath}");
                    return NotFound("文件不存在");
                }
                
                if (ProtectionService.IsSystemPath(filePath))
                {
                    _logger.LogWarning($"尝试下载系统路径文件，客户端IP: {clientIP}，文件路径: {filePath}");
                    return NotFound("文件不存在");
                }
                
                var config = _configManager.GetConfig();
                var repoPath = config.RepositoryPath;
                var fullPath = Path.Combine(repoPath, filePath);

                if (!IsPathValid(repoPath, fullPath))
                {
                    _logger.LogWarning($"安全警告！客户端IP: {clientIP}，尝试下载仓库外的文件: {filePath}");
                    return BadRequest("文件路径不合法");
                }

                var relPath = GetRelativePath(repoPath, fullPath);
                
                if (_protectionService.IsPathProtected(relPath) && !_protectionService.VerifyToken(relPath, token))
                {
                    _logger.LogWarning($"受保护文件下载被拒绝，客户端IP: {clientIP}，文件路径: {relPath}");
                    return NotFound("文件不存在");
                }
                
                _logger.LogInfo($"下载请求处理中，客户端IP: {clientIP}，请求文件: {filePath}，相对路径: {relPath}");
                
                var forbiddenPaths = config.ForbiddenDownloadPaths?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                _logger.LogInfo($"当前禁止下载路径配置: {config.ForbiddenDownloadPaths ?? "空"}");
                
                if (IsDownloadForbidden(relPath))
                {
                    _logger.LogWarning($"文件下载被禁止下载列表阻止，客户端IP: {clientIP}，文件路径: {relPath}");
                    return NotFound("文件不存在");
                }
                
                _logger.LogInfo($"文件不在禁止下载列表中，继续处理下载请求，客户端IP: {clientIP}，文件路径: {relPath}");
                if (_blacklistService.IsPathBlacklisted(relPath))
                {
                    _logger.LogWarning($"文件下载被黑名单阻止，客户端IP: {clientIP}，文件路径: {relPath}");
                    return NotFound("文件不存在");
                }

                bool fileExists = await Task.Run(() => System.IO.File.Exists(fullPath));
                if (!fileExists)
                {
                    _logger.LogWarning($"文件不存在，客户端IP: {clientIP}，文件路径: {fullPath}");
                    return NotFound("文件不存在");
                }

                var fileInfo = await Task.Run(() => new System.IO.FileInfo(fullPath));
                var maxDownloadSizeBytes = config.MaxDownloadSizeMB * 1024 * 1024;
                if (fileInfo.Length > maxDownloadSizeBytes)
                {
                    _logger.LogWarning($"文件过大，拒绝下载，客户端IP: {clientIP}，文件路径: {filePath} ({FormatFileSize(fileInfo.Length)})，限制大小: {FormatFileSize(maxDownloadSizeBytes)}");
                    return StatusCode(413, $"文件过大（超过{config.MaxDownloadSizeMB}MB）");
                }

                var fileExtension = Path.GetExtension(fullPath).ToLowerInvariant();
                var fileName = Path.GetFileName(fullPath);
                
                _logger.LogInfo($"文件下载成功，客户端IP: {clientIP}，文件路径: {filePath} ({FormatFileSize(fileInfo.Length)})");
                
                Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["Expires"] = "0";
                
                var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                return File(bytes, GetContentType(fileExtension), fileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, $"权限不足，无法下载文件: {filePath}");
                return StatusCode(403, "没有访问权限");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, $"文件读取错误: {filePath}");
                return StatusCode(500, "文件读取失败");
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, $"内存不足，无法处理大文件: {filePath}");
                return StatusCode(500, "服务器内存不足");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"下载文件时发生未知错误: {filePath}");
                return StatusCode(500, "服务器内部错误");
            }
        }

        private string GetContentType(string extension)
        {
            var imageContentType = GetImageContentType(extension);
            if (imageContentType != "application/octet-stream")
            {
                return imageContentType;
            }
            
            var audioContentType = GetAudioContentType(extension);
            if (audioContentType != "application/octet-stream")
            {
                return audioContentType;
            }
            
            var videoContentType = GetVideoContentType(extension);
            if (videoContentType != "application/octet-stream")
            {
                return videoContentType;
            }
            
            return extension.ToLower() switch
            {
                ".txt" or ".log" or ".md" => "text/plain",
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".zip" => "application/zip",
                ".rar" => "application/x-rar-compressed",
                ".7z" => "application/x-7z-compressed",
                ".tar" => "application/x-tar",
                ".gz" => "application/gzip",
                _ => "application/octet-stream"
            };
        }

        private string GetImageContentType(string extension)
        {
            return extension.ToLower() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".tiff" or ".tif" => "image/tiff",
                _ => "application/octet-stream"
            };
        }
        
        private string GetAudioContentType(string extension)
        {
            return extension.ToLower() switch
            {
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".flac" => "audio/flac",
                ".aac" => "audio/aac",
                ".m4a" => "audio/mp4",
                ".wma" => "audio/x-ms-wma",
                _ => "application/octet-stream"
            };
        }

        private string GetVideoContentType(string extension)
        {
            return extension.ToLower() switch
            {
                ".mp4" => "video/mp4",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                ".wmv" => "video/x-ms-wmv",
                ".flv" => "video/x-flv",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".m4v" => "video/mp4",
                ".3gp" => "video/3gpp",
                ".ogv" => "video/ogg",
                _ => "application/octet-stream"
            };
        }

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

        private bool ContainsIllegalCharacters(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            
            var illegalPatterns = new[] { "..", "~", "$", "&", "|", ";", "<", ">", "\"", "'", "`", "\n", "\r", "\t" };
            
            return illegalPatterns.Any(pattern => path.Contains(pattern));
        }

        private bool IsPreviewForbidden(string relativePath)
        {
            var config = _configManager.GetConfig();
            var forbiddenPaths = config.ForbiddenPreviewPaths?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            
            foreach (var forbiddenPath in forbiddenPaths)
            {
                var trimmedPath = forbiddenPath.Trim();
                
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

        private bool IsDownloadForbidden(string relativePath)
        {
            var config = _configManager.GetConfig();
            var forbiddenPaths = config.ForbiddenDownloadPaths?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            
            foreach (var forbiddenPath in forbiddenPaths)
            {
                var trimmedPath = forbiddenPath.Trim();
                
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

        private (long start, long end)? ParseRangeHeader(string rangeHeader, long fileLength)
        {
            try
            {
                if (rangeHeader.StartsWith("bytes="))
                {
                    var rangeSpec = rangeHeader.Substring(6);
                    var parts = rangeSpec.Split('-');
                    
                    if (parts.Length == 2)
                    {
                        long start = 0;
                        long end = fileLength - 1;
                        
                        if (!string.IsNullOrEmpty(parts[0]))
                        {
                            start = long.Parse(parts[0]);
                        }
                        
                        if (!string.IsNullOrEmpty(parts[1]))
                        {
                            end = long.Parse(parts[1]);
                        }
                        
                        if (start <= end && start < fileLength)
                        {
                            end = Math.Min(end, fileLength - 1);
                            return (start, end);
                        }
                    }
                }
            }
            catch
            {
            }
            
            return null;
        }

        private async Task CopyRangeAsync(Stream source, Stream destination, long start, long end)
        {
            var buffer = new byte[81920];
            var bytesToCopy = end - start + 1;
            var bytesCopied = 0L;
            
            while (bytesCopied < bytesToCopy)
            {
                var bytesToRead = (int)Math.Min(buffer.Length, bytesToCopy - bytesCopied);
                var bytesRead = await source.ReadAsync(buffer, 0, bytesToRead);
                
                if (bytesRead == 0)
                {
                    break;
                }
                
                await destination.WriteAsync(buffer, 0, bytesRead);
                bytesCopied += bytesRead;
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}
