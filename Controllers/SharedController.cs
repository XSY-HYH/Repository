using Microsoft.AspNetCore.Mvc;
using Repository.Models;
using Repository.Services;
using System.IO;

namespace Repository.Controllers
{
    /// <summary>
    /// 处理共享功能（如favicon、日志等）的控制器
    /// </summary>
    [Route("")]
    public class SharedController : Controller
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;

        public SharedController(ConfigManager configManager, Logger logger)
        {
            _configManager = configManager;
            _logger = logger;
        }

        /// <summary>
        /// 获取网站图标（支持 /api/log 路径）
        /// </summary>
        [HttpGet("favicon.ico")]
        [HttpGet("api/log")]
        public IActionResult GetFavicon()
        {
            try
            {
                // 只检查 EXE 根目录下的 favicon.ico（这是程序运行的目录）
                string exeRootPath = AppContext.BaseDirectory;
                string exeRootFaviconPath = Path.Combine(exeRootPath, "favicon.ico");
                
                if (System.IO.File.Exists(exeRootFaviconPath))
                {
                    _logger.LogInfo($"返回 EXE 根目录下的 favicon.ico: {exeRootFaviconPath}");
                    return File(System.IO.File.ReadAllBytes(exeRootFaviconPath), "image/x-icon");
                }
                
                _logger.LogWarning("未找到 favicon.ico 文件，将返回 404");
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 favicon 时发生错误");
                return NotFound();
            }
        }



        /// <summary>
        /// 获取系统信息
        /// </summary>
        [HttpGet("api/system/info")]
        public IActionResult GetSystemInfo()
        {
            // 获取客户端IP地址
            var clientIP = GetClientIP();
            
            // 记录系统信息请求开始
            _logger.LogInfo($"开始处理系统信息请求，客户端IP: {clientIP}");
            
            try
            {
                var config = _configManager.GetConfig();
                
                // 检查系统信息访问功能是否启用
                 // 检查系统信息访问功能是否启用
                 // 由于Config类中没有SystemInfoEnabled属性，我们使用PreviewEnabled作为替代
                 if (!config.PreviewEnabled)
                 {
                     _logger.LogWarning($"系统信息访问功能已禁用，客户端IP: {clientIP}");
                     return StatusCode(403, "系统信息访问功能已禁用");
                 }
                 
                 // 获取系统信息
                 var systemInfo = new
                 {
                     version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "Unknown",
                     environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                     os = Environment.OSVersion.ToString(),
                     machineName = Environment.MachineName,
                     processorCount = Environment.ProcessorCount,
                     workingDirectory = Directory.GetCurrentDirectory(),
                     repositoryPath = config.RepositoryPath,
                     previewEnabled = config.PreviewEnabled,
                     uploadEnabled = config.UploadEnabled,
                     logAccessEnabled = config.PreviewEnabled, // 使用PreviewEnabled替代LogAccessEnabled
                     systemInfoEnabled = config.PreviewEnabled, // 使用PreviewEnabled替代SystemInfoEnabled
                     maxUploadFileSize = FormatFileSize(config.MaxUploadSizeMB * 1024 * 1024), // 使用MaxUploadSizeMB替代MaxUploadFileSize
                     serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                 };
                
                _logger.LogInfo($"系统信息获取成功，客户端IP: {clientIP}");
                
                // 返回系统信息
                return Ok(systemInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取系统信息时发生未知错误");
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
        /// 检查路径是否合法（防止目录遍历攻击）
        /// </summary>
        private bool IsPathValid(string basePath, string fullPath)
        {
            try
            {
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
        private bool ContainsIllegalCharacters(string path)
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