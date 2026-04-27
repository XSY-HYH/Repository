using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Repository.Models;
using System.IO;

namespace Repository.Services
{
    public class ConfigManager : IDisposable
    {
        private readonly string _configPath = "Config.yml";
        private Config? _config;
        private FileSystemWatcher? _fileWatcher;
        private readonly Logger _logger;
        
        private DateTime _lastConfigLoadTime = DateTime.MinValue;
        private readonly TimeSpan _configLoadThreshold = TimeSpan.FromMilliseconds(500);
        private bool _isProcessingChange = false;
        private readonly object _configLock = new object();
        
        private FileStream? _configFileLock;
        private readonly object _fileLock = new object();
        
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
            if (_disposed)
                return;

            ValidateConfig(config);
            
            lock (_configLock)
            {
                _config = config;
                
                if (_fileWatcher != null && !_disposed)
                {
                    try
                    {
                        _fileWatcher.EnableRaisingEvents = false;
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
                
                try
                {
                    var yamlContent = GenerateYamlWithComments(config);
                    File.WriteAllText(_configPath, yamlContent);
                    
                    _logger.LogInfo(I18nService.Instance.T("config.saved"));
                }
                finally
                {
                    if (_fileWatcher != null && !_disposed)
                    {
                        try
                        {
                            _fileWatcher.EnableRaisingEvents = true;
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }
                }
            }
        }

        private string GenerateYamlWithComments(Config config)
        {
            bool isZh = config.Language == "zh";
            var lines = new List<string>();

            if (isZh)
            {
                lines.Add("# Repository 配置文件");
                lines.Add("# 语言: zh");
                lines.Add("");
                lines.Add("# 服务器监听地址（0.0.0.0 为所有接口，:: 为所有 IPv6 接口）");
            }
            else
            {
                lines.Add("# Repository Configuration File");
                lines.Add("# Language: en");
                lines.Add("");
                lines.Add("# Server listening address (0.0.0.0 for all interfaces, :: for IPv6 all interfaces)");
            }
            lines.Add($"IP: \"{EscapeYamlString(config.IP)}\"");
            if (isZh) lines.Add("# 仓库根目录路径");
            else lines.Add("# Repository root directory path");
            lines.Add($"RepositoryPath: \"{EscapeYamlString(config.RepositoryPath)}\"");
            if (isZh) lines.Add("# 启用 HTTPS");
            else lines.Add("# Enable HTTPS");
            lines.Add($"HttpsEnabled: {config.HttpsEnabled.ToString().ToLower()}");
            if (isZh) lines.Add("# HTTPS 端口号");
            else lines.Add("# HTTPS port number");
            lines.Add($"HttpsPort: {config.HttpsPort}");
            if (isZh) lines.Add("# SSL 证书文件路径");
            else lines.Add("# SSL certificate file path");
            lines.Add($"HttpsCertificatePath: \"{EscapeYamlString(config.HttpsCertificatePath)}\"");
            if (isZh) lines.Add("# SSL 证书密码（仅限PFX）");
            else lines.Add("# SSL certificate password (PFX only)");
            lines.Add($"HttpsCertificatePassword: \"{EscapeYamlString(config.HttpsCertificatePassword)}\"");
            if (isZh) lines.Add("# 启用 HTTP 到 HTTPS 重定向（需要启用http）");
            else lines.Add("# Enable HTTP to HTTPS redirect (requires HTTP enabled)");
            lines.Add($"HttpsRedirectEnabled: {config.HttpsRedirectEnabled.ToString().ToLower()}");
            if (isZh) lines.Add("# 用于证书生成的域名");
            else lines.Add("# Domain name for certificate generation");
            lines.Add($"Domain: \"{EscapeYamlString(config.Domain)}\"");
            if (isZh) lines.Add("# 启用 HTTP（设为 false 时仅使用 HTTPS）");
            else lines.Add("# Enable HTTP (when false, only HTTPS is available)");
            lines.Add($"HttpEnabled: {config.HttpEnabled.ToString().ToLower()}");
            if (isZh) lines.Add("# HTTP端口号");
            else lines.Add("# HTTP port number");
            lines.Add($"Port: {config.Port}");
            if (isZh) lines.Add("# 启用请求限速（速率限制）");
            else lines.Add("# Enable request throttling (rate limiting)");
            lines.Add($"RequestThrottling: {config.RequestThrottling.ToString().ToLower()}");
            if (isZh) lines.Add("# 每秒最大请求值");
            else lines.Add("# Maximum requests per second");
            lines.Add($"MaximumRequestsPerSecond: {config.MaximumRequestsPerSecond}");
            if (isZh) lines.Add("# 每秒告警请求值");
            else lines.Add("# Alarm threshold per second");
            lines.Add($"AlarmRequestsPerSecond: {config.AlarmRequestsPerSecond}");
            if (isZh) lines.Add("# 已封禁的IP");
            else lines.Add("# Blocked IPs");
            lines.Add($"BlockedIPs: \"{EscapeYamlString(config.BlockedIPs)}\"");
            if (isZh) lines.Add("# 临时封锁的IP");
            else lines.Add("# Temporarily blocked IPs");
            lines.Add($"TemporarilyBlockedIPs: \"{EscapeYamlString(config.TemporarilyBlockedIPs)}\"");
            if (isZh) lines.Add("# 超过限制时的封禁时长（分钟）");
            else lines.Add("# Ban duration when exceeding the limit (minutes)");
            lines.Add($"BlockDurationMinutes: {config.BlockDurationMinutes}");
            if (isZh) lines.Add("# 启用 IP 封锁");
            else lines.Add("# Enable IP blocking");
            lines.Add($"IPBlocking: {config.IPBlocking.ToString().ToLower()}");
            if (isZh) lines.Add("# 隐藏的文件/文件夹名称和模式（用 | 分隔）");
            else lines.Add("# Hidden file/folder names and patterns (separated by |)");
            lines.Add($"Blacklist: \"{EscapeYamlString(config.Blacklist)}\"");
            if (isZh) lines.Add("# 启动时生成 help.txt 文件");
            else lines.Add("# Generate help.txt file on startup");
            lines.Add($"GenerateHelp: {config.GenerateHelp.ToString().ToLower()}");
            if (isZh) lines.Add("# 将控制台窗口置顶（仅 Windows）");
            else lines.Add("# Set console window to topmost (Windows only)");
            lines.Add($"PintoTop: {config.PintoTop.ToString().ToLower()}");
            if (isZh) lines.Add("# 后台运行/隐藏控制台（仅 Windows）");
            else lines.Add("# Run in background/hide console (Windows only)");
            lines.Add($"Background: {config.Background.ToString().ToLower()}");
            if (isZh) lines.Add("# 启用请求头过滤");
            else lines.Add("# Enable request header filtering");
            lines.Add($"RequestHeaderFiltering: {config.RequestHeaderFiltering.ToString().ToLower()}");
            if (isZh) lines.Add("# User-Agent 请求头");
            else lines.Add("# User-Agent header");
            lines.Add($"RequireUserAgent: {config.RequireUserAgent.ToString().ToLower()}");
            if (isZh) lines.Add("# Accept 请求头");
            else lines.Add("# Accept header");
            lines.Add($"RequireAccept: {config.RequireAccept.ToString().ToLower()}");
            if (isZh) lines.Add("# 允许的浏览器标识符");
            else lines.Add("# Allowed browser identifiers");
            lines.Add($"AllowedBrowsers: \"{EscapeYamlString(config.AllowedBrowsers)}\"");
            if (isZh) lines.Add("# 启用文件上传功能");
            else lines.Add("# Enable file upload feature");
            lines.Add($"UploadEnabled: {config.UploadEnabled.ToString().ToLower()}");
            if (isZh) lines.Add("# 最大上传文件大小（MB）");
            else lines.Add("# Maximum upload file size (MB)");
            lines.Add($"MaxUploadSizeMB: {config.MaxUploadSizeMB}");
            if (isZh) lines.Add("# 允许上传的文件扩展名（逗号分隔）");
            else lines.Add("# Allowed file extensions for upload (comma-separated)");
            lines.Add($"AllowedUploadExtensions: \"{EscapeYamlString(config.AllowedUploadExtensions)}\"");
            if (isZh) lines.Add("# 允许覆盖已存在的文件");
            else lines.Add("# Allow overwriting existing files");
            lines.Add($"AllowOverwrite: {config.AllowOverwrite.ToString().ToLower()}");
            if (isZh) lines.Add("# 允许上传到根目录");
            else lines.Add("# Allow uploading to root directory");
            lines.Add($"AllowRootUpload: {config.AllowRootUpload.ToString().ToLower()}");
            if (isZh) lines.Add("# 最大磁盘使用百分比（0 = 无限制）");
            else lines.Add("# Maximum disk usage percentage (0 = unlimited)");
            lines.Add($"MaxDiskUsagePercent: {config.MaxDiskUsagePercent}");
            if (isZh) lines.Add("# 最大下载文件大小（MB）");
            else lines.Add("# Maximum download file size (MB)");
            lines.Add($"MaxDownloadSizeMB: {config.MaxDownloadSizeMB}");
            if (isZh) lines.Add("# 启用文件预览功能");
            else lines.Add("# Enable file preview feature");
            lines.Add($"PreviewEnabled: {config.PreviewEnabled.ToString().ToLower()}");
            if (isZh) lines.Add("# 文本文件预览扩展名");
            else lines.Add("# Text file preview extensions");
            lines.Add($"PreviewExtensions: \"{EscapeYamlString(config.PreviewExtensions)}\"");
            if (isZh) lines.Add("# 图片预览扩展名");
            else lines.Add("# Image preview extensions");
            lines.Add($"ImagePreviewExtensions: \"{EscapeYamlString(config.ImagePreviewExtensions)}\"");
            if (isZh) lines.Add("# 音频预览扩展名");
            else lines.Add("# Audio preview extensions");
            lines.Add($"AudioPreviewExtensions: \"{EscapeYamlString(config.AudioPreviewExtensions)}\"");
            if (isZh) lines.Add("# 视频预览扩展名");
            else lines.Add("# Video preview extensions");
            lines.Add($"VideoPreviewExtensions: \"{EscapeYamlString(config.VideoPreviewExtensions)}\"");
            if (isZh) lines.Add("# 禁止下载的路径");
            else lines.Add("# Forbidden download paths");
            lines.Add($"ForbiddenDownloadPaths: \"{EscapeYamlString(config.ForbiddenDownloadPaths)}\"");
            if (isZh) lines.Add("# 禁止预览的路径");
            else lines.Add("# Forbidden preview paths");
            lines.Add($"ForbiddenPreviewPaths: \"{EscapeYamlString(config.ForbiddenPreviewPaths)}\"");
            if (isZh) lines.Add("# 隐藏的路径");
            else lines.Add("# Hidden paths");
            lines.Add($"HiddenPaths: \"{EscapeYamlString(config.HiddenPaths)}\"");
            if (isZh) lines.Add("# 启用路径保护");
            else lines.Add("# Enable path protection");
            lines.Add($"ProtectEnabled: {config.ProtectEnabled.ToString().ToLower()}");
            if (isZh) lines.Add("# 受保护的路径");
            else lines.Add("# Protected paths");
            lines.Add($"ProtectPaths: \"{EscapeYamlString(config.ProtectPaths)}\"");
            if (isZh) lines.Add("# 启用 PROXY Protocol 支持");
            else lines.Add("# Enable PROXY Protocol support");
            lines.Add($"ProxyProtocolEnabled: {config.ProxyProtocolEnabled.ToString().ToLower()}");
            if (isZh) lines.Add("# 启用系统通知（仅 Windows）");
            else lines.Add("# Enable system notifications (Windows only)");
            lines.Add($"Notification: {config.Notification.ToString().ToLower()}");
            if (isZh) lines.Add("# 启用管理面板");
            else lines.Add("# Enable admin panel");
            lines.Add($"AdminEnabled: {config.AdminEnabled.ToString().ToLower()}");
            if (isZh) lines.Add("# 管理员用户名");
            else lines.Add("# Admin username");
            lines.Add($"AdminUsername: \"{EscapeYamlString(config.AdminUsername)}\"");
            if (isZh) lines.Add("# 管理员密码");
            else lines.Add("# Admin password");
            lines.Add($"AdminPassword: \"{EscapeYamlString(config.AdminPassword)}\"");
            if (isZh) lines.Add("# 界面语言");
            else lines.Add("# Interface language");
            lines.Add($"Language: \"{EscapeYamlString(config.Language)}\"");
            if (isZh) lines.Add("# 访问统计（由系统填充）");
            else lines.Add("# Access statistics (auto-filled by system)");
            lines.Add($"AccessCount: {config.AccessCount}");
            if (isZh) lines.Add("# 崩溃时自动重启");
            else lines.Add("# Enable auto restart on crash");
            lines.Add($"AutoRestart: {config.AutoRestart.ToString().ToLower()}");
            if (isZh) lines.Add("# 最大重启尝试次数");
            else lines.Add("# Maximum restart attempts");
            lines.Add($"MaxRestartAttempts: {config.MaxRestartAttempts}");
            if (isZh) lines.Add("# 当前重启次数（由系统填充）");
            else lines.Add("# Current restart count (auto-filled by system)");
            lines.Add($"RestartCount: {config.RestartCount}");

            return string.Join("\n", lines) + "\n";
        }

        private string EscapeYamlString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
                
            return value.Replace("\\", "\\\\")
                        .Replace("\"", "\\\"")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r")
                        .Replace("\t", "\\t");
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
                        for (int i = 0; i < 3; i++)
                        {
                            try
                            {
                                var yaml = File.ReadAllText(_configPath);
                                
                                if (string.IsNullOrWhiteSpace(yaml))
                                {
                                    throw new InvalidDataException("Configuration file is empty");
                                }
                                
                                var deserializer = new DeserializerBuilder()
                                    .Build();
                                
                                var newConfig = deserializer.Deserialize<Config>(yaml);
                                
                                if (newConfig == null)
                                {
                                    throw new Exception("Failed to deserialize configuration file");
                                }
                                
                                ValidateConfig(newConfig);
                                
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

                if (_config != null)
                {
                    ReplaceCommentsBasedOnLanguage(_config.Language);
                }
            }
        }

        private void ReplaceCommentsBasedOnLanguage(string language)
        {
            try
            {
                if (!File.Exists(_configPath))
                    return;

                bool isZh = language == "zh";
                var yamlContent = File.ReadAllText(_configPath);
                var lines = yamlContent.Split('\n');
                var newLines = new List<string>();

                var enComments = new Dictionary<string, string>
                {
                    { "# Repository 配置文件", "# Repository Configuration File" },
                    { "# 语言: zh", "# Language: en" },
                    { "# 服务器监听地址（0.0.0.0 为所有接口，:: 为所有 IPv6 接口）", "# Server listening address (0.0.0.0 for all interfaces, :: for IPv6 all interfaces)" },
                    { "# 仓库根目录路径", "# Repository root directory path" },
                    { "# 启用 HTTPS", "# Enable HTTPS" },
                    { "# HTTPS 端口号", "# HTTPS port number" },
                    { "# SSL 证书文件路径", "# SSL certificate file path" },
                    { "# SSL 证书密码（仅限PFX）", "# SSL certificate password (PFX only)" },
                    { "# 启用 HTTP 到 HTTPS 重定向（需要启用http）", "# Enable HTTP to HTTPS redirect (requires HTTP enabled)" },
                    { "# 用于证书生成的域名", "# Domain name for certificate generation" },
                    { "# 启用 HTTP（设为 false 时仅使用 HTTPS）", "# Enable HTTP (when false, only HTTPS is available)" },
                    { "# HTTP端口号", "# HTTP port number" },
                    { "# 启用请求限速（速率限制）", "# Enable request throttling (rate limiting)" },
                    { "# 每秒最大请求值", "# Maximum requests per second" },
                    { "# 每秒告警请求值", "# Alarm threshold per second" },
                    { "# 已封禁的IP", "# Blocked IPs" },
                    { "# 临时封锁的IP", "# Temporarily blocked IPs" },
                    { "# 超过限制时的封禁时长（分钟）", "# Ban duration when exceeding the limit (minutes)" },
                    { "# 启用 IP 封锁", "# Enable IP blocking" },
                    { "# 隐藏的文件/文件夹名称和模式（用 | 分隔）", "# Hidden file/folder names and patterns (separated by |)" },
                    { "# 启动时生成 help.txt 文件", "# Generate help.txt file on startup" },
                    { "# 将控制台窗口置顶（仅 Windows）", "# Set console window to topmost (Windows only)" },
                    { "# 后台运行/隐藏控制台（仅 Windows）", "# Run in background/hide console (Windows only)" },
                    { "# 启用请求头过滤", "# Enable request header filtering" },
                    { "# User-Agent 请求头", "# User-Agent header" },
                    { "# Accept 请求头", "# Accept header" },
                    { "# 允许的浏览器标识符", "# Allowed browser identifiers" },
                    { "# 启用JS验证（自动封禁未执行JS的访问者）", "# Enable JS verification (auto-block visitors without JS execution)" },
                    { "# 启用文件上传功能", "# Enable file upload feature" },
                    { "# 最大上传文件大小（MB）", "# Maximum upload file size (MB)" },
                    { "# 允许上传的文件扩展名（逗号分隔）", "# Allowed file extensions for upload (comma-separated)" },
                    { "# 允许覆盖已存在的文件", "# Allow overwriting existing files" },
                    { "# 允许上传到根目录", "# Allow uploading to root directory" },
                    { "# 最大磁盘使用百分比（0 = 无限制）", "# Maximum disk usage percentage (0 = unlimited)" },
                    { "# 最大下载文件大小（MB）", "# Maximum download file size (MB)" },
                    { "# 启用文件预览功能", "# Enable file preview feature" },
                    { "# 文本文件预览扩展名", "# Text file preview extensions" },
                    { "# 图片预览扩展名", "# Image preview extensions" },
                    { "# 音频预览扩展名", "# Audio preview extensions" },
                    { "# 视频预览扩展名", "# Video preview extensions" },
                    { "# 禁止下载的路径", "# Forbidden download paths" },
                    { "# 禁止预览的路径", "# Forbidden preview paths" },
                    { "# 隐藏的路径", "# Hidden paths" },
                    { "# 启用路径保护", "# Enable path protection" },
                    { "# 受保护的路径", "# Protected paths" },
                    { "# 启用 PROXY Protocol 支持", "# Enable PROXY Protocol support" },
                    { "# 启用系统通知（仅 Windows）", "# Enable system notifications (Windows only)" },
                    { "# 启用管理面板", "# Enable admin panel" },
                    { "# 管理员用户名", "# Admin username" },
                    { "# 管理员密码", "# Admin password" },
                    { "# 界面语言", "# Interface language" },
                    { "# 访问统计（由系统填充）", "# Access statistics (auto-filled by system)" },
                    { "# 崩溃时自动重启", "# Enable auto restart on crash" },
                    { "# 最大重启尝试次数", "# Maximum restart attempts" },
                    { "# 当前重启次数（由系统填充）", "# Current restart count (auto-filled by system)" }
                };

                var zhComments = new Dictionary<string, string>
                {
                    { "# Repository Configuration File", "# Repository 配置文件" },
                    { "# Language: en", "# 语言: zh" },
                    { "# Server listening address (0.0.0.0 for all interfaces, :: for IPv6 all interfaces)", "# 服务器监听地址（0.0.0.0 为所有接口，:: 为所有 IPv6 接口）" },
                    { "# Repository root directory path", "# 仓库根目录路径" },
                    { "# Enable HTTPS", "# 启用 HTTPS" },
                    { "# HTTPS port number", "# HTTPS 端口号" },
                    { "# SSL certificate file path", "# SSL 证书文件路径" },
                    { "# SSL certificate password (PFX only)", "# SSL 证书密码（仅限PFX）" },
                    { "# Enable HTTP to HTTPS redirect (requires HTTP enabled)", "# 启用 HTTP 到 HTTPS 重定向（需要启用http）" },
                    { "# Domain name for certificate generation", "# 用于证书生成的域名" },
                    { "# Enable HTTP (when false, only HTTPS is available)", "# 启用 HTTP（设为 false 时仅使用 HTTPS）" },
                    { "# HTTP port number", "# HTTP端口号" },
                    { "# Enable request throttling (rate limiting)", "# 启用请求限速（速率限制）" },
                    { "# Maximum requests per second", "# 每秒最大请求值" },
                    { "# Alarm threshold per second", "# 每秒告警请求值" },
                    { "# Blocked IPs", "# 已封禁的IP" },
                    { "# Temporarily blocked IPs", "# 临时封锁的IP" },
                    { "# Ban duration when exceeding the limit (minutes)", "# 超过限制时的封禁时长（分钟）" },
                    { "# Enable IP blocking", "# 启用 IP 封锁" },
                    { "# Hidden file/folder names and patterns (separated by |)", "# 隐藏的文件/文件夹名称和模式（用 | 分隔）" },
                    { "# Generate help.txt file on startup", "# 启动时生成 help.txt 文件" },
                    { "# Set console window to topmost (Windows only)", "# 将控制台窗口置顶（仅 Windows）" },
                    { "# Run in background/hide console (Windows only)", "# 后台运行/隐藏控制台（仅 Windows）" },
                    { "# Enable request header filtering", "# 启用请求头过滤" },
                    { "# User-Agent header", "# User-Agent 请求头" },
                    { "# Accept header", "# Accept 请求头" },
                    { "# Allowed browser identifiers", "# 允许的浏览器标识符" },
                    { "# Enable JS verification (auto-block visitors without JS execution)", "# 启用JS验证（自动封禁未执行JS的访问者）" },
                    { "# Enable file upload feature", "# 启用文件上传功能" },
                    { "# Maximum upload file size (MB)", "# 最大上传文件大小（MB）" },
                    { "# Allowed file extensions for upload (comma-separated)", "# 允许上传的文件扩展名（逗号分隔）" },
                    { "# Allow overwriting existing files", "# 允许覆盖已存在的文件" },
                    { "# Allow uploading to root directory", "# 允许上传到根目录" },
                    { "# Maximum disk usage percentage (0 = unlimited)", "# 最大磁盘使用百分比（0 = 无限制）" },
                    { "# Maximum download file size (MB)", "# 最大下载文件大小（MB）" },
                    { "# Enable file preview feature", "# 启用文件预览功能" },
                    { "# Text file preview extensions", "# 文本文件预览扩展名" },
                    { "# Image preview extensions", "# 图片预览扩展名" },
                    { "# Audio preview extensions", "# 音频预览扩展名" },
                    { "# Video preview extensions", "# 视频预览扩展名" },
                    { "# Forbidden download paths", "# 禁止下载的路径" },
                    { "# Forbidden preview paths", "# 禁止预览的路径" },
                    { "# Hidden paths", "# 隐藏的路径" },
                    { "# Enable path protection", "# 启用路径保护" },
                    { "# Protected paths", "# 受保护的路径" },
                    { "# Enable PROXY Protocol support", "# 启用 PROXY Protocol 支持" },
                    { "# Enable system notifications (Windows only)", "# 启用系统通知（仅 Windows）" },
                    { "# Enable admin panel", "# 启用管理面板" },
                    { "# Admin username", "# 管理员用户名" },
                    { "# Admin password", "# 管理员密码" },
                    { "# Interface language", "# 界面语言" },
                    { "# Access statistics (auto-filled by system)", "# 访问统计（由系统填充）" },
                    { "# Enable auto restart on crash", "# 崩溃时自动重启" },
                    { "# Maximum restart attempts", "# 最大重启尝试次数" },
                    { "# Current restart count (auto-filled by system)", "# 当前重启次数（由系统填充）" }
                };

                var commentMap = isZh ? zhComments : enComments;

                foreach (var line in lines)
                {
                    var trimmedLine = line.TrimEnd('\r');
                    if (commentMap.ContainsKey(trimmedLine))
                    {
                        newLines.Add(commentMap[trimmedLine]);
                    }
                    else
                    {
                        newLines.Add(trimmedLine);
                    }
                }

                File.WriteAllText(_configPath, string.Join("\n", newLines));
                _logger.LogInfo(I18nService.Instance.T("config.comments_replaced"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(I18nService.Instance.T("config.comment_replace_failed", ex.Message));
            }
        }

        private bool ConfigEquals(Config? oldConfig, Config? newConfig)
        {
            if (oldConfig == null || newConfig == null)
                return false;
                
            return oldConfig.IP == newConfig.IP &&
                   oldConfig.Port == newConfig.Port &&
                   oldConfig.RepositoryPath == newConfig.RepositoryPath &&
                   oldConfig.RequestThrottling == newConfig.RequestThrottling &&
                   oldConfig.MaximumRequestsPerSecond == newConfig.MaximumRequestsPerSecond &&
                   oldConfig.AlarmRequestsPerSecond == newConfig.AlarmRequestsPerSecond &&
                   oldConfig.BlockedIPs == newConfig.BlockedIPs &&
                   oldConfig.TemporarilyBlockedIPs == newConfig.TemporarilyBlockedIPs &&
                   oldConfig.BlockDurationMinutes == newConfig.BlockDurationMinutes &&
                   oldConfig.IPBlocking == newConfig.IPBlocking &&
                   oldConfig.IPBlockingList == newConfig.IPBlockingList &&
                   oldConfig.Blacklist == newConfig.Blacklist &&
                   oldConfig.GenerateHelp == newConfig.GenerateHelp &&
                   oldConfig.PintoTop == newConfig.PintoTop &&
                   oldConfig.Background == newConfig.Background &&
                   oldConfig.RequestHeaderFiltering == newConfig.RequestHeaderFiltering &&
                   oldConfig.RequireUserAgent == newConfig.RequireUserAgent &&
                   oldConfig.RequireAccept == newConfig.RequireAccept &&
                   oldConfig.AllowedBrowsers == newConfig.AllowedBrowsers &&
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
                RequestThrottling = true,
                MaximumRequestsPerSecond = 50,
                AlarmRequestsPerSecond = 30,
                BlockedIPs = "",
                TemporarilyBlockedIPs = "",
                BlockDurationMinutes = 30,
                IPBlocking = false,
                IPBlockingList = "127.0.0.1:8000",
                Blacklist = "",
                GenerateHelp = true,
                PintoTop = true,
                Background = false,
                RequestHeaderFiltering = false,
                RequireUserAgent = true,
                RequireAccept = true,
                AllowedBrowsers = "Chrome,Firefox,Safari,Edg,Opera,OPR,MSIE,Trident",
                UploadEnabled = false,
                MaxUploadSizeMB = 50,
                MaxDownloadSizeMB = 100,
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
            
            GenerateHelpFile(defaultConfig);
            
            return defaultConfig;
        }

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
                    if (!File.Exists(_configPath))
                    {
                        _config = CreateDefaultConfig();
                        SaveConfig(_config);
                    }

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
