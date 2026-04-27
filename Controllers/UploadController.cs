using Microsoft.AspNetCore.Mvc;
using Repository.Models;
using Repository.Services;
using System.IO;

namespace Repository.Controllers
{
    public class UploadController : Controller
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly BlacklistService _blacklistService;
        private readonly ClientIPService _clientIPService;

        public UploadController(ConfigManager configManager, Logger logger, BlacklistService blacklistService, ClientIPService clientIPService)
        {
            _configManager = configManager;
            _logger = logger;
            _blacklistService = blacklistService;
            _clientIPService = clientIPService;
        }

        [HttpPost("api/upload/{**folderPath}")]
        public async Task<IActionResult> UploadFile(string? folderPath, IFormFile? file, [FromQuery(Name = "token")] string? token = null)
        {
            var clientIP = _clientIPService.GetClientIP(HttpContext);
            
            _logger.LogInfo(I18nService.Instance.T("upload.request", clientIP, folderPath, file?.FileName));
            
            try
            {
                if (file == null || file.Length == 0)
                {
                    _logger.LogWarning(I18nService.Instance.T("upload.file_empty", clientIP));
                    return BadRequest(I18nService.Instance.T("upload.select_file_prompt"));
                }
                
                if (!string.IsNullOrEmpty(folderPath))
                {
                    try
                    {
                        folderPath = Uri.UnescapeDataString(folderPath);
                        _logger.LogInfo(I18nService.Instance.T("upload.url_decode_success", folderPath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(I18nService.Instance.T("upload.url_decode_failed", folderPath, ex.Message));
                    }
                }
                
                if (!string.IsNullOrEmpty(folderPath) && ContainsIllegalCharacters(folderPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("upload.illegal_chars_path", clientIP, folderPath));
                    return BadRequest(I18nService.Instance.T("upload.path_invalid"));
                }
                
                if (ContainsIllegalCharacters(file.FileName))
                {
                    _logger.LogWarning(I18nService.Instance.T("upload.illegal_chars_filename", clientIP, file.FileName));
                    return BadRequest(I18nService.Instance.T("upload.filename_invalid"));
                }
                
                var config = _configManager.GetConfig();
                
                if (!config.UploadEnabled)
                {
                    _logger.LogWarning(I18nService.Instance.T("upload.feature_disabled", clientIP));
                    return StatusCode(403, I18nService.Instance.T("upload.feature_disabled_response"));
                }
                
                if (!config.AllowRootUpload && (string.IsNullOrEmpty(folderPath) || folderPath.Trim('/') == ""))
                {
                    _logger.LogWarning(I18nService.Instance.T("upload.root_upload_denied", clientIP));
                    return BadRequest(I18nService.Instance.T("upload.root_upload_denied_response"));
                }
                
                var repoPath = config.RepositoryPath;
                var targetFolder = BuildFullPath(repoPath, folderPath);

                if (!IsPathValid(repoPath, targetFolder))
                {
                    _logger.LogWarning(I18nService.Instance.T("upload.outside_repo", clientIP, folderPath));
                    return BadRequest(I18nService.Instance.T("upload.path_invalid"));
                }
                
                if (PathSecurity.IsSystemPath(folderPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("upload.system_path", clientIP, folderPath));
                    return NotFound(I18nService.Instance.T("upload.folder_not_found"));
                }
                
                var relPath = GetRelativePath(repoPath, targetFolder);
                
                if (!Directory.Exists(targetFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(targetFolder);
                        _logger.LogInfo(I18nService.Instance.T("upload.create_folder", targetFolder));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, I18nService.Instance.T("upload.folder_create_error", targetFolder));
                        return StatusCode(500, I18nService.Instance.T("upload.folder_create_failed"));
                    }
                }
                
                var filePath = Path.Combine(targetFolder, file.FileName);
                
                var fileRelPath = GetRelativePath(repoPath, filePath);
                
                if (_blacklistService.IsPathBlacklisted(fileRelPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("upload.blacklist_blocked", clientIP, fileRelPath));
                    return StatusCode(403, I18nService.Instance.T("upload.access_denied"));
                }
                
                // 检查文件大小是否超过限制
                var maxUploadSizeBytes = config.MaxUploadSizeMB * 1024 * 1024;
                if (file.Length > maxUploadSizeBytes)
                {
                    _logger.LogWarning(I18nService.Instance.T("upload.file_too_large", clientIP, file.FileName, FormatFileSize(file.Length), FormatFileSize(maxUploadSizeBytes)));
                    return StatusCode(413, I18nService.Instance.T("upload.file_too_large", clientIP, file.FileName, FormatFileSize(file.Length), FormatFileSize(maxUploadSizeBytes)));
                }
                
                // 检查磁盘占用限制
                if (config.MaxDiskUsagePercent > 0)
                {
                    var driveInfo = new DriveInfo(repoPath);
                    var totalBytes = driveInfo.TotalSize;
                    var usedBytes = totalBytes - driveInfo.AvailableFreeSpace;
                    var currentUsagePercent = (double)usedBytes / totalBytes * 100;
                    var projectedUsedBytes = usedBytes + file.Length;
                    var projectedUsagePercent = (double)projectedUsedBytes / totalBytes * 100;
                    
                    if (projectedUsagePercent > config.MaxDiskUsagePercent)
                    {
                        _logger.LogWarning(I18nService.Instance.T("upload.disk_full", clientIP, file.FileName, currentUsagePercent, projectedUsagePercent, config.MaxDiskUsagePercent));
                        return StatusCode(507, I18nService.Instance.T("upload.disk_full", clientIP, file.FileName, currentUsagePercent, projectedUsagePercent, config.MaxDiskUsagePercent));
                    }
                }
                
                // 检查文件扩展名是否被允许
                var allowedExtensions = config.AllowedUploadExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                if (allowedExtensions.Length > 0)
                {
                    var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        _logger.LogWarning(I18nService.Instance.T("upload.file_type_denied", clientIP, file.FileName, fileExtension));
                        return StatusCode(403, I18nService.Instance.T("upload.file_type_denied", clientIP, file.FileName, fileExtension));
                    }
                }
                
                // 检查文件是否已存在
                if (System.IO.File.Exists(filePath))
                {
                    // 如果配置不允许覆盖，返回错误
                    if (!config.AllowOverwrite)
                    {
                        _logger.LogWarning(I18nService.Instance.T("upload.file_exists", clientIP, filePath));
                        return StatusCode(409, I18nService.Instance.T("upload.file_exists", clientIP, filePath));
                    }
                    
                    // 如果允许覆盖，先删除现有文件
                    try
                    {
                        System.IO.File.Delete(filePath);
                        _logger.LogInfo(I18nService.Instance.T("upload.delete_existing", filePath));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, I18nService.Instance.T("upload.file_delete_error", filePath));
                        return StatusCode(500, I18nService.Instance.T("upload.file_delete_error", filePath));
                    }
                }
                
                // 保存文件
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                
                _logger.LogInfo(I18nService.Instance.T("upload.success", clientIP, filePath, FormatFileSize(file.Length)));
                
                // 返回成功信息
                return Ok(new { 
                    message = I18nService.Instance.T("upload.upload_success_message"),
                    fileName = file.FileName,
                    filePath = relPath,
                    fileSize = FormatFileSize(file.Length)
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("upload.error_unauthorized", folderPath));
                return StatusCode(403, I18nService.Instance.T("upload.access_denied"));
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("upload.error_io", folderPath));
                return StatusCode(500, I18nService.Instance.T("upload.server_error"));
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("upload.error_memory", file?.FileName));
                return StatusCode(500, I18nService.Instance.T("upload.error_memory", file?.FileName));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("upload.error_unknown", file?.FileName));
                return StatusCode(500, I18nService.Instance.T("upload.server_error"));
            }
        }

        /// <summary>
        /// 构建完整路径
        /// </summary>
        private string BuildFullPath(string repoPath, string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return repoPath;
            }
            
            // 规范化路径分隔符
            path = path.Replace('/', Path.DirectorySeparatorChar);
            
            // 移除开头的分隔符
            if (path.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                path = path.Substring(1);
            }
            
            return Path.Combine(repoPath, path);
        }

        /// <summary>
        /// 获取相对路径
        /// </summary>
        private string GetRelativePath(string basePath, string? fullPath)
        {
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(fullPath))
            {
                return fullPath ?? string.Empty;
            }
            
            // 确保路径使用相同的分隔符
            basePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar);
            fullPath = Path.GetFullPath(fullPath);
            
            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = fullPath.Substring(basePath.Length);
                
                // 确保相对路径以分隔符开头
                if (!relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    relativePath = Path.DirectorySeparatorChar + relativePath;
                }
                
                // 规范化分隔符为 '/'
                return relativePath.Replace(Path.DirectorySeparatorChar, '/');
            }
            
            return fullPath;
        }

        /// <summary>
        /// 检查路径是否合法（防止目录遍历攻击）
        /// </summary>
        private bool IsPathValid(string basePath, string? fullPath)
        {
            try
            {
                if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(fullPath))
                {
                    return false;
                }
                
                // 获取绝对路径
                var baseDir = Path.GetFullPath(basePath);
                var fullDir = Path.GetFullPath(fullPath);
                
                // 检查完整路径是否在基础路径下
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
        private bool ContainsIllegalCharacters(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            
            // 检查常见的路径遍历攻击模式
            var illegalPatterns = new[]
            {
                "..", // 父目录引用
                "~", // 用户目录
                "$", // 环境变量
                "&", // 命令连接符
                "|", // 管道符
                ";", // 命令分隔符
                "<", // 重定向符
                ">", // 重定向符
                "\"", // 引号
                "'", // 单引号
                "`", // 反引号
                "\n", // 换行符
                "\r", // 回车符
                "\t"  // 制表符
            };
            
            foreach (var pattern in illegalPatterns)
            {
                if (path.Contains(pattern))
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}