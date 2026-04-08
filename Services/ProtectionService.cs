using System.Collections.Concurrent;
using System.Text.Json;
using Repository.Models;
using Repository.Services;

public class ProtectionService
{
    private readonly ConfigManager _configManager;
    private readonly Logger _logger;
    private readonly KeyManagementService _keyManagementService;
    private readonly SecureSessionService _sessionService;
    private readonly ConcurrentDictionary<string, ProtectionLock> _protectionLocks = new();
    private readonly ConcurrentDictionary<string, string> _protectedPaths = new();
    private readonly ConcurrentDictionary<string, SecureServerHandler> _secureHandlers = new();
    private FileSystemWatcher? _watcher;
    private string? _repositoryPath;
    private bool _protectEnabled = true;

    public ProtectionService(ConfigManager configManager, Logger logger, KeyManagementService keyManagementService, SecureSessionService sessionService)
    {
        _configManager = configManager;
        _logger = logger;
        _keyManagementService = keyManagementService;
        _sessionService = sessionService;
    }

    public void StartWatching(string repositoryPath)
    {
        var config = _configManager.GetConfig();
        _protectEnabled = config.ProtectEnabled;
        
        if (!_protectEnabled)
        {
            _logger.LogInfo(I18nService.Instance.T("protection.disabled"));
            return;
        }
        
        _repositoryPath = repositoryPath;
        
        ScanProtectionLocks();
        
        _watcher = new FileSystemWatcher(repositoryPath)
        {
            IncludeSubdirectories = true,
            Filter = "Protectionlock.json",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        
        _watcher.Created += OnProtectionLockChanged;
        _watcher.Changed += OnProtectionLockChanged;
        _watcher.Deleted += OnProtectionLockDeleted;
        _watcher.Renamed += OnProtectionLockRenamed;
        
        _watcher.EnableRaisingEvents = true;
        _logger.LogInfo(I18nService.Instance.T("protection.started"));
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        
        foreach (var handler in _secureHandlers.Values)
        {
            handler?.Dispose();
        }
        _secureHandlers.Clear();
        
        _logger.LogInfo(I18nService.Instance.T("protection.stopped"));
    }

    private void ScanProtectionLocks()
    {
        if (string.IsNullOrEmpty(_repositoryPath) || !Directory.Exists(_repositoryPath))
        {
            return;
        }

        try
        {
            var lockFiles = Directory.GetFiles(_repositoryPath, "Protectionlock.json", SearchOption.AllDirectories);
            
            foreach (var lockFile in lockFiles)
            {
                LoadProtectionLock(lockFile);
            }
            
            _logger.LogInfo(I18nService.Instance.T("protection.scanned", lockFiles.Length));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(I18nService.Instance.T("protection.scan_error", ex.Message));
        }
    }

    private void LoadProtectionLock(string lockFilePath)
    {
        try
        {
            var json = File.ReadAllText(lockFilePath);
            var protectionLock = JsonSerializer.Deserialize<ProtectionLock>(json);
            
            if (protectionLock != null)
            {
                protectionLock.UpdateHash();
                
                var directory = Path.GetDirectoryName(lockFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    var relativePath = GetRelativePath(directory);
                    var normalizedPath = NormalizePath(relativePath);
                    
                    _protectionLocks[normalizedPath] = protectionLock;
                    _protectedPaths[normalizedPath] = lockFilePath;
                    
                    if (protectionLock.AuthMethod == "token")
                    {
                        if (!string.IsNullOrEmpty(protectionLock.Token) && string.IsNullOrEmpty(protectionLock.TokenHash))
                        {
                            var updatedJson = JsonSerializer.Serialize(protectionLock, new JsonSerializerOptions 
                            { 
                                WriteIndented = true 
                            });
                            File.WriteAllText(lockFilePath, updatedJson);
                            _logger.LogInfo(I18nService.Instance.T("protection.hash_updated", normalizedPath));
                        }
                    }
                    else if (protectionLock.IsSecureAuth())
                    {
                        RegisterSecureClient(protectionLock, normalizedPath);
                    }
                    
                    _logger.LogInfo(I18nService.Instance.T("protection.lock_loaded", normalizedPath, protectionLock.AuthMethod));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(I18nService.Instance.T("protection.lock_load_error", lockFilePath, ex.Message));
        }
    }

    private void RegisterSecureClient(ProtectionLock protectionLock, string normalizedPath)
    {
        if (string.IsNullOrEmpty(protectionLock.ClientId) || string.IsNullOrEmpty(protectionLock.SharedToken))
        {
            _logger.LogWarning(I18nService.Instance.T("protection.invalid_secure_config", normalizedPath));
            return;
        }

        if (!_keyManagementService.HasClientKey(protectionLock.ClientId))
        {
            _logger.LogWarning(I18nService.Instance.T("protection.client_not_registered", protectionLock.ClientId));
            _keyManagementService.SetSharedToken(protectionLock.ClientId, protectionLock.SharedToken);
            return;
        }

        _keyManagementService.SetSharedToken(protectionLock.ClientId, protectionLock.SharedToken);
        
        var handler = _keyManagementService.CreateSecureServerHandler(protectionLock.ClientId);
        if (handler != null)
        {
            _secureHandlers[normalizedPath] = handler;
            _logger.LogInfo(I18nService.Instance.T("protection.handler_created", normalizedPath));
        }
    }

    private void OnProtectionLockChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogInfo(I18nService.Instance.T("protection.lock_changed", e.FullPath));
        LoadProtectionLock(e.FullPath);
    }

    private void OnProtectionLockDeleted(object sender, FileSystemEventArgs e)
    {
        var directory = Path.GetDirectoryName(e.FullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            var relativePath = GetRelativePath(directory);
            var normalizedPath = NormalizePath(relativePath);
            
            _protectionLocks.TryRemove(normalizedPath, out _);
            _protectedPaths.TryRemove(normalizedPath, out _);
            
            if (_secureHandlers.TryRemove(normalizedPath, out var handler))
            {
                handler?.Dispose();
            }
            
            _logger.LogInfo(I18nService.Instance.T("protection.lock_removed", normalizedPath));
        }
    }

    private void OnProtectionLockRenamed(object sender, RenamedEventArgs e)
    {
        OnProtectionLockDeleted(sender, new FileSystemEventArgs(WatcherChangeTypes.Deleted, 
            Path.GetDirectoryName(e.OldFullPath) ?? "", 
            e.OldName ?? ""));
        
        if (e.Name?.Equals("Protectionlock.json", StringComparison.OrdinalIgnoreCase) == true)
        {
            LoadProtectionLock(e.FullPath);
        }
    }

    public bool IsPathProtected(string path)
    {
        if (!_protectEnabled)
        {
            return false;
        }
        
        var normalizedPath = NormalizePath(path);
        
        foreach (var protectedPath in _protectionLocks.Keys)
        {
            if (normalizedPath.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath, protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        
        return false;
    }

    public string? GetAuthMethod(string path)
    {
        var normalizedPath = NormalizePath(path);
        
        foreach (var kvp in _protectionLocks)
        {
            var protectedPath = kvp.Key;
            var protectionLock = kvp.Value;
            
            if (normalizedPath.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath, protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return protectionLock.AuthMethod;
            }
        }
        
        return null;
    }

    public bool VerifyToken(string path, string? providedToken)
    {
        var normalizedPath = NormalizePath(path);
        
        foreach (var kvp in _protectionLocks)
        {
            var protectedPath = kvp.Key;
            var protectionLock = kvp.Value;
            
            if (normalizedPath.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath, protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                if (protectionLock.AuthMethod == "secure")
                {
                    return false;
                }
                return protectionLock.VerifyToken(providedToken);
            }
        }
        
        return true;
    }

    public bool VerifySession(string path, string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return false;
        }
        
        return _sessionService.ValidateSessionForPath(sessionId, path);
    }

    public async Task<(bool success, string? sessionId, byte[]? response)> VerifySecureRequestAsync(string path, byte[] encryptedRequest)
    {
        var normalizedPath = NormalizePath(path);
        
        foreach (var kvp in _secureHandlers)
        {
            var protectedPath = kvp.Key;
            var handler = kvp.Value;
            var protectionLock = _protectionLocks.GetValueOrDefault(protectedPath);
            
            if (normalizedPath.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath, protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                var (success, response) = await handler.HandleRequestAsync(encryptedRequest);
                
                if (success && protectionLock != null)
                {
                    var session = _sessionService.CreateSession(
                        protectionLock.ClientId,
                        protectedPath,
                        Array.Empty<byte>()
                    );
                    
                    return (true, session.SessionId, response);
                }
                
                return (success, null, response);
            }
        }
        
        return (false, null, null);
    }

    public bool HasSecureHandler(string path)
    {
        var normalizedPath = NormalizePath(path);
        
        foreach (var protectedPath in _secureHandlers.Keys)
        {
            if (normalizedPath.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath, protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        
        return false;
    }

    public string? GetProtectedParentPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        
        foreach (var protectedPath in _protectionLocks.Keys)
        {
            if (normalizedPath.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedPath, protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return protectedPath;
            }
        }
        
        return null;
    }

    private string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            return fullPath;
        }
        
        var repoFullPath = Path.GetFullPath(_repositoryPath);
        var fullFilePath = Path.GetFullPath(fullPath);
        
        if (fullFilePath.StartsWith(repoFullPath, StringComparison.OrdinalIgnoreCase))
        {
            var relative = fullFilePath.Substring(repoFullPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative;
        }
        
        return fullPath;
    }

    private string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim('/').TrimEnd('/');
    }

    public Dictionary<string, ProtectionLock> GetAllProtectionLocks()
    {
        return new Dictionary<string, ProtectionLock>(_protectionLocks);
    }
    
    public void RefreshSecureHandlerForClient(string clientId)
    {
        foreach (var kvp in _protectionLocks)
        {
            var protectedPath = kvp.Key;
            var protectionLock = kvp.Value;
            
            if (protectionLock.IsSecureAuth() && protectionLock.ClientId == clientId)
            {
                RegisterSecureClient(protectionLock, protectedPath);
            }
        }
    }
    
    public static bool IsSystemPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        
        var normalizedPath = path.Replace('\\', '/').Trim('/');
        var segments = normalizedPath.Split('/');
        
        var systemDirs = new[] { ".keys" };
        
        foreach (var segment in segments)
        {
            if (systemDirs.Any(sd => string.Equals(segment, sd, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        
        return false;
    }
}
