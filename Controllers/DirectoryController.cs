using Microsoft.AspNetCore.Mvc;
using Repository.Models;
using Repository.Services;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Repository.Controllers
{
    public class DirectoryController : Controller
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly BlacklistService _blacklistService;
        private readonly ClientIPService _clientIPService;

        public DirectoryController(ConfigManager configManager, Logger logger, BlacklistService blacklistService, ClientIPService clientIPService)
        {
            _configManager = configManager;
            _logger = logger;
            _blacklistService = blacklistService;
            _clientIPService = clientIPService;
        }

        [HttpGet("api/files")]
        public async Task<IActionResult> GetFiles([FromQuery(Name = "path")] string path = "", [FromQuery(Name = "token")] string? token = null)
        {
            var clientIP = _clientIPService.GetClientIP(HttpContext);
            
            try
            {
                if (Request.Query.ContainsKey("path[]") || Request.Query["path"].Count > 1)
                {
                    _logger.LogWarning(I18nService.Instance.T("directory.path_pollution", clientIP, Request.QueryString));
                    return BadRequest(I18nService.Instance.T("directory.path_invalid"));
                }
                
                if (!string.IsNullOrEmpty(path) && ContainsIllegalCharacters(path))
                {
                    _logger.LogWarning(I18nService.Instance.T("directory.illegal_chars", clientIP, path));
                    return BadRequest(I18nService.Instance.T("directory.path_invalid"));
                }
                
                var repoPath = _configManager.GetConfig().RepositoryPath;
                var fullPath = BuildFullPath(repoPath, path);
                
                if (!IsPathValid(repoPath, fullPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("directory.outside_repo", clientIP, path));
                    return BadRequest(I18nService.Instance.T("directory.path_invalid"));
                }

                if (PathSecurity.IsSystemPath(path))
                {
                    _logger.LogWarning(I18nService.Instance.T("directory.system_path", clientIP, path));
                    return NotFound(I18nService.Instance.T("directory.directory_not_found"));
                }

                bool dirExists = await Task.Run(() => Directory.Exists(fullPath));
                if (!dirExists)
                {
                    _logger.LogWarning(I18nService.Instance.T("directory.not_exist", clientIP, fullPath));
                    return NotFound(I18nService.Instance.T("directory.directory_not_found"));
                }

                var listing = new DirectoryListing { CurrentPath = path };
                
                var config = _configManager.GetConfig();
                
                await Task.WhenAll(
                    ProcessDirectoriesAsync(repoPath, fullPath, path, listing, config),
                    ProcessFilesAsync(repoPath, fullPath, path, listing, config)
                );

                listing.DirectoryHash = await Task.Run(() => ComputeDirectoryHash(fullPath));

                _logger.LogInfo(I18nService.Instance.T("directory.success", clientIP, path));
                return Ok(listing);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("directory.error_unauthorized", path));
                return StatusCode(403, I18nService.Instance.T("directory.access_denied"));
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("directory.error_io", path));
                return StatusCode(500, I18nService.Instance.T("directory.file_system_error"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("directory.error_unknown", path));
                return StatusCode(500, I18nService.Instance.T("directory.server_error"));
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
                
                if (_blacklistService.IsPathBlacklisted(relPath))
                {
                    continue;
                }
                
                if (IsPathHidden(relPath, hiddenPaths))
                {
                    continue;
                }
                
                
                var fileExtension = Path.GetExtension(file).ToLower();
                var isPreviewable = allPreviewableExtensions.Contains(fileExtension);
                
                var relativeUrl = GetRelativeUrl(relativePath, fileName, false);
                var fileInfo = await Task.Run(() => new System.IO.FileInfo(file));
                var fileSha256 = await Task.Run(() => ComputeFileSha256(file));
                
                lock (listing.Files)
                {
                    listing.Files.Add(new Models.FileInfo
                    {
                        Name = fileName,
                        Url = relativeUrl,
                        LastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Size = fileInfo.Length,
                        Previewable = isPreviewable,
                        Sha256 = fileSha256,
                        CanView = true,
                        CanDownload = true,
                        CanPreview = isPreviewable
                    });
                }
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

        private static string ComputeFileSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = System.IO.File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLower();
        }

        private static string ComputeDirectoryHash(string directoryPath)
        {
            using var sha256 = SHA256.Create();
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            try
            {
                var dirs = Directory.GetDirectories(directoryPath)
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    writer.Write(Encoding.UTF8.GetBytes(name));
                    writer.Write(System.IO.File.GetLastWriteTimeUtc(dir).Ticks);
                }

                var files = Directory.GetFiles(directoryPath)
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    writer.Write(Encoding.UTF8.GetBytes(name));
                    writer.Write(new System.IO.FileInfo(file).Length);
                    writer.Write(System.IO.File.GetLastWriteTimeUtc(file).Ticks);
                }
            }
            catch
            {
            }

            var hash = sha256.ComputeHash(ms.ToArray());
            return Convert.ToHexString(hash).ToLower();
        }
    }
}
