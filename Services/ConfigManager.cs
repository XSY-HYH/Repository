using System.Text.Json;
using Repository.Models;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Repository.Services
{
    public class ConfigManager : IDisposable
    {
        private readonly string _configPath = "Config.json";
        private Config? _config;
        private FileSystemWatcher? _fileWatcher;
        private readonly Logger _logger;
        
        // 防抖相关字段
        private DateTime _lastConfigLoadTime = DateTime.MinValue;
        private readonly TimeSpan _configLoadThreshold = TimeSpan.FromMilliseconds(500);
        private bool _isProcessingChange = false;
        private readonly object _configLock = new object();
        
        // 文件锁定相关字段
        private FileStream? _configFileLock;
        private readonly object _fileLock = new object();
        
        // 配置变更事件
        public event EventHandler<Config>? OnConfigChanged;

        public ConfigManager(Logger logger)
        {
            _logger = logger;
            LockConfigFile();
            LoadConfig();
            StartFileWatcher();
        }

        public Config GetConfig()
        {
            return _config!;
        }

        public void SaveConfig(Config config)
        {
            // 验证配置
            ValidateConfig(config);
            
            lock (_configLock)
            {
                _config = config;
                
                // 保存时临时禁用文件监视器，避免触发自身更改事件
                if (_fileWatcher != null)
                    _fileWatcher.EnableRaisingEvents = false;
                
                try
                {
                    // 使用 JsonSerializer 自动构建 JSON 字符串
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                    };
                    var json = JsonSerializer.Serialize(config, options);
                    using var writer = new StreamWriter(_configPath);
                    writer.Write(json);
                    
                    _logger.LogInfo(I18nService.Instance.T("config.saved"));
                }
                finally
                {
                    if (_fileWatcher != null)
                        _fileWatcher.EnableRaisingEvents = true;
                }
            }
        }

        // 转义 JSON 字符串中的特殊字符
        private string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
                
            return value
                .Replace("\\", "\\\\")  // 转义反斜杠
                .Replace("\"", "\\\"")  // 转义双引号
                .Replace("\b", "\\b")   // 转义退格
                .Replace("\f", "\\f")   // 转义换页
                .Replace("\n", "\\n")   // 转义换行
                .Replace("\r", "\\r")   // 转义回车
                .Replace("\t", "\\t");  // 转义制表符
        }

        private void ValidateConfig(Config config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
                
            if (config.Port < 1 || config.Port > 65535)
                throw new ArgumentException("Port must be between 1 and 65535");
                
            if (string.IsNullOrWhiteSpace(config.RepositoryPath))
                throw new ArgumentException("RepositoryPath cannot be null or empty");
        }

        private void LoadConfig()
        {
            lock (_configLock)
            {
                if (File.Exists(_configPath))
                {
                    try
                    {
                        // 重试机制，避免文件被锁定
                        for (int i = 0; i < 3; i++)
                        {
                            try
                            {
                                var json = File.ReadAllText(_configPath);
                                
                                // 检查文件内容是否为空或无效
                                if (string.IsNullOrWhiteSpace(json))
                                {
                                    throw new InvalidDataException("Configuration file is empty or contains only whitespace");
                                }
                                
                                var newConfig = JsonSerializer.Deserialize<Config>(json);
                                
                                // 检查反序列化结果是否为null
                                if (newConfig == null)
                                {
                                    throw new JsonException("Failed to deserialize configuration file - invalid JSON format");
                                }
                                
                                // 验证配置的必需字段
                                ValidateConfig(newConfig);
                                
                                // 只有配置实际发生变化时才更新
                                if (!ConfigEquals(_config, newConfig))
                                {
                                    _config = newConfig;
                                    _logger.LogInfo("Configuration loaded from file");
                                }
                                break;
                            }
                            catch (IOException) when (i < 2)
                            {
                                Thread.Sleep(50);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(I18nService.Instance.T("config.load_failed", ex.GetType().Name, ex.Message));
                        
                        try
                        {
                            if (File.Exists(_configPath))
                            {
                                string backupPath = _configPath + ".backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                File.Copy(_configPath, backupPath);
                                _logger.LogInfo(I18nService.Instance.T("config.backup_created", backupPath));
                            }
                            
                            File.Delete(_configPath);
                            _logger.LogInfo(I18nService.Instance.T("config.corrupted_deleted"));
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.LogWarning(I18nService.Instance.T("config.delete_failed", deleteEx.Message));
                        }
                        
                        // 创建默认配置
                        _config = CreateDefaultConfig();
                        SaveConfig(_config);
                    }
                }
                else
                {
                    _logger.LogInfo(I18nService.Instance.T("config.not_found"));
                    _config = CreateDefaultConfig();
                    SaveConfig(_config);
                }
            }
        }

        private bool ConfigEquals(Config? oldConfig, Config? newConfig)
        {
            if (oldConfig == null || newConfig == null)
                return false;
                
            return oldConfig.IP == newConfig.IP &&
                   oldConfig.Port == newConfig.Port &&
                   oldConfig.RepositoryPath == newConfig.RepositoryPath &&
                   oldConfig.IPBlocking == newConfig.IPBlocking &&
                   oldConfig.IPBlockingList == newConfig.IPBlockingList &&
                   oldConfig.Blacklist == newConfig.Blacklist &&
                   oldConfig.GenerateHelp == newConfig.GenerateHelp &&
                   oldConfig.PintoTop == newConfig.PintoTop &&
                   oldConfig.Background == newConfig.Background &&
                   oldConfig.DDoSProtection == newConfig.DDoSProtection &&
                   oldConfig.MaxRequestsPerMinute == newConfig.MaxRequestsPerMinute &&
                   oldConfig.BlockDurationMinutes == newConfig.BlockDurationMinutes &&
                   oldConfig.BlockedIPs == newConfig.BlockedIPs &&
                   oldConfig.UploadEnabled == newConfig.UploadEnabled &&
                   oldConfig.MaxUploadSizeMB == newConfig.MaxUploadSizeMB &&
                   oldConfig.MaxDownloadSizeMB == newConfig.MaxDownloadSizeMB &&
                   oldConfig.PreviewEnabled == newConfig.PreviewEnabled &&
                   oldConfig.PreviewExtensions == newConfig.PreviewExtensions &&
                   oldConfig.ImagePreviewExtensions == newConfig.ImagePreviewExtensions &&
                   oldConfig.AudioPreviewExtensions == newConfig.AudioPreviewExtensions &&
                   oldConfig.VideoPreviewExtensions == newConfig.VideoPreviewExtensions &&
                   oldConfig.ForbiddenDownloadPaths == newConfig.ForbiddenDownloadPaths &&
                   oldConfig.ForbiddenPreviewPaths == newConfig.ForbiddenPreviewPaths &&
                   oldConfig.HiddenPaths == newConfig.HiddenPaths &&
                   oldConfig.HttpsEnabled == newConfig.HttpsEnabled &&
                   oldConfig.HttpsPort == newConfig.HttpsPort &&
                   oldConfig.HttpsCertificatePath == newConfig.HttpsCertificatePath &&
                   oldConfig.HttpsCertificatePassword == newConfig.HttpsCertificatePassword &&
                   oldConfig.HttpEnabled == newConfig.HttpEnabled;
        }

        private Config CreateDefaultConfig()
        {
            var defaultConfig = new Config
            {
                IP = "0.0.0.0",
                Port = 8000,
                RepositoryPath = "./Repository",
                IPBlocking = false,
                IPBlockingList = "127.0.0.1:8000",
                Blacklist = "",
                GenerateHelp = true,
                PintoTop = true,
                Background = false,
                DDoSProtection = true,
                MaxRequestsPerMinute = 100,
                BlockDurationMinutes = 30,
                BlockedIPs = "",
                UploadEnabled = false,
                MaxUploadSizeMB = 50,
                MaxDownloadSizeMB = 100, // 显式设置下载大小限制默认值
                PreviewEnabled = true,
                PreviewExtensions = ".txt,.md,.json,.xml,.html,.css,.js,.cs,.py,.java,.c,.cpp,.h,.hpp,.sh,.bat,.ini,.log,.csv,.tsv",
                ImagePreviewExtensions = ".jpg,.jpeg,.png,.gif,.bmp,.webp,.svg,.ico,.tiff,.tif",
                AudioPreviewExtensions = ".mp3,.wav,.ogg,.flac,.aac,.m4a,.wma",
                VideoPreviewExtensions = ".mp4,.avi,.mov,.wmv,.flv,.webm,.mkv,.m4v,.3gp,.ogv",
                ForbiddenDownloadPaths = "",
                ForbiddenPreviewPaths = "",
                HiddenPaths = "",
                Language = "en"
            };
            
            // 创建默认配置时生成help.txt文件
            GenerateHelpFile(defaultConfig);
            
            return defaultConfig;
        }

        // 生成help.txt文件
        private void GenerateHelpFile(Config? config = null)
        {
            var targetConfig = config ?? _config;
            
            if (targetConfig == null)
            {
                _logger.LogWarning(I18nService.Instance.T("config.help_null"));
                return;
            }
            
            if (!targetConfig.GenerateHelp)
            {
                if (File.Exists("help.txt"))
                {
                    try
                    {
                        File.Delete("help.txt");
                        _logger.LogInfo(I18nService.Instance.T("config.help_deleted"));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(I18nService.Instance.T("config.help_delete_failed", ex.Message));
                    }
                }
                return;
            }
            
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "Repository.help.txt";
                
                if (assembly.GetManifestResourceNames().Contains(resourceName))
                {
                    using (var stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream != null)
                        {
                            using (var reader = new StreamReader(stream))
                            {
                                string helpContent = reader.ReadToEnd();
                                File.WriteAllText("help.txt", helpContent, System.Text.Encoding.UTF8);
                                _logger.LogInfo(I18nService.Instance.T("config.help_extracted"));
                            }
                        }
                        else
                        {
                            _logger.LogWarning(I18nService.Instance.T("config.help_stream_null"));
                        }
                    }
                }
                else
                {
                    _logger.LogWarning(I18nService.Instance.T("config.help_not_found", resourceName));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(I18nService.Instance.T("config.help_extract_failed", ex.Message));
            }
        }

        public void EnsureRepositoryDirectoryExists()
        {
            var config = GetConfig();
            
            bool isNetworkPath = config.RepositoryPath.StartsWith("\\\\") || config.RepositoryPath.StartsWith("//");
            
            if (isNetworkPath)
            {
                _logger.LogInfo(I18nService.Instance.T("config.repo_ensured", config.RepositoryPath));
            }
            else
            {
                if (!Directory.Exists(config.RepositoryPath))
                {
                    Directory.CreateDirectory(config.RepositoryPath);
                    _logger.LogInfo(I18nService.Instance.T("config.repo_created", config.RepositoryPath));
                }
                else
                {
                    _logger.LogInfo(I18nService.Instance.T("config.repo_ensured", config.RepositoryPath));
                }
            }
        }

        private void LockConfigFile()
        {
            lock (_fileLock)
            {
                try
                {
                    // 确保配置文件存在
                    if (!File.Exists(_configPath))
                    {
                        // 如果文件不存在，创建默认配置并保存
                        _config = CreateDefaultConfig();
                        SaveConfig(_config);
                    }

                    // 以共享读取模式打开文件，防止被删除
                    _configFileLock = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _logger.LogInfo(I18nService.Instance.T("config.file_locked", _configPath));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(I18nService.Instance.T("config.lock_failed", ex.Message));
                }
            }
        }

        private void UnlockConfigFile()
        {
            lock (_fileLock)
            {
                try
                {
                    if (_configFileLock != null)
                    {
                        _configFileLock.Close();
                        _configFileLock.Dispose();
                        _configFileLock = null;
                        _logger.LogInfo(I18nService.Instance.T("config.file_unlocked"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(I18nService.Instance.T("config.unlock_failed", ex.Message));
                }
            }
        }

        private void StartFileWatcher()
        {
            try
            {
                // 确保配置文件所在目录存在
                var configDirectory = Path.GetDirectoryName(Path.GetFullPath(_configPath));
                if (!Directory.Exists(configDirectory) && configDirectory != null)
                {
                    Directory.CreateDirectory(configDirectory);
                }

                _fileWatcher = new FileSystemWatcher();
                _fileWatcher.Path = Path.GetDirectoryName(Path.GetFullPath(_configPath)) ?? ".";
                _fileWatcher.Filter = Path.GetFileName(_configPath);
                _fileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                _fileWatcher.Changed += OnConfigFileChanged;
                _fileWatcher.EnableRaisingEvents = true;
                
                _logger.LogInfo(I18nService.Instance.T("config.watcher_started"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(I18nService.Instance.T("config.watcher_failed", ex.Message));
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            // 防抖：避免短时间内重复处理
            var now = DateTime.Now;
            if (now - _lastConfigLoadTime < _configLoadThreshold)
            {
                _logger.LogInfo(I18nService.Instance.T("config.throttled"));
                return;
            }

            if (_isProcessingChange)
            {
                _logger.LogInfo(I18nService.Instance.T("config.already_processing"));
                return;
            }

            _isProcessingChange = true;
            _lastConfigLoadTime = now;

            // 延迟加载以避免文件被锁定时读取
            Task.Delay(100).ContinueWith(_ =>
            {
                try
                {
                    var oldConfig = _config;
                    LoadConfig();
                    
                    if (!ConfigEquals(oldConfig, _config))
                    {
                        _logger.LogInfo(I18nService.Instance.T("config.reloaded"));
                        
                        OnConfigChanged?.Invoke(this, _config!);
                        
                        _logger.LogInfo(I18nService.Instance.T("config.change_event", _config!.IP, _config.Port, _config.RepositoryPath));
                    }
                    else
                    {
                        _logger.LogInfo(I18nService.Instance.T("config.unchanged"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, I18nService.Instance.T("config.reload_failed"));
                }
                finally
                {
                    _isProcessingChange = false;
                }
            });
        }

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    UnlockConfigFile();
                    _fileWatcher?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}