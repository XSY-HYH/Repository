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
        private readonly ClientIPService _clientIPService;

        public FileController(ConfigManager configManager, Logger logger, BlacklistService blacklistService, VideoStreamingService videoStreamingService, ClientIPService clientIPService)
        {
            _configManager = configManager;
            _logger = logger;
            _blacklistService = blacklistService;
            _videoStreamingService = videoStreamingService;
            _clientIPService = clientIPService;
        }

        [HttpGet("api/preview/{**filePath}")]
        public async Task<IActionResult> PreviewFile(string filePath, [FromQuery(Name = "token")] string? token = null)
        {
            var clientIP = _clientIPService.GetClientIP(HttpContext);
            
            _logger.LogInfo(I18nService.Instance.T("file_preview.request", clientIP, filePath));
            
            try
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        filePath = Uri.UnescapeDataString(filePath);
                        _logger.LogInfo(I18nService.Instance.T("file_preview.url_decode_success", filePath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(I18nService.Instance.T("file_preview.url_decode_failed", filePath, ex.Message));
                    }
                }
                
                if (!string.IsNullOrEmpty(filePath) && ContainsIllegalCharacters(filePath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_preview.illegal_chars", clientIP, filePath));
                    return BadRequest(I18nService.Instance.T("file_preview.path_invalid"));
                }
                
                if (Path.GetFileName(filePath).Equals("data.ini", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_preview.permission_config", clientIP, filePath));
                    return NotFound(I18nService.Instance.T("file_preview.file_not_found"));
                }
                
                if (PathSecurity.IsSystemPath(filePath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_preview.system_path", clientIP, filePath));
                    return NotFound(I18nService.Instance.T("file_preview.file_not_found"));
                }
                
                var config = _configManager.GetConfig();
                
                if (!config.PreviewEnabled)
                {
                    _logger.LogWarning(I18nService.Instance.T("file_preview.feature_disabled", clientIP, filePath));
                    return StatusCode(403, I18nService.Instance.T("file_preview.preview_disabled_response"));
                }
                
                var repoPath = config.RepositoryPath;
                var fullPath = Path.Combine(repoPath, filePath);

                if (!IsPathValid(repoPath, fullPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_preview.outside_repo", clientIP, filePath));
                    return BadRequest(I18nService.Instance.T("file_preview.path_invalid"));
                }

                var relPath = GetRelativePath(repoPath, fullPath);
                
                if (IsPreviewForbidden(relPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_preview.forbidden_preview", clientIP, relPath));
                    return NotFound(I18nService.Instance.T("file_preview.file_not_found"));
                }
                
                if (IsDownloadForbidden(relPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_preview.forbidden_download", clientIP, relPath));
                    return NotFound(I18nService.Instance.T("file_preview.file_not_found"));
                }
                
                if (_blacklistService.IsPathBlacklisted(relPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_preview.blacklist_blocked", clientIP, relPath));
                    return NotFound(I18nService.Instance.T("file_preview.file_not_found"));
                }

                bool fileExists = await Task.Run(() => System.IO.File.Exists(fullPath));
                if (!fileExists)
                {
                    _logger.LogWarning(I18nService.Instance.T("file_preview.file_not_exist", clientIP, fullPath));
                    return NotFound(I18nService.Instance.T("file_preview.file_not_found"));
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
                    _logger.LogWarning(I18nService.Instance.T("file_preview.file_too_large", clientIP, filePath, FormatFileSize(fileInfo.Length)));
                    return StatusCode(413, I18nService.Instance.T("file_preview.file_too_large_response"));
                }
                
                if (imageExtensions.Contains(fileExtension))
                {
                    _logger.LogInfo(I18nService.Instance.T("file_preview.image_success", clientIP, filePath, FormatFileSize(fileInfo.Length)));
                    var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                    return File(bytes, GetImageContentType(fileExtension));
                }
                
                if (audioExtensions.Contains(fileExtension))
                {
                    _logger.LogInfo(I18nService.Instance.T("file_preview.audio_success", clientIP, filePath, FormatFileSize(fileInfo.Length)));
                    var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                    return File(bytes, GetAudioContentType(fileExtension));
                }
                
                if (isVideo)
                {
                    _logger.LogInfo(I18nService.Instance.T("file_preview.video_success", clientIP, filePath, FormatFileSize(fileInfo.Length)));
                    
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
                    _logger.LogInfo(I18nService.Instance.T("file_preview.text_success", clientIP, filePath, FormatFileSize(fileInfo.Length)));
                    return Ok(new { name = fileName, content = fileContent });
                }
                
                _logger.LogWarning(I18nService.Instance.T("file_preview.unsupported_type", clientIP, filePath));
                return StatusCode(415, I18nService.Instance.T("file_preview.unsupported_type_response"));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("file_preview.error_unauthorized", filePath));
                return StatusCode(403, I18nService.Instance.T("file_preview.access_denied"));
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("file_preview.error_io", filePath));
                return StatusCode(500, I18nService.Instance.T("file_preview.file_read_error"));
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("file_preview.error_memory", filePath));
                return StatusCode(500, I18nService.Instance.T("file_preview.insufficient_memory"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("file_preview.error_unknown", filePath));
                return StatusCode(500, I18nService.Instance.T("file_preview.server_error"));
            }
        }

        [HttpGet("api/download/{**filePath}")]
        public async Task<IActionResult> DownloadFile(string filePath, [FromQuery(Name = "token")] string? token = null)
        {
            var clientIP = _clientIPService.GetClientIP(HttpContext);
            
            _logger.LogInfo(I18nService.Instance.T("file_download.request", clientIP, filePath));
            
            try
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        filePath = Uri.UnescapeDataString(filePath);
                        _logger.LogInfo(I18nService.Instance.T("file_download.url_decode_success", filePath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(I18nService.Instance.T("file_download.url_decode_failed", filePath, ex.Message));
                    }
                }
                
                if (!string.IsNullOrEmpty(filePath) && ContainsIllegalCharacters(filePath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_download.illegal_chars", clientIP, filePath));
                    return BadRequest(I18nService.Instance.T("file_download.path_invalid"));
                }
                
                if (Path.GetFileName(filePath).Equals("data.ini", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_download.permission_config", clientIP, filePath));
                    return NotFound(I18nService.Instance.T("file_download.file_not_found"));
                }
                
                if (PathSecurity.IsSystemPath(filePath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_download.system_path", clientIP, filePath));
                    return NotFound(I18nService.Instance.T("file_download.file_not_found"));
                }
                
                var config = _configManager.GetConfig();
                var repoPath = config.RepositoryPath;
                var fullPath = Path.Combine(repoPath, filePath);

                if (!IsPathValid(repoPath, fullPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_download.outside_repo", clientIP, filePath));
                    return BadRequest(I18nService.Instance.T("file_download.path_invalid"));
                }

                var relPath = GetRelativePath(repoPath, fullPath);
                
                _logger.LogInfo(I18nService.Instance.T("file_download.processing", clientIP, filePath, relPath));
                
                var forbiddenPaths = config.ForbiddenDownloadPaths?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                _logger.LogInfo(I18nService.Instance.T("file_download.forbidden_config", config.ForbiddenDownloadPaths ?? I18nService.Instance.T("file_download.not_configured")));
                
                if (IsDownloadForbidden(relPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_download.forbidden_list", clientIP, relPath));
                    return NotFound(I18nService.Instance.T("file_download.file_not_found"));
                }
                
                _logger.LogInfo(I18nService.Instance.T("file_download.not_in_forbidden", clientIP, relPath));
                if (_blacklistService.IsPathBlacklisted(relPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("file_download.blacklist_blocked", clientIP, relPath));
                    return NotFound(I18nService.Instance.T("file_download.file_not_found"));
                }

                bool fileExists = await Task.Run(() => System.IO.File.Exists(fullPath));
                if (!fileExists)
                {
                    _logger.LogWarning(I18nService.Instance.T("file_download.file_not_exist", clientIP, fullPath));
                    return NotFound(I18nService.Instance.T("file_download.file_not_found"));
                }

                var fileInfo = await Task.Run(() => new System.IO.FileInfo(fullPath));
                var maxDownloadSizeBytes = config.MaxDownloadSizeMB * 1024 * 1024;
                if (fileInfo.Length > maxDownloadSizeBytes)
                {
                    _logger.LogWarning(I18nService.Instance.T("file_download.file_too_large", clientIP, filePath, FormatFileSize(fileInfo.Length), FormatFileSize(maxDownloadSizeBytes)));
                    return StatusCode(413, I18nService.Instance.T("file_download.file_too_large_response", config.MaxDownloadSizeMB));
                }

                var fileExtension = Path.GetExtension(fullPath).ToLowerInvariant();
                var fileName = Path.GetFileName(fullPath);
                
                _logger.LogInfo(I18nService.Instance.T("file_download.success", clientIP, filePath, FormatFileSize(fileInfo.Length)));
                
                Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                Response.Headers["Pragma"] = "no-cache";
                Response.Headers["Expires"] = "0";
                
                var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                return File(bytes, GetContentType(fileExtension), fileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("file_download.error_unauthorized", filePath));
                return StatusCode(403, I18nService.Instance.T("file_download.access_denied"));
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("file_download.error_io", filePath));
                return StatusCode(500, I18nService.Instance.T("file_download.file_read_error"));
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("file_download.error_memory", filePath));
                return StatusCode(500, I18nService.Instance.T("file_download.insufficient_memory"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("file_download.error_unknown", filePath));
                return StatusCode(500, I18nService.Instance.T("file_download.server_error"));
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
