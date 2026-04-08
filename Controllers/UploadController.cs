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
        private readonly ProtectionService _protectionService;

        public UploadController(ConfigManager configManager, Logger logger, BlacklistService blacklistService, ProtectionService protectionService)
        {
            _configManager = configManager;
            _logger = logger;
            _blacklistService = blacklistService;
            _protectionService = protectionService;
        }

        [HttpPost("api/upload/{**folderPath}")]
        public async Task<IActionResult> UploadFile(string? folderPath, IFormFile? file, [FromQuery(Name = "token")] string? token = null)
        {
            var clientIP = GetClientIP();
            
            _logger.LogInfo($"开始处理文件上传请求，客户端IP: {clientIP}，目标文件夹: {folderPath}，文件名: {file?.FileName}");
            
            try
            {
                if (file == null || file.Length == 0)
                {
                    _logger.LogWarning($"上传文件为空，客户端IP: {clientIP}");
                    return BadRequest("请选择要上传的文件");
                }
                
                if (!string.IsNullOrEmpty(folderPath))
                {
                    try
                    {
                        folderPath = Uri.UnescapeDataString(folderPath);
                        _logger.LogInfo($"URL解码后文件夹路径: {folderPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"URL解码失败，使用原始路径: {folderPath}，错误: {ex.Message}");
                    }
                }
                
                if (!string.IsNullOrEmpty(folderPath) && ContainsIllegalCharacters(folderPath))
                {
                    _logger.LogWarning($"检测到非法字符的文件上传请求，客户端IP: {clientIP}，文件夹路径: {folderPath}");
                    return BadRequest("文件夹路径不合法");
                }
                
                if (ContainsIllegalCharacters(file.FileName))
                {
                    _logger.LogWarning($"检测到非法字符的文件名，客户端IP: {clientIP}，文件名: {file.FileName}");
                    return BadRequest("文件名不合法");
                }
                
                var config = _configManager.GetConfig();
                
                if (!config.UploadEnabled)
                {
                    _logger.LogWarning($"文件上传功能已禁用，客户端IP: {clientIP}");
                    return StatusCode(403, "上传功能已禁用");
                }
                
                if (!config.AllowRootUpload && (string.IsNullOrEmpty(folderPath) || folderPath.Trim('/') == ""))
                {
                    _logger.LogWarning($"尝试上传到根目录，客户端IP: {clientIP}");
                    return BadRequest("不允许上传到根目录");
                }
                
                var repoPath = config.RepositoryPath;
                var targetFolder = BuildFullPath(repoPath, folderPath);

                if (!IsPathValid(repoPath, targetFolder))
                {
                    _logger.LogWarning($"安全警告！客户端IP: {clientIP}，尝试上传到仓库外的文件夹: {folderPath}");
                    return BadRequest("文件夹路径不合法");
                }
                
                if (ProtectionService.IsSystemPath(folderPath))
                {
                    _logger.LogWarning($"尝试上传到系统路径，客户端IP: {clientIP}，路径: {folderPath}");
                    return NotFound("目录不存在");
                }
                
                var relPath = GetRelativePath(repoPath, targetFolder);
                
                if (_protectionService.IsPathProtected(relPath) && !_protectionService.VerifyToken(relPath, token))
                {
                    _logger.LogWarning($"受保护目录上传被拒绝，客户端IP: {clientIP}，路径: {relPath}");
                    return NotFound("目录不存在");
                }
                
                if (!Directory.Exists(targetFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(targetFolder);
                        _logger.LogInfo($"创建目标文件夹: {targetFolder}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"无法创建目标文件夹: {targetFolder}");
                        return StatusCode(500, "无法创建目标文件夹");
                    }
                }
                
                var filePath = Path.Combine(targetFolder, file.FileName);
                
                var fileRelPath = GetRelativePath(repoPath, filePath);
                
                if (_blacklistService.IsPathBlacklisted(fileRelPath))
                {
                    _logger.LogWarning($"文件上传被黑名单阻止，客户端IP: {clientIP}，文件路径: {fileRelPath}");
                    return StatusCode(403, "文件路径不被允许");
                }
                
                // 检查文件大小是否超过限制
                var maxUploadSizeBytes = config.MaxUploadSizeMB * 1024 * 1024;
                if (file.Length > maxUploadSizeBytes)
                {
                    _logger.LogWarning($"文件过大，拒绝上传，客户端IP: {clientIP}，文件名: {file.FileName} ({FormatFileSize(file.Length)})，限制: {FormatFileSize(maxUploadSizeBytes)}");
                    return StatusCode(413, $"文件过大，最大允许 {FormatFileSize(maxUploadSizeBytes)}");
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
                        _logger.LogWarning($"磁盘占用超限，拒绝上传，客户端IP: {clientIP}，文件名: {file.FileName}，当前占用: {currentUsagePercent:F1}%，上传后预计: {projectedUsagePercent:F1}%，限制: {config.MaxDiskUsagePercent}%");
                        return StatusCode(507, $"磁盘占用超限，当前占用 {currentUsagePercent:F1}%，限制 {config.MaxDiskUsagePercent}%");
                    }
                }
                
                // 检查文件扩展名是否被允许
                var allowedExtensions = config.AllowedUploadExtensions?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                if (allowedExtensions.Length > 0)
                {
                    var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        _logger.LogWarning($"文件类型不被允许，客户端IP: {clientIP}，文件名: {file.FileName}，扩展名: {fileExtension}");
                        return StatusCode(403, $"不支持的文件类型: {fileExtension}");
                    }
                }
                
                // 检查文件是否已存在
                if (System.IO.File.Exists(filePath))
                {
                    // 如果配置不允许覆盖，返回错误
                    if (!config.AllowOverwrite)
                    {
                        _logger.LogWarning($"文件已存在且不允许覆盖，客户端IP: {clientIP}，文件路径: {filePath}");
                        return StatusCode(409, "文件已存在");
                    }
                    
                    // 如果允许覆盖，先删除现有文件
                    try
                    {
                        System.IO.File.Delete(filePath);
                        _logger.LogInfo($"删除现有文件: {filePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"无法删除现有文件: {filePath}");
                        return StatusCode(500, "无法覆盖现有文件");
                    }
                }
                
                // 保存文件
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                
                _logger.LogInfo($"文件上传成功，客户端IP: {clientIP}，文件路径: {filePath} ({FormatFileSize(file.Length)})");
                
                // 返回成功信息
                return Ok(new { 
                    message = "文件上传成功",
                    fileName = file.FileName,
                    filePath = relPath,
                    fileSize = FormatFileSize(file.Length)
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, $"权限不足，无法上传文件到: {folderPath}");
                return StatusCode(403, "没有访问权限");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, $"文件写入错误: {folderPath}");
                return StatusCode(500, "文件写入失败");
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, $"内存不足，无法处理大文件: {file?.FileName}");
                return StatusCode(500, "服务器内存不足");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"上传文件时发生未知错误: {file?.FileName}");
                return StatusCode(500, "服务器内部错误");
            }
        }

        /// <summary>
        /// 获取客户端IP地址
        /// </summary>
        private string GetClientIP()
        {
            try
            {
                var httpContext = HttpContext;
                
                // 首先检查X-Forwarded-For头部（代理服务器）
                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(forwardedFor))
                {
                    // X-Forwarded-For可能包含多个IP，取第一个
                    var ips = forwardedFor.Split(',').Select(ip => ip.Trim());
                    return ips.FirstOrDefault() ?? "Unknown";
                }
                
                // 检查X-Real-IP头部
                var realIP = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(realIP))
                {
                    return realIP;
                }
                
                // 最后使用RemoteIpAddress
                return httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
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