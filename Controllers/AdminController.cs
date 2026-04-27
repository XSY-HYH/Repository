using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using Repository.Models;
using Repository.Services;

namespace Repository.Controllers
{
    public class AdminController : Controller
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly ChapAuthService _chapAuthService;
        private readonly BlacklistService _blacklistService;
        private readonly ProtectionService _protectionService;
        private readonly AdminConnectionManager _connectionManager;

        public AdminController(ConfigManager configManager, Logger logger, ChapAuthService chapAuthService, BlacklistService blacklistService, ProtectionService protectionService, AdminConnectionManager connectionManager)
        {
            _configManager = configManager;
            _logger = logger;
            _chapAuthService = chapAuthService;
            _blacklistService = blacklistService;
            _protectionService = protectionService;
            _connectionManager = connectionManager;
        }

        [HttpGet("/admin")]
        public IActionResult AdminPage()
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return NotFound(I18nService.Instance.T("admin.not_enabled"));
            }

            var html = AdminHtml.GetHtml();
            return Content(html, "text/html", Encoding.UTF8);
        }

        [HttpGet("/admin.css")]
        public IActionResult GetAdminCss()
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return NotFound();
            }

            var css = AdminCss.GetCss();
            return Content(css, "text/css", Encoding.UTF8);
        }

        [HttpGet("/admin.js")]
        public IActionResult GetAdminJs()
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return NotFound();
            }

            var js = AdminJs.GetJs();
            return Content(js, "application/javascript", Encoding.UTF8);
        }

        [Route("/admin/ws")]
        public async Task<IActionResult> WebSocketEndpoint()
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return BadRequest(I18nService.Instance.T("admin.not_enabled"));
            }

            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                return BadRequest(I18nService.Instance.T("admin.websocket_required"));
            }

            var clientIP = GetClientIP();
            _logger.LogInfo(I18nService.Instance.T("admin.ws_connected", clientIP));

            WebSocket webSocket;
            try
            {
                webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("admin.ws_accept_failed"));
                return BadRequest(I18nService.Instance.T("admin.websocket_failed"));
            }

            try
            {
                await HandleWebSocketConnection(webSocket, clientIP);
            }
            catch (WebSocketException ex)
            {
                if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
                {
                    _logger.LogError(ex, I18nService.Instance.T("admin.ws_error", clientIP));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("admin.ws_handler_error", clientIP));
            }

            return new EmptyResult();
        }

        private async Task HandleWebSocketConnection(WebSocket webSocket, string clientIP)
        {
            var buffer = new byte[8192];
            byte[]? sessionKey = null;
            string? sessionId = null;
            string? connectionId = null;

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogInfo(I18nService.Instance.T("admin.ws_closed", clientIP));
                            try
                            {
                                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                                {
                                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Close", CancellationToken.None);
                                }
                            }
                            catch (WebSocketException)
                            {
                            }
                            break;
                        }

                        if (result.MessageType != WebSocketMessageType.Binary)
                        {
                            await SendError(webSocket, I18nService.Instance.T("admin.binary_only"));
                            continue;
                        }

                        var data = new byte[result.Count];
                        Buffer.BlockCopy(buffer, 0, data, 0, result.Count);

                        if (sessionKey == null)
                        {
                            var (success, response, key) = _chapAuthService.HandleLogin(data, clientIP);
                            if (success && key != null)
                            {
                                sessionKey = key;
                                sessionId = response.NewId;
                                connectionId = _connectionManager.RegisterConnection(webSocket, sessionKey);
                            }
                            await SendResponse(webSocket, sessionKey ?? _chapAuthService.GetKeyFromPassword("invalid"), response);
                        }
                        else
                        {
                            var (success, response, key) = _chapAuthService.HandleOperation(data, clientIP);
                            if (success && key != null)
                            {
                                sessionKey = key;
                                if (connectionId != null)
                                {
                                    _connectionManager.UpdateSessionKey(connectionId, sessionKey);
                                }
                            }
                            if (success && response.Data is ChapOperation operation)
                            {
                                var operationResult = await ExecuteOperation(operation, clientIP);
                                response.Message = operationResult.Message;
                                response.Data = operationResult.Data;
                            }
                            await SendResponse(webSocket, sessionKey!, response);
                        }
                    }
                    catch (WebSocketException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, I18nService.Instance.T("admin.ws_message_error", clientIP));
                        await SendError(webSocket, I18nService.Instance.T("admin.internal_error"));
                    }
                }
            }
            finally
            {
                if (connectionId != null)
                {
                    _connectionManager.RemoveConnection(connectionId);
                }
            }
        }

        private async Task SendResponse(WebSocket webSocket, byte[] key, ChapResponse response)
        {
            var json = JsonSerializer.Serialize(response);
            var encrypted = _chapAuthService.Encrypt(key, json);
            await webSocket.SendAsync(new ArraySegment<byte>(encrypted), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        private async Task SendError(WebSocket webSocket, string message)
        {
            var config = _configManager.GetConfig();
            var key = _chapAuthService.GetKeyFromPassword(config.AdminPassword ?? "default_key");
            var response = new ChapResponse { Success = false, Message = message };
            await SendResponse(webSocket, key, response);
        }

        private async Task<(string Message, object? Data)> ExecuteOperation(ChapOperation operation, string clientIP)
        {
            var config = _configManager.GetConfig();
            var repoPath = config.RepositoryPath;

            try
            {
                switch (operation.Action.ToLowerInvariant())
                {
                    case "delete_file":
                        return await DeleteFile(operation.Path, repoPath, clientIP);

                    case "delete_directory":
                        return await DeleteDirectory(operation.Path, repoPath, clientIP);

                    case "move_file":
                        return await MoveFile(operation.Path, operation.NewPath, repoPath, clientIP);

                    case "move_directory":
                        return await MoveDirectory(operation.Path, operation.NewPath, repoPath, clientIP);

                    case "create_directory":
                        return await CreateDirectory(operation.Path, repoPath, clientIP);

                    case "list_directory":
                        return await ListDirectory(operation.Path, repoPath, clientIP);

                    case "get_logs":
                        return await GetLogFiles(clientIP);

                    case "get_log":
                        return await GetLogFile(operation.Path, clientIP);

                    case "get_settings":
                        return await GetServerSettings(clientIP);

                    case "update_settings":
                        return await UpdateServerSettings(operation.Settings, clientIP);

                    case "get_rate_limit_status":
                        return await GetRateLimitStatus(clientIP);

                    case "force_resume_rate_limit":
                        return await ForceResumeRateLimit(clientIP);

                    default:
                        return (I18nService.Instance.T("admin.unknown_action", operation.Action), null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("admin.operation_failed", ex.Message));
                return (I18nService.Instance.T("admin.operation_failed", ex.Message), null);
            }
        }

        private async Task<(string Message, object? Data)> DeleteFile(string? path, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(path))
            {
                return (I18nService.Instance.T("admin.path_empty"), null);
            }

            var fullPath = Path.Combine(repoPath, path.TrimStart('/'));

            if (!IsPathValid(repoPath, fullPath))
            {
                return (I18nService.Instance.T("admin.invalid_path"), null);
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return (I18nService.Instance.T("admin.file_not_exist"), null);
            }

            FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            _logger.LogInfo(I18nService.Instance.T("admin.file_recycled_log", clientIP, path));

            return (I18nService.Instance.T("admin.file_recycled"), null);
        }

        private async Task<(string Message, object? Data)> DeleteDirectory(string? path, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(path))
            {
                return (I18nService.Instance.T("admin.path_empty"), null);
            }

            var fullPath = Path.Combine(repoPath, path.TrimStart('/'));

            if (!IsPathValid(repoPath, fullPath))
            {
                return (I18nService.Instance.T("admin.invalid_path"), null);
            }

            if (!Directory.Exists(fullPath))
            {
                return (I18nService.Instance.T("admin.dir_not_exist"), null);
            }

            FileSystem.DeleteDirectory(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            _logger.LogInfo(I18nService.Instance.T("admin.dir_recycled_log", clientIP, path));

            return (I18nService.Instance.T("admin.dir_recycled"), null);
        }

        private async Task<(string Message, object? Data)> MoveFile(string? oldPath, string? newPath, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
            {
                return (I18nService.Instance.T("admin.path_empty"), null);
            }

            var fullOldPath = Path.Combine(repoPath, oldPath.TrimStart('/'));
            var fullNewPath = Path.Combine(repoPath, newPath.TrimStart('/'));

            if (!IsPathValid(repoPath, fullOldPath) || !IsPathValid(repoPath, fullNewPath))
            {
                return (I18nService.Instance.T("admin.invalid_path"), null);
            }

            if (!System.IO.File.Exists(fullOldPath))
            {
                return (I18nService.Instance.T("admin.source_file_not_exist"), null);
            }

            var newDir = Path.GetDirectoryName(fullNewPath);
            if (!string.IsNullOrEmpty(newDir) && !Directory.Exists(newDir))
            {
                Directory.CreateDirectory(newDir);
            }

            System.IO.File.Move(fullOldPath, fullNewPath);
            _logger.LogInfo(I18nService.Instance.T("admin.file_moved_log", clientIP, oldPath, newPath));

            return (I18nService.Instance.T("admin.file_moved"), null);
        }

        private async Task<(string Message, object? Data)> MoveDirectory(string? oldPath, string? newPath, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
            {
                return (I18nService.Instance.T("admin.path_empty"), null);
            }

            var fullOldPath = Path.Combine(repoPath, oldPath.TrimStart('/'));
            var fullNewPath = Path.Combine(repoPath, newPath.TrimStart('/'));

            if (!IsPathValid(repoPath, fullOldPath) || !IsPathValid(repoPath, fullNewPath))
            {
                return (I18nService.Instance.T("admin.invalid_path"), null);
            }

            if (!Directory.Exists(fullOldPath))
            {
                return (I18nService.Instance.T("admin.source_dir_not_exist"), null);
            }

            var newDir = Path.GetDirectoryName(fullNewPath);
            if (!string.IsNullOrEmpty(newDir) && !Directory.Exists(newDir))
            {
                Directory.CreateDirectory(newDir);
            }

            Directory.Move(fullOldPath, fullNewPath);
            _logger.LogInfo(I18nService.Instance.T("admin.dir_moved_log", clientIP, oldPath, newPath));

            return (I18nService.Instance.T("admin.dir_moved"), null);
        }

        private async Task<(string Message, object? Data)> CreateDirectory(string? path, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(path))
            {
                return (I18nService.Instance.T("admin.path_empty"), null);
            }

            var fullPath = Path.Combine(repoPath, path.TrimStart('/'));

            if (!IsPathValid(repoPath, fullPath))
            {
                return (I18nService.Instance.T("admin.invalid_path"), null);
            }

            if (Directory.Exists(fullPath))
            {
                return (I18nService.Instance.T("admin.dir_exists"), null);
            }

            Directory.CreateDirectory(fullPath);
            _logger.LogInfo(I18nService.Instance.T("admin.dir_created_log", clientIP, path));

            return (I18nService.Instance.T("admin.dir_created"), null);
        }

        private async Task<(string Message, object? Data)> ListDirectory(string? path, string repoPath, string clientIP)
        {
            string targetPath;
            if (string.IsNullOrEmpty(path))
            {
                targetPath = repoPath;
            }
            else
            {
                var normalizedPath = path.TrimStart('/');
                targetPath = Path.Combine(repoPath, normalizedPath);
            }

            if (!IsPathValid(repoPath, targetPath))
            {
                return (I18nService.Instance.T("admin.invalid_path"), null);
            }

            if (!Directory.Exists(targetPath))
            {
                return (I18nService.Instance.T("admin.dir_not_exist"), null);
            }

            var directories = Directory.GetDirectories(targetPath)
                .Select(d => new
                {
                    name = Path.GetFileName(d),
                    path = GetRelativePath(repoPath, d).TrimStart('/'),
                    type = "directory"
                })
                .ToList();

            var files = Directory.GetFiles(targetPath)
                .Select(f => new
                {
                    name = Path.GetFileName(f),
                    path = GetRelativePath(repoPath, f).TrimStart('/'),
                    type = "file",
                    size = new System.IO.FileInfo(f).Length
                })
                .ToList();

            return (I18nService.Instance.T("admin.dir_list_success"), new { directories, files });
        }

        private string GetClientIP()
        {
            try
            {
                var forwardedFor = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(forwardedFor))
                {
                    return forwardedFor.Split(',').First().Trim();
                }

                var realIP = HttpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(realIP))
                {
                    return realIP;
                }

                return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private bool IsPathValid(string basePath, string? fullPath)
        {
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(fullPath))
            {
                return false;
            }

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

        private string GetRelativePath(string basePath, string fullPath)
        {
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

        private async Task<(string Message, object? Data)> GetLogFiles(string clientIP)
        {
            _logger.LogInfo(I18nService.Instance.T("admin.log_list_requested", clientIP));

            try
            {
                var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");

                if (!Directory.Exists(logDirectory))
                {
                    return (I18nService.Instance.T("admin.log_list_success"), new { files = new List<object>() });
                }

                var logFiles = Directory.GetFiles(logDirectory, "*.log")
                    .Select(file => new System.IO.FileInfo(file))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select(f => new
                    {
                        name = f.Name,
                        size = FormatFileSize(f.Length),
                        lastModified = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToList();

                return (I18nService.Instance.T("admin.log_list_success"), new { files = logFiles });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("admin.log_list_failed"));
                return (I18nService.Instance.T("admin.log_list_failed"), null);
            }
        }

        private async Task<(string Message, object? Data)> GetLogFile(string? logFile, string clientIP)
        {
            if (string.IsNullOrEmpty(logFile))
            {
                return (I18nService.Instance.T("admin.log_name_empty"), null);
            }

            _logger.LogInfo(I18nService.Instance.T("admin.log_requested", clientIP, logFile));

            try
            {
                if (ContainsIllegalCharacters(logFile))
                {
                    return (I18nService.Instance.T("admin.log_name_invalid"), null);
                }

                if (!logFile.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                {
                    return (I18nService.Instance.T("admin.log_only"), null);
                }

                var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                var logFilePath = Path.Combine(logDirectory, logFile);

                if (!IsPathValid(logDirectory, logFilePath))
                {
                    return (I18nService.Instance.T("admin.log_path_invalid"), null);
                }

                if (!System.IO.File.Exists(logFilePath))
                {
                    return (I18nService.Instance.T("admin.log_not_exist"), null);
                }

                var fileInfo = new System.IO.FileInfo(logFilePath);
                if (fileInfo.Length > 10 * 1024 * 1024)
                {
                    return (I18nService.Instance.T("admin.log_too_large"), null);
                }

                var logContent = System.IO.File.ReadAllText(logFilePath);
                return (I18nService.Instance.T("admin.log_get_success"), new { content = logContent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("admin.log_get_failed"));
                return (I18nService.Instance.T("admin.log_get_failed"), null);
            }
        }

        private bool ContainsIllegalCharacters(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var illegalPatterns = new[]
            {
                "..", "~", "$", "&", "|", ";", "<", ">", "\"", "'", "`", "\n", "\r", "\t"
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

        [HttpPost("/admin/api/upload")]
        public async Task<IActionResult> UploadFile(IFormFile? file, [FromQuery] string? path)
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return NotFound(I18nService.Instance.T("admin.not_enabled"));
            }

            var clientIP = GetClientIP();
            _logger.LogInfo(I18nService.Instance.T("admin.upload_requested", clientIP, path ?? "", file?.FileName ?? ""));

            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(I18nService.Instance.T("admin.upload_select"));
                }

                if (ContainsIllegalCharacters(file.FileName))
                {
                    return BadRequest(I18nService.Instance.T("admin.upload_name_invalid"));
                }

                var repoPath = config.RepositoryPath;
                var targetFolder = repoPath;

                if (!string.IsNullOrEmpty(path))
                {
                    targetFolder = Path.Combine(repoPath, path.TrimStart('/'));
                }

                if (!IsPathValid(repoPath, targetFolder))
                {
                    return BadRequest(I18nService.Instance.T("admin.upload_path_invalid"));
                }

                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                var filePath = Path.Combine(targetFolder, file.FileName);

                if (!IsPathValid(repoPath, filePath))
                {
                    return BadRequest(I18nService.Instance.T("admin.upload_file_path_invalid"));
                }

                var maxUploadSizeBytes = config.MaxUploadSizeMB * 1024 * 1024;
                if (file.Length > maxUploadSizeBytes)
                {
                    return StatusCode(413, I18nService.Instance.T("admin.upload_too_large", FormatFileSize(maxUploadSizeBytes)));
                }

                if (config.MaxDiskUsagePercent > 0)
                {
                    var driveInfo = new DriveInfo(repoPath);
                    var totalBytes = driveInfo.TotalSize;
                    var usedBytes = totalBytes - driveInfo.AvailableFreeSpace;
                    var projectedUsedBytes = usedBytes + file.Length;
                    var projectedUsagePercent = (double)projectedUsedBytes / totalBytes * 100;

                    if (projectedUsagePercent > config.MaxDiskUsagePercent)
                    {
                        return StatusCode(507, I18nService.Instance.T("admin.upload_disk_full"));
                    }
                }

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                _logger.LogInfo(I18nService.Instance.T("admin.upload_success_log", clientIP, filePath));

                return Ok(new
                {
                    message = I18nService.Instance.T("admin.upload_success"),
                    fileName = file.FileName,
                    fileSize = FormatFileSize(file.Length)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("admin.upload_failed"));
                return StatusCode(500, I18nService.Instance.T("admin.internal_error"));
            }
        }

        private async Task<(string Message, object? Data)> GetServerSettings(string clientIP)
        {
            _logger.LogInfo(I18nService.Instance.T("admin.settings_requested", clientIP));

            var config = _configManager.GetConfig();

            return (I18nService.Instance.T("admin.settings_get_success"), new
            {
                settings = new
                {
                    RequestThrottling = config.RequestThrottling,
                    MaximumRequestsPerSecond = config.MaximumRequestsPerSecond,
                    AlarmRequestsPerSecond = config.AlarmRequestsPerSecond,
                    BlockDurationMinutes = config.BlockDurationMinutes,
                    IPBlocking = config.IPBlocking,
                    UploadEnabled = config.UploadEnabled,
                    MaxUploadSizeMB = config.MaxUploadSizeMB,
                    MaxDiskUsagePercent = config.MaxDiskUsagePercent,
                    AllowOverwrite = config.AllowOverwrite,
                    AllowRootUpload = config.AllowRootUpload,
                    PreviewEnabled = config.PreviewEnabled,
                    MaxDownloadSizeMB = config.MaxDownloadSizeMB,
                    HttpEnabled = config.HttpEnabled,
                    HttpsEnabled = config.HttpsEnabled,
                    HttpsRedirectEnabled = config.HttpsRedirectEnabled,
                    Notification = config.Notification,
                    AutoRestart = config.AutoRestart,
                    MaxRestartAttempts = config.MaxRestartAttempts
                }
            });
        }

        private async Task<(string Message, object? Data)> UpdateServerSettings(Dictionary<string, object?>? settings, string clientIP)
        {
            if (settings == null || settings.Count == 0)
            {
                return (I18nService.Instance.T("admin.settings_empty"), null);
            }

            _logger.LogInfo(I18nService.Instance.T("admin.settings_update_log", clientIP));

            try
            {
                var config = _configManager.GetConfig();
                var updatedFields = new List<string>();

                foreach (var kvp in settings)
                {
                    switch (kvp.Key)
                    {
                        case "RequestThrottling":
                            if (kvp.Value is bool throttlingValue)
                            {
                                config.RequestThrottling = throttlingValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_throttling"));
                            }
                            break;

                        case "MaximumRequestsPerSecond":
                            if (int.TryParse(kvp.Value?.ToString(), out var maxReq))
                            {
                                config.MaximumRequestsPerSecond = maxReq;
                                updatedFields.Add(I18nService.Instance.T("admin.field_max_requests"));
                            }
                            break;

                        case "AlarmRequestsPerSecond":
                            if (int.TryParse(kvp.Value?.ToString(), out var alarmReq))
                            {
                                config.AlarmRequestsPerSecond = alarmReq;
                                updatedFields.Add(I18nService.Instance.T("admin.field_alarm_requests"));
                            }
                            break;

                        case "BlockDurationMinutes":
                            if (int.TryParse(kvp.Value?.ToString(), out var blockDur))
                            {
                                config.BlockDurationMinutes = blockDur;
                                updatedFields.Add(I18nService.Instance.T("admin.field_block_duration"));
                            }
                            break;

                        case "IPBlocking":
                            if (kvp.Value is bool ipBlockingValue)
                            {
                                config.IPBlocking = ipBlockingValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_ip_blocking"));
                            }
                            break;

                        case "UploadEnabled":
                            if (kvp.Value is bool uploadValue)
                            {
                                config.UploadEnabled = uploadValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_upload"));
                            }
                            break;

                        case "MaxUploadSizeMB":
                            if (int.TryParse(kvp.Value?.ToString(), out var maxSize))
                            {
                                config.MaxUploadSizeMB = maxSize;
                                updatedFields.Add(I18nService.Instance.T("admin.field_max_upload_size"));
                            }
                            break;

                        case "MaxDiskUsagePercent":
                            if (int.TryParse(kvp.Value?.ToString(), out var diskPercent))
                            {
                                config.MaxDiskUsagePercent = diskPercent;
                                updatedFields.Add(I18nService.Instance.T("admin.field_max_disk_usage"));
                            }
                            break;

                        case "AllowOverwrite":
                            if (kvp.Value is bool overwriteValue)
                            {
                                config.AllowOverwrite = overwriteValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_allow_overwrite"));
                            }
                            break;

                        case "AllowRootUpload":
                            if (kvp.Value is bool rootUploadValue)
                            {
                                config.AllowRootUpload = rootUploadValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_allow_root_upload"));
                            }
                            break;

                        case "PreviewEnabled":
                            if (kvp.Value is bool previewValue)
                            {
                                config.PreviewEnabled = previewValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_preview"));
                            }
                            break;

                        case "MaxDownloadSizeMB":
                            if (int.TryParse(kvp.Value?.ToString(), out var maxDownload))
                            {
                                config.MaxDownloadSizeMB = maxDownload;
                                updatedFields.Add(I18nService.Instance.T("admin.field_max_download_size"));
                            }
                            break;

                        case "HttpEnabled":
                            if (kvp.Value is bool httpValue)
                            {
                                config.HttpEnabled = httpValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_http"));
                            }
                            break;

                        case "HttpsEnabled":
                            if (kvp.Value is bool httpsValue)
                            {
                                config.HttpsEnabled = httpsValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_https"));
                            }
                            break;

                        case "HttpsRedirectEnabled":
                            if (kvp.Value is bool httpsRedirectValue)
                            {
                                config.HttpsRedirectEnabled = httpsRedirectValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_https_redirect"));
                            }
                            break;

                        case "Notification":
                            if (kvp.Value is bool notifyValue)
                            {
                                config.Notification = notifyValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_notification"));
                            }
                            break;

                        case "AutoRestart":
                            if (kvp.Value is bool restartValue)
                            {
                                config.AutoRestart = restartValue;
                                updatedFields.Add(I18nService.Instance.T("admin.field_auto_restart"));
                            }
                            break;

                        case "MaxRestartAttempts":
                            if (int.TryParse(kvp.Value?.ToString(), out var maxAttempts))
                            {
                                config.MaxRestartAttempts = maxAttempts;
                                updatedFields.Add(I18nService.Instance.T("admin.field_max_restart_attempts"));
                            }
                            break;
                    }
                }

                _configManager.SaveConfig(config);
                _logger.LogInfo(I18nService.Instance.T("admin.settings_updated_log", string.Join(", ", updatedFields)));

                return (I18nService.Instance.T("admin.settings_updated", string.Join(", ", updatedFields)), new { updatedFields });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("admin.settings_update_failed", ex.Message));
                return (I18nService.Instance.T("admin.settings_update_failed", ex.Message), null);
            }
        }

        private async Task<(string Message, object? Data)> GetRateLimitStatus(string clientIP)
        {
            return (I18nService.Instance.T("admin.rate_limit_status_success"), new
            {
                message = I18nService.Instance.T("admin.rate_limit_not_implemented")
            });
        }

        private async Task<(string Message, object? Data)> ForceResumeRateLimit(string clientIP)
        {
            _logger.LogInfo(I18nService.Instance.T("admin.rate_limit_resume_log", clientIP));

            return (I18nService.Instance.T("admin.rate_limit_not_implemented"), null);
        }
    }
}
