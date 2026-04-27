using Microsoft.AspNetCore.Mvc;
using Repository.Models;
using Repository.Services;
using System.IO;

namespace Repository.Controllers
{
    /// <summary>
    /// 主控制器，处理核心路由逻辑
    /// </summary>
    public class RepositoryController : Controller
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly BlacklistService _blacklistService;
        private readonly ClientIPService _clientIPService;

        public RepositoryController(ConfigManager configManager, Logger logger, BlacklistService blacklistService, ClientIPService clientIPService)
        {
            _configManager = configManager;
            _logger = logger;
            _blacklistService = blacklistService;
            _clientIPService = clientIPService;
        }

        /// <summary>
        /// 主页路由，返回目录浏览页面
        /// </summary>
        [HttpGet]
        public IActionResult Index([FromQuery(Name = "path")] string path = "")
        {
            var clientIP = _clientIPService.GetClientIP(HttpContext);
            
            // 添加分隔线，开始新的请求
            _logger.LogSeparator();
            
            // 记录请求开始
            _logger.LogInfo(I18nService.Instance.T("repository.processing_request", clientIP, path));
            
            try
            {
                var repoPath = _configManager.GetConfig().RepositoryPath;
                var fullPath = BuildFullPath(repoPath, path);
                
                // 先做路径安全检查
                if (!IsPathValid(repoPath, fullPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("repository.outside_repo_log", clientIP, path));
                    return NotFound(I18nService.Instance.T("repository.path_invalid"));
                }
                
                // 检查目录是否存在
                if (!Directory.Exists(fullPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("repository.directory_not_exist_log", fullPath));
                    return NotFound(I18nService.Instance.T("repository.directory_not_exist"));
                }
                
                // 检查路径是否在黑名单中
                var relPath = GetRelativePath(repoPath, fullPath);
                if (_blacklistService.IsPathBlacklisted(relPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("repository.blacklist_blocked", relPath));
                    return NotFound(I18nService.Instance.T("repository.directory_not_exist")); // 为了安全，不暴露黑名单信息
                }
                
                // 检查是否为系统路径
                if (PathSecurity.IsSystemPath(path))
                {
                    _logger.LogWarning(I18nService.Instance.T("repository.system_path_log", clientIP, path));
                    return NotFound(I18nService.Instance.T("repository.directory_not_exist"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("repository.error_processing", path));
                return StatusCode(500, I18nService.Instance.T("repository.server_error"));
            }
            
            var config = _configManager.GetConfig();
            
            var html = DirectoryListingHtml.GetHtml();
            
            var css = DirectoryListingCss.GetCss();
            var js = DirectoryListingJs.GetCommonJs();
            
            var displayPath = string.IsNullOrEmpty(path) ? "/" : path;
            html = html.Replace("{{currentPath}}", displayPath);
            
            var i18n = I18nService.Instance;
            var webTranslations = i18n.GetWebTranslationsJson();
            
            html = html.Replace("{{lang}}", i18n.CurrentLanguage);
            html = html.Replace("{{title}}", i18n.T("web.title"));
            html = html.Replace("{{directory_for}}", i18n.T("web.directory_for"));
            html = html.Replace("{{server}}", i18n.T("web.server"));
            html = html.Replace("{{current_time}}", i18n.T("web.current_time"));
            html = html.Replace("{{name}}", i18n.T("web.name"));
            html = html.Replace("{{last_modified}}", i18n.T("web.last_modified"));
            html = html.Replace("{{size}}", i18n.T("web.size"));
            html = html.Replace("{{loading}}", i18n.T("web.loading"));
            html = html.Replace("{{preview_file}}", i18n.T("web.preview_file"));
            html = html.Replace("{{loading_content}}", i18n.T("web.loading_content"));
            html = html.Replace("{{close}}", i18n.T("web.close"));
            html = html.Replace("{{i18n}}", webTranslations);
            
            html = html.Replace("/* CSS will be injected here */", css);
            html = html.Replace("/* JavaScript will be injected here */", js);
              
             _logger.LogInfo(I18nService.Instance.T("repository.html_generated", html.Length, displayPath));
             _logger.LogInfo(I18nService.Instance.T("repository.css_js_length", css.Length, js.Length));
             
             _logger.LogInfo(I18nService.Instance.T("repository.success", clientIP, path));
             
             // 请求处理完成，添加分隔线
             _logger.LogSeparator();
             
             return Content(html, "text/html");
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
        private string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(fullPath))
            {
                return fullPath;
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
        /// 检查文件是否在禁止预览列表中
        /// </summary>
        private bool IsPreviewForbidden(string relativePath)
        {
            var config = _configManager.GetConfig();
            var forbiddenPaths = config.ForbiddenPreviewPaths?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            
            foreach (var forbiddenPath in forbiddenPaths)
            {
                var trimmedPath = forbiddenPath.Trim();
                
                // 检查是否完全匹配
                if (relativePath.Equals(trimmedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // 检查是否是目录下的文件
                if (trimmedPath.EndsWith("/"))
                {
                    if (relativePath.StartsWith(trimmedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                // 检查是否是目录本身
                else if (relativePath.Equals(trimmedPath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 检查文件是否在禁止下载列表中
        /// </summary>
        private bool IsDownloadForbidden(string relativePath)
        {
            var config = _configManager.GetConfig();
            var forbiddenPaths = config.ForbiddenDownloadPaths?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            
            foreach (var forbiddenPath in forbiddenPaths)
            {
                var trimmedPath = forbiddenPath.Trim();
                
                // 检查是否完全匹配
                if (relativePath.Equals(trimmedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // 检查是否是目录下的文件
                if (trimmedPath.EndsWith("/"))
                {
                    if (relativePath.StartsWith(trimmedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                // 检查是否是目录本身
                else if (relativePath.Equals(trimmedPath + "/", StringComparison.OrdinalIgnoreCase))
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