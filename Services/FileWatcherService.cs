using System.IO;
using Repository.Models;
using Repository.Services;

namespace Repository.Services
{
    public class FileWatcherService : IDisposable
    {
        private readonly Logger _logger;
        private readonly ConfigManager _configManager;
        private FileSystemWatcher? _fileWatcher;
        private string _repositoryPath;
        
        // 事件：当文件或目录发生变化时触发
        public event EventHandler<FileSystemEventInfo>? OnFileChanged;
        public event EventHandler<FileSystemEventInfo>? OnFileCreated;
        public event EventHandler<FileSystemEventInfo>? OnFileDeleted;
        public event EventHandler<FileSystemEventInfo>? OnFileRenamed;

        public FileWatcherService(Logger logger, ConfigManager configManager)
        {
            _logger = logger;
            _configManager = configManager;
            _repositoryPath = Path.GetFullPath(_configManager.GetConfig().RepositoryPath);
            
            StartFileWatcher();
        }

        private void StartFileWatcher()
        {
            try
            {
                _fileWatcher = new FileSystemWatcher();
                _fileWatcher.Path = _repositoryPath;
                
                // 监视所有文件和子目录
                _fileWatcher.IncludeSubdirectories = true;
                _fileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | 
                                           NotifyFilters.LastWrite | NotifyFilters.Size | 
                                           NotifyFilters.CreationTime;
                
                // 注册事件处理程序
                _fileWatcher.Created += OnFileSystemCreated;
                _fileWatcher.Changed += OnFileSystemChanged;
                _fileWatcher.Deleted += OnFileSystemDeleted;
                _fileWatcher.Renamed += OnFileSystemRenamed;
                _fileWatcher.Error += OnFileSystemError;
                
                _fileWatcher.EnableRaisingEvents = true;
                
                _logger.LogInfo(I18nService.Instance.T("filewatcher.started", _repositoryPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("filewatcher.start_failed"));
            }
        }

        private void OnFileSystemCreated(object sender, FileSystemEventArgs e)
        {
            _logger.LogInfo(I18nService.Instance.T("filewatcher.file_created", e.FullPath));
            var eventInfo = GetFileSystemEventInfo(e.FullPath, "created");
            OnFileCreated?.Invoke(this, eventInfo);
            OnFileChanged?.Invoke(this, eventInfo);
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            _logger.LogInfo(I18nService.Instance.T("filewatcher.file_changed", e.FullPath));
            var eventInfo = GetFileSystemEventInfo(e.FullPath, "changed");
            OnFileChanged?.Invoke(this, eventInfo);
        }

        private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
        {
            _logger.LogInfo(I18nService.Instance.T("filewatcher.file_deleted", e.FullPath));
            var eventInfo = GetFileSystemEventInfo(e.FullPath, "deleted");
            OnFileDeleted?.Invoke(this, eventInfo);
            OnFileChanged?.Invoke(this, eventInfo);
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            _logger.LogInfo(I18nService.Instance.T("filewatcher.file_renamed", e.OldFullPath, e.FullPath));
            var eventInfo = GetFileSystemEventInfo(e.FullPath, "renamed");
            OnFileRenamed?.Invoke(this, eventInfo);
            OnFileChanged?.Invoke(this, eventInfo);
        }

        private void OnFileSystemError(object sender, ErrorEventArgs e)
        {
            _logger.LogError(e.GetException(), I18nService.Instance.T("filewatcher.error"));
        }

        // 获取相对路径（相对于仓库根目录）
        public string GetRelativePath(string fullPath)
        {
            if (fullPath.StartsWith(_repositoryPath))
            {
                return fullPath.Substring(_repositoryPath.Length).TrimStart(Path.DirectorySeparatorChar);
            }
            return fullPath;
        }

        // 获取当前路径的文件系统事件信息
        public FileSystemEventInfo GetFileSystemEventInfo(string path, string changeType)
        {
            var relativePath = GetRelativePath(path);
            var isDirectory = Directory.Exists(path);
            
            return new FileSystemEventInfo
            {
                Path = relativePath,
                ChangeType = changeType,
                IsDirectory = isDirectory,
                Timestamp = DateTime.UtcNow
            };
        }

        public void StartWatching(string repositoryPath)
        {
            _repositoryPath = Path.GetFullPath(repositoryPath);
            if (_fileWatcher != null)
            {
                _fileWatcher.Path = _repositoryPath;
                _fileWatcher.EnableRaisingEvents = true;
                _logger.LogInfo(I18nService.Instance.T("filewatcher.started", _repositoryPath));
            }
        }

        public void StopWatching()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _logger.LogInfo(I18nService.Instance.T("filewatcher.stopped"));
            }
        }

        public void UpdateRepositoryPath(string newRepositoryPath)
        {
            var oldPath = _repositoryPath;
            _repositoryPath = Path.GetFullPath(newRepositoryPath);
            
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Path = _repositoryPath;
                _fileWatcher.EnableRaisingEvents = true;
                
                _logger.LogInfo(I18nService.Instance.T("filewatcher.path_updated", oldPath, _repositoryPath));
            }
        }

        public void Dispose()
        {
            _fileWatcher?.Dispose();
        }
    }

    // 文件系统事件信息模型
    public class FileSystemEventInfo
    {
        public string Path { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty; // created, changed, deleted, renamed
        public bool IsDirectory { get; set; }
        public DateTime Timestamp { get; set; }
    }
}