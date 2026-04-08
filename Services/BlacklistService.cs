using Repository.Models;
using Repository.Services;
using System.Text.RegularExpressions;

namespace Repository.Services
{
    public class BlacklistService
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;

        public BlacklistService(ConfigManager configManager, Logger logger)
        {
            _configManager = configManager;
            _logger = logger;
        }

        public bool IsPathBlacklisted(string relativePath)
        {
            var config = _configManager.GetConfig();
            
            // 如果黑名单为空，允许所有路径访问
            if (string.IsNullOrWhiteSpace(config.Blacklist))
            {
                return false;
            }

            // 解析黑名单
            var blacklistedPaths = ParseBlacklist(config.Blacklist);
            
            // 检查路径是否在黑名单中
            foreach (var blacklistedPath in blacklistedPaths)
            {
                if (IsPathMatch(relativePath, blacklistedPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("blacklist.path_blocked", relativePath, blacklistedPath));
                    return true;
                }
            }

            return false;
        }

        public List<string> GetBlacklistedPaths()
        {
            var config = _configManager.GetConfig();
            return ParseBlacklist(config.Blacklist);
        }

        private List<string> ParseBlacklist(string blacklist)
        {
            var blacklistedPaths = new List<string>();
            
            if (string.IsNullOrWhiteSpace(blacklist))
            {
                return blacklistedPaths;
            }

            // 检查黑名单是否指向一个文件
            if (File.Exists(blacklist))
            {
                try
                {
                    var fileContent = File.ReadAllText(blacklist);
                    if (!string.IsNullOrWhiteSpace(fileContent))
                    {
                        // 递归解析文件内容
                        var fileEntries = ParseBlacklist(fileContent);
                        blacklistedPaths.AddRange(fileEntries);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(I18nService.Instance.T("blacklist.file_read_error", blacklist, ex.Message));
                }
                return blacklistedPaths;
            }

            // 使用竖线 | 作为分隔符，避免与Windows目录中可能包含的逗号冲突
            var entries = blacklist.Split('|', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var entry in entries)
            {
                var trimmedEntry = entry.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedEntry))
                {
                    // 处理 {} 包裹的文件或通配符表达式
                    if (trimmedEntry.StartsWith("{") && trimmedEntry.EndsWith("}") && trimmedEntry.Length > 2)
                    {
                        var innerContent = trimmedEntry.Substring(1, trimmedEntry.Length - 2).Trim();
                        if (!string.IsNullOrWhiteSpace(innerContent))
                        {
                            // 保留原始格式用于特殊匹配
                            blacklistedPaths.Add(innerContent);
                        }
                    }
                    // 处理 %/xxx 格式：代表子目录中的xxx目录
                    else if (trimmedEntry.StartsWith("%/") && trimmedEntry.Length > 2)
                    {
                        var folderName = trimmedEntry.Substring(2); // 移除 %/ 前缀
                        if (!string.IsNullOrWhiteSpace(folderName))
                        {
                            // 保留原始格式用于特殊匹配
                            blacklistedPaths.Add(trimmedEntry);
                        }
                    }
                    else
                    {
                        // 确保路径格式正确（移除开头和结尾的斜杠）
                        var normalizedPath = trimmedEntry.Trim('/').Trim('\\');
                        blacklistedPaths.Add(normalizedPath);
                    }
                }
            }

            return blacklistedPaths;
        }

        private bool IsPathMatch(string path, string blacklistedPath)
        {
            // 处理 %/xxx 格式的特殊匹配
            if (blacklistedPath.StartsWith("%/") && blacklistedPath.Length > 2)
            {
                var folderName = blacklistedPath.Substring(2); // 移除 %/ 前缀
                
                // 检查路径中是否包含该文件夹名（在任何子目录中）
                // 例如：%/src 会匹配 "project/src"、"project/subfolder/src"、"src" 等
                var pathSegments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var segment in pathSegments)
                {
                    if (string.Equals(segment, folderName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            
            // 处理通配符表达式（如 /*.cs）
            if (blacklistedPath.Contains("*"))
            {
                // 将通配符模式转换为正则表达式
                var pattern = "^" + Regex.Escape(blacklistedPath).Replace("\\*", ".*") + "$";
                try
                {
                    return Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase);
                }
                catch (Exception ex)
                {
                    _logger.LogError(I18nService.Instance.T("blacklist.wildcard_error", blacklistedPath, pattern, ex.Message));
                    return false;
                }
            }
            
            // 标准化路径（移除开头和结尾的斜杠）
            var normalizedPath = path.Trim('/').Trim('\\');
            var normalizedBlacklist = blacklistedPath.Trim('/').Trim('\\');

            // 精确匹配（区分大小写）
            if (string.Equals(normalizedPath, normalizedBlacklist, StringComparison.Ordinal))
            {
                return true;
            }

            // 包含匹配：如果访问的路径包含黑名单路径（作为前缀）
            // 例如：黑名单路径是 "qwq/src/xxx"，访问路径是 "qwq/src/xxx/subfolder" 或 "qwq/src/xxx/file.txt"
            if (normalizedPath.StartsWith(normalizedBlacklist + "/") || 
                normalizedPath.StartsWith(normalizedBlacklist + "\\"))
            {
                return true;
            }

            return false;
        }

        public void UpdateBlacklist(string blacklist)
        {
            var config = _configManager.GetConfig();
            config.Blacklist = blacklist;
            _configManager.SaveConfig(config);
            
            _logger.LogInfo(I18nService.Instance.T("blacklist.updated", blacklist));
        }

        public List<string> FilterBlacklistedPaths(List<string> paths)
        {
            var filteredPaths = new List<string>();
            
            foreach (var path in paths)
            {
                if (!IsPathBlacklisted(path))
                {
                    filteredPaths.Add(path);
                }
            }

            return filteredPaths;
        }
    }
}