using System.IO;
using Microsoft.Extensions.FileProviders;
using Repository.Models;

namespace Repository.Services
{
    public class NetworkFileService : IDisposable
    {
        private readonly Logger _logger;
        private IFileProvider? _fileProvider;
        private string _currentPath;
        private bool _isNetworkPath;

        public NetworkFileService(Logger logger)
        {
            _logger = logger;
            _currentPath = string.Empty;
            _isNetworkPath = false;
        }

        /// <summary>
        /// 初始化文件服务
        /// </summary>
        /// <param name="repositoryPath">仓库路径，可以是本地路径或网络路径</param>
        public void Initialize(string repositoryPath)
        {
            try
            {
                _isNetworkPath = IsNetworkPath(repositoryPath);
                _currentPath = repositoryPath;

                if (_isNetworkPath)
                {
                    var fullPath = GetFullPath(repositoryPath);
                    _fileProvider = new PhysicalFileProvider(fullPath);
                    _logger.LogInfo(I18nService.Instance.T("network.network_initialized", repositoryPath, fullPath));
                }
                else
                {
                    var fullPath = Path.GetFullPath(repositoryPath);
                    
                    if (!Directory.Exists(fullPath))
                    {
                        Directory.CreateDirectory(fullPath);
                        _logger.LogInfo(I18nService.Instance.T("network.directory_created", fullPath));
                    }
                    
                    _fileProvider = new PhysicalFileProvider(fullPath);
                    _logger.LogInfo(I18nService.Instance.T("network.local_initialized", fullPath));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("network.init_failed", repositoryPath));
                throw;
            }
        }

        /// <summary>
        /// 检查是否为网络路径
        /// </summary>
        public bool IsNetworkPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // 检查UNC路径格式（如\\server\share）
            if (path.StartsWith("\\\\") || path.StartsWith("//"))
                return true;

            // 检查网络驱动器格式（如Z:\，其中Z是网络驱动器）
            if (path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0]))
            {
                try
                {
                    var driveInfo = new DriveInfo(path.Substring(0, 2));
                    return driveInfo.DriveType == DriveType.Network;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// 获取文件的完整路径
        /// </summary>
        public string GetFullPath(string path)
        {
            if (IsNetworkPath(path))
            {
                // 对于网络路径，直接使用（Windows会自动处理UNC路径）
                // 将//格式转换为\\格式
                if (path.StartsWith("//"))
                {
                    path = path.Replace("//", "\\\\");
                }
                return path;
            }
            else
            {
                // 对于本地路径，获取绝对路径
                return Path.GetFullPath(path);
            }
        }

        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        public bool FileExists(string relativePath)
        {
            try
            {
                if (_fileProvider == null)
                    return false;

                var fileInfo = _fileProvider.GetFileInfo(relativePath);
                return fileInfo.Exists && !fileInfo.IsDirectory;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("network.file_exists_error", relativePath));
                return false;
            }
        }

        /// <summary>
        /// 获取文件信息
        /// </summary>
        public IFileInfo? GetFileInfo(string relativePath)
        {
            try
            {
                // 首先尝试使用PhysicalFileProvider
                var fileInfo = _fileProvider?.GetFileInfo(relativePath);
                
                // 如果PhysicalPath为空但文件应该存在，尝试直接使用文件系统API
                if (fileInfo != null && string.IsNullOrEmpty(fileInfo.PhysicalPath) && !fileInfo.Exists)
                {
                    // 统一路径分隔符为系统默认分隔符
                    var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    
                    // 构建完整路径
                    var fullPath = Path.Combine(_currentPath, normalizedRelativePath);
                    
                    // 检查文件是否存在
                    if (File.Exists(fullPath))
                    {
                        _logger.LogInfo(I18nService.Instance.T("network.provider_fallback", fullPath));
                        return new CustomFileInfo(fullPath);
                    }
                }
                
                return fileInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("network.file_info_error", relativePath));
                return null;
            }
        }

        /// <summary>
        /// 检查目录是否存在
        /// </summary>
        public bool DirectoryExists(string relativePath)
        {
            try
            {
                if (_fileProvider == null)
                    return false;

                var fileInfo = _fileProvider.GetFileInfo(relativePath);
                return fileInfo.Exists && fileInfo.IsDirectory;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("network.directory_exists_error", relativePath));
                return false;
            }
        }

        /// <summary>
        /// 获取目录内容
        /// </summary>
        public IDirectoryContents? GetDirectoryContents(string relativePath)
        {
            try
            {
                return _fileProvider?.GetDirectoryContents(relativePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("network.directory_contents_error", relativePath));
                return null;
            }
        }

        /// <summary>
        /// 更新文件服务路径
        /// </summary>
        public void UpdatePath(string newPath)
        {
            try
            {
                if (_currentPath == newPath)
                    return;

                _logger.LogInfo(I18nService.Instance.T("network.updating_path", _currentPath, newPath));

                if (_fileProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _fileProvider = null;

                Initialize(newPath);
                
                _logger.LogInfo(I18nService.Instance.T("network.path_updated"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("network.update_failed", newPath));
                throw;
            }
        }

        /// <summary>
        /// 获取当前路径
        /// </summary>
        public string GetCurrentPath() => _currentPath;

        /// <summary>
        /// 检查是否为网络路径
        /// </summary>
        public bool IsCurrentPathNetwork() => _isNetworkPath;

        public void Dispose()
        {
            if (_fileProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// 自定义文件信息类，用于处理PhysicalFileProvider无法处理的路径
    /// </summary>
    public class CustomFileInfo : IFileInfo
    {
        private readonly System.IO.FileInfo _fileInfo;

        public CustomFileInfo(string physicalPath)
        {
            _fileInfo = new System.IO.FileInfo(physicalPath);
            Name = _fileInfo.Name;
            PhysicalPath = physicalPath;
            Exists = _fileInfo.Exists;
            Length = Exists ? _fileInfo.Length : 0;
            LastModified = Exists ? _fileInfo.LastWriteTimeUtc : DateTimeOffset.MinValue;
            IsDirectory = false;
        }

        public bool Exists { get; }
        public long Length { get; }
        public string PhysicalPath { get; }
        public string Name { get; }
        public DateTimeOffset LastModified { get; }
        public bool IsDirectory { get; }

        public Stream CreateReadStream()
        {
            if (!Exists)
                throw new FileNotFoundException(I18nService.Instance.T("network.file_not_found", PhysicalPath));

            return new FileStream(PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }
}