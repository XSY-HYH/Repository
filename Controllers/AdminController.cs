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
        public async Task<IActionResult> AdminPage()
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return NotFound("管理功能未启用");
            }

            var html = GetAdminHtml();
            return Content(html, "text/html", Encoding.UTF8);
        }

        [HttpGet("/admin.js")]
        public IActionResult AdminJs()
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return NotFound();
            }

            var js = GetAdminJs();
            return Content(js, "application/javascript", Encoding.UTF8);
        }

        [Route("/admin/ws")]
        public async Task<IActionResult> WebSocketEndpoint()
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return BadRequest("管理功能未启用");
            }

            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                return BadRequest("需要WebSocket连接");
            }

            var clientIP = GetClientIP();
            _logger.LogInfo($"管理员WebSocket连接建立，客户端IP: {clientIP}");

            WebSocket webSocket;
            try
            {
                webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AcceptWebSocketAsync失败");
                return BadRequest("WebSocket握手失败");
            }

            try
            {
                await HandleWebSocketConnection(webSocket, clientIP);
            }
            catch (WebSocketException ex)
            {
                if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
                {
                    _logger.LogError(ex, $"WebSocket错误，客户端IP: {clientIP}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"WebSocket处理错误，客户端IP: {clientIP}");
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
                            _logger.LogInfo($"管理员WebSocket连接关闭，客户端IP: {clientIP}");
                            try
                            {
                                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                                {
                                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "关闭连接", CancellationToken.None);
                                }
                            }
                            catch (WebSocketException)
                            {
                            }
                            break;
                        }

                        if (result.MessageType != WebSocketMessageType.Binary)
                        {
                            await SendError(webSocket, "只支持二进制消息");
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
                        _logger.LogError(ex, $"处理WebSocket消息错误，客户端IP: {clientIP}");
                        await SendError(webSocket, "服务器内部错误");
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

                    default:
                        return ($"未知操作: {operation.Action}", null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"执行操作失败，客户端IP: {clientIP}，操作: {operation.Action}");
                return ($"操作失败: {ex.Message}", null);
            }
        }

        private async Task<(string Message, object? Data)> DeleteFile(string? path, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(path))
            {
                return ("路径不能为空", null);
            }

            var fullPath = Path.Combine(repoPath, path.TrimStart('/'));

            if (!IsPathValid(repoPath, fullPath))
            {
                return ("无效的路径", null);
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return ("文件不存在", null);
            }

            FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            _logger.LogInfo($"文件已移至回收站，客户端IP: {clientIP}，路径: {path}");

            return ("文件已移至回收站", null);
        }

        private async Task<(string Message, object? Data)> DeleteDirectory(string? path, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(path))
            {
                return ("路径不能为空", null);
            }

            var fullPath = Path.Combine(repoPath, path.TrimStart('/'));

            if (!IsPathValid(repoPath, fullPath))
            {
                return ("无效的路径", null);
            }

            if (!Directory.Exists(fullPath))
            {
                return ("目录不存在", null);
            }

            FileSystem.DeleteDirectory(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            _logger.LogInfo($"目录已移至回收站，客户端IP: {clientIP}，路径: {path}");

            return ("目录已移至回收站", null);
        }

        private async Task<(string Message, object? Data)> MoveFile(string? oldPath, string? newPath, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
            {
                return ("路径不能为空", null);
            }

            var fullOldPath = Path.Combine(repoPath, oldPath.TrimStart('/'));
            var fullNewPath = Path.Combine(repoPath, newPath.TrimStart('/'));

            if (!IsPathValid(repoPath, fullOldPath) || !IsPathValid(repoPath, fullNewPath))
            {
                return ("无效的路径", null);
            }

            if (!System.IO.File.Exists(fullOldPath))
            {
                return ("源文件不存在", null);
            }

            var newDir = Path.GetDirectoryName(fullNewPath);
            if (!string.IsNullOrEmpty(newDir) && !Directory.Exists(newDir))
            {
                Directory.CreateDirectory(newDir);
            }

            System.IO.File.Move(fullOldPath, fullNewPath);
            _logger.LogInfo($"移动文件成功，客户端IP: {clientIP}，从 {oldPath} 到 {newPath}");

            return ("文件移动成功", null);
        }

        private async Task<(string Message, object? Data)> MoveDirectory(string? oldPath, string? newPath, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
            {
                return ("路径不能为空", null);
            }

            var fullOldPath = Path.Combine(repoPath, oldPath.TrimStart('/'));
            var fullNewPath = Path.Combine(repoPath, newPath.TrimStart('/'));

            if (!IsPathValid(repoPath, fullOldPath) || !IsPathValid(repoPath, fullNewPath))
            {
                return ("无效的路径", null);
            }

            if (!Directory.Exists(fullOldPath))
            {
                return ("源目录不存在", null);
            }

            var newDir = Path.GetDirectoryName(fullNewPath);
            if (!string.IsNullOrEmpty(newDir) && !Directory.Exists(newDir))
            {
                Directory.CreateDirectory(newDir);
            }

            Directory.Move(fullOldPath, fullNewPath);
            _logger.LogInfo($"移动目录成功，客户端IP: {clientIP}，从 {oldPath} 到 {newPath}");

            return ("目录移动成功", null);
        }

        private async Task<(string Message, object? Data)> CreateDirectory(string? path, string repoPath, string clientIP)
        {
            if (string.IsNullOrEmpty(path))
            {
                return ("路径不能为空", null);
            }

            var fullPath = Path.Combine(repoPath, path.TrimStart('/'));

            if (!IsPathValid(repoPath, fullPath))
            {
                return ("无效的路径", null);
            }

            if (Directory.Exists(fullPath))
            {
                return ("目录已存在", null);
            }

            Directory.CreateDirectory(fullPath);
            _logger.LogInfo($"创建目录成功，客户端IP: {clientIP}，路径: {path}");

            return ("目录创建成功", null);
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
                return ("无效的路径", null);
            }

            if (!Directory.Exists(targetPath))
            {
                return ("目录不存在", null);
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

            return ("获取目录列表成功", new { directories, files });
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
            _logger.LogInfo($"管理员请求日志文件列表，客户端IP: {clientIP}");

            try
            {
                var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");

                if (!Directory.Exists(logDirectory))
                {
                    return ("获取日志文件列表成功", new { files = new List<object>() });
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

                return ("获取日志文件列表成功", new { files = logFiles });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取日志文件列表失败");
                return ("获取日志文件列表失败", null);
            }
        }

        private async Task<(string Message, object? Data)> GetLogFile(string? logFile, string clientIP)
        {
            if (string.IsNullOrEmpty(logFile))
            {
                return ("日志文件名不能为空", null);
            }

            _logger.LogInfo($"管理员请求日志文件，客户端IP: {clientIP}，文件: {logFile}");

            try
            {
                if (ContainsIllegalCharacters(logFile))
                {
                    return ("日志文件名不合法", null);
                }

                if (!logFile.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                {
                    return ("只能访问日志文件", null);
                }

                var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                var logFilePath = Path.Combine(logDirectory, logFile);

                if (!IsPathValid(logDirectory, logFilePath))
                {
                    return ("日志文件路径不合法", null);
                }

                if (!System.IO.File.Exists(logFilePath))
                {
                    return ("日志文件不存在", null);
                }

                var fileInfo = new System.IO.FileInfo(logFilePath);
                if (fileInfo.Length > 10 * 1024 * 1024)
                {
                    return ("日志文件过大", null);
                }

                var logContent = System.IO.File.ReadAllText(logFilePath);
                return ("获取日志文件成功", new { content = logContent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取日志文件失败: {logFile}");
                return ("获取日志文件失败", null);
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
                return NotFound("管理功能未启用");
            }

            var clientIP = GetClientIP();
            _logger.LogInfo($"管理员上传文件，客户端IP: {clientIP}，路径: {path}，文件: {file?.FileName}");

            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest("请选择要上传的文件");
                }

                if (ContainsIllegalCharacters(file.FileName))
                {
                    return BadRequest("文件名不合法");
                }

                var repoPath = config.RepositoryPath;
                var targetFolder = repoPath;

                if (!string.IsNullOrEmpty(path))
                {
                    targetFolder = Path.Combine(repoPath, path.TrimStart('/'));
                }

                if (!IsPathValid(repoPath, targetFolder))
                {
                    return BadRequest("路径不合法");
                }

                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                var filePath = Path.Combine(targetFolder, file.FileName);

                if (!IsPathValid(repoPath, filePath))
                {
                    return BadRequest("文件路径不合法");
                }

                var maxUploadSizeBytes = config.MaxUploadSizeMB * 1024 * 1024;
                if (file.Length > maxUploadSizeBytes)
                {
                    return StatusCode(413, $"文件过大，最大允许 {FormatFileSize(maxUploadSizeBytes)}");
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
                        return StatusCode(507, $"磁盘占用超限");
                    }
                }

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                _logger.LogInfo($"文件上传成功，客户端IP: {clientIP}，路径: {filePath}");

                return Ok(new
                {
                    message = "上传成功",
                    fileName = file.FileName,
                    fileSize = FormatFileSize(file.Length)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "文件上传失败");
                return StatusCode(500, "服务器内部错误");
            }
        }

        private string GetAdminHtml()
        {
            return @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <link rel=""icon"" href=""/api/log"" type=""image/x-icon"">
    <title>Repository 管理</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;
            background-color: #f8f9fa;
            color: #212529;
            padding: 20px;
        }
        .login-container {
            max-width: 400px;
            margin: 100px auto;
            background: white;
            padding: 30px;
            border-radius: 8px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        }
        .login-container h2 { margin-bottom: 20px; text-align: center; }
        .form-group { margin-bottom: 15px; }
        .form-group label { display: block; margin-bottom: 5px; font-weight: 500; }
        .form-group input {
            width: 100%;
            padding: 10px;
            border: 1px solid #ced4da;
            border-radius: 4px;
            font-size: 14px;
        }
        .btn {
            width: 100%;
            padding: 10px;
            background: #007bff;
            color: white;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            font-size: 14px;
        }
        .btn:hover { background: #0069d9; }
        .btn:disabled { background: #6c757d; cursor: not-allowed; }
        .error { color: #dc3545; margin-top: 10px; text-align: center; }
        .admin-container { display: none; max-width: 1200px; margin: 0 auto; }
        .header {
            background: #343a40;
            color: white;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 20px;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .header h1 { font-size: 1.5rem; }
        .header-actions button {
            padding: 8px 16px;
            background: #dc3545;
            color: white;
            border: none;
            border-radius: 4px;
            cursor: pointer;
        }
        .content { background: white; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        table { width: 100%; border-collapse: collapse; }
        thead { background: #e9ecef; }
        th, td { padding: 12px 15px; text-align: left; border-bottom: 1px solid #dee2e6; }
        th { font-weight: 600; color: #495057; }
        tr:hover { background: #f8f9fa; }
        .dir { font-weight: 600; color: #007bff; }
        .file { color: #212529; }
        .actions { display: flex; gap: 5px; }
        .action-btn {
            padding: 4px 8px;
            border: none;
            border-radius: 3px;
            cursor: pointer;
            font-size: 12px;
        }
        .btn-delete { background: #dc3545; color: white; }
        .btn-move { background: #ffc107; color: #212529; }
        .btn-create { background: #28a745; color: white; }
        .breadcrumb { padding: 15px; background: #f8f9fa; border-bottom: 1px solid #dee2e6; }
        .breadcrumb a { color: #007bff; text-decoration: none; }
        .breadcrumb a:hover { text-decoration: underline; }
        .modal {
            display: none;
            position: fixed;
            z-index: 1000;
            left: 0;
            top: 0;
            width: 100%;
            height: 100%;
            background: rgba(0,0,0,0.5);
        }
        .modal-content {
            background: white;
            margin: 15% auto;
            padding: 20px;
            border-radius: 8px;
            width: 400px;
        }
        .modal-header { margin-bottom: 15px; }
        .modal-body { margin-bottom: 15px; }
        .modal-footer { text-align: right; }
        .modal-footer button { margin-left: 10px; padding: 8px 16px; border: none; border-radius: 4px; cursor: pointer; }
        .btn-cancel { background: #6c757d; color: white; }
        .btn-confirm { background: #007bff; color: white; }
    </style>
</head>
<body>
    <div class=""login-container"" id=""loginContainer"">
        <h2>管理员登录</h2>
        <div class=""form-group"">
            <label for=""username"">用户名</label>
            <input type=""text"" id=""username"" placeholder=""请输入用户名"">
        </div>
        <div class=""form-group"">
            <label for=""password"">密码</label>
            <input type=""password"" id=""password"" placeholder=""请输入密码"">
        </div>
        <button class=""btn"" id=""loginBtn"" onclick=""login()"">登录</button>
        <div class=""error"" id=""loginError""></div>
    </div>

    <div class=""admin-container"" id=""adminContainer"">
        <div class=""header"">
            <h1>Repository 管理</h1>
            <div class=""header-actions"">
                <button onclick=""showLogsModal()"" style=""background: #17a2b8; margin-right: 10px;"">查看日志</button>
                <button onclick=""logout()"">退出登录</button>
            </div>
        </div>
        <div class=""content"">
            <div class=""breadcrumb"" id=""breadcrumb"">
                <a href=""#"" onclick=""navigateTo('')"">根目录</a>
            </div>
            <div style=""padding: 15px; border-bottom: 1px solid #dee2e6;"">
                <button class=""action-btn btn-create"" onclick=""showCreateDirModal()"">新建目录</button>
                <button class=""action-btn"" style=""background: #17a2b8; color: white;"" onclick=""showUploadModal()"">上传文件</button>
                <input type=""file"" id=""fileInput"" style=""display: none;"" onchange=""handleFileSelect(event)"">
            </div>
            <table>
                <thead>
                    <tr>
                        <th>名称</th>
                        <th>大小</th>
                        <th>操作</th>
                    </tr>
                </thead>
                <tbody id=""fileList""></tbody>
            </table>
        </div>
    </div>

    <div class=""modal"" id=""moveModal"">
        <div class=""modal-content"">
            <div class=""modal-header""><h3>移动/重命名</h3></div>
            <div class=""modal-body"">
                <div class=""form-group"">
                    <label>新路径</label>
                    <input type=""text"" id=""newPathInput"" style=""width: 100%; padding: 8px;"">
                </div>
            </div>
            <div class=""modal-footer"">
                <button class=""btn-cancel"" onclick=""closeMoveModal()"">取消</button>
                <button class=""btn-confirm"" onclick=""confirmMove()"">确认</button>
            </div>
        </div>
    </div>

    <div class=""modal"" id=""createDirModal"">
        <div class=""modal-content"">
            <div class=""modal-header""><h3>新建目录</h3></div>
            <div class=""modal-body"">
                <div class=""form-group"">
                    <label>目录名称</label>
                    <input type=""text"" id=""dirNameInput"" style=""width: 100%; padding: 8px;"">
                </div>
            </div>
            <div class=""modal-footer"">
                <button class=""btn-cancel"" onclick=""closeCreateDirModal()"">取消</button>
                <button class=""btn-confirm"" onclick=""confirmCreateDir()"">确认</button>
            </div>
        </div>
    </div>

    <div class=""modal"" id=""logsModal"">
        <div class=""modal-content"" style=""width: 800px; max-height: 70vh; margin: 5vh auto; overflow: hidden; display: flex; flex-direction: column;"">
            <div class=""modal-header""><h3>日志文件</h3></div>
            <div class=""modal-body"" style=""flex: 1; overflow: auto;"">
                <div id=""logsList"">加载中...</div>
                <div id=""logContent"" style=""display: none; margin-top: 15px;"">
                    <div style=""margin-bottom: 10px;"">
                        <button class=""btn-cancel"" onclick=""backToLogsList()"">返回列表</button>
                        <span id=""currentLogFile"" style=""margin-left: 10px; font-weight: bold;""></span>
                    </div>
                    <pre id=""logText"" style=""background: #f8f9fa; padding: 15px; border-radius: 4px; overflow: auto; max-height: 50vh; white-space: pre-wrap; word-break: break-all; font-size: 12px;""></pre>
                </div>
            </div>
            <div class=""modal-footer"">
                <button class=""btn-cancel"" onclick=""closeLogsModal()"">关闭</button>
            </div>
        </div>
    </div>

    <script src=""/admin.js""></script>
</body>
</html>";
        }

        private string GetAdminJs()
        {
            return @"let ws = null;
let sessionId = null;
let key = null;
let currentPath = '';
let moveTargetPath = null;
let moveTargetType = null;

async function sha256(message) {
    const msgBuffer = new TextEncoder().encode(message);
    const hashBuffer = await crypto.subtle.digest('SHA-256', msgBuffer);
    return new Uint8Array(hashBuffer);
}

async function importKey(keyBytes) {
    return await crypto.subtle.importKey(
        'raw',
        keyBytes,
        { name: 'AES-CBC' },
        false,
        ['encrypt', 'decrypt']
    );
}

async function encrypt(keyBytes, plaintext) {
    const iv = crypto.getRandomValues(new Uint8Array(16));
    const cryptoKey = await importKey(keyBytes);
    const encoded = new TextEncoder().encode(plaintext);
    const ciphertext = await crypto.subtle.encrypt(
        { name: 'AES-CBC', iv: iv },
        cryptoKey,
        encoded
    );
    const result = new Uint8Array(iv.length + ciphertext.byteLength);
    result.set(iv, 0);
    result.set(new Uint8Array(ciphertext), iv.length);
    return result;
}

async function decrypt(keyBytes, ciphertext) {
    const iv = ciphertext.slice(0, 16);
    const data = ciphertext.slice(16);
    const cryptoKey = await importKey(keyBytes);
    const decrypted = await crypto.subtle.decrypt(
        { name: 'AES-CBC', iv: iv },
        cryptoKey,
        data
    );
    return new TextDecoder().decode(decrypted);
}

async function login() {
    const username = document.getElementById('username').value.trim();
    const password = document.getElementById('password').value;
    const errorDiv = document.getElementById('loginError');
    const loginBtn = document.getElementById('loginBtn');

    if (!username || !password) {
        errorDiv.textContent = '请输入用户名和密码';
        return;
    }

    errorDiv.textContent = '';
    loginBtn.disabled = true;
    loginBtn.textContent = '连接中...';

    try {
        key = await sha256(password);

        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const wsUrl = protocol + '//' + window.location.host + '/admin/ws';

        ws = new WebSocket(wsUrl);
        ws.binaryType = 'arraybuffer';

        ws.onopen = async () => {
            const encrypted = await encrypt(key, username);
            ws.send(encrypted);
        };

        ws.onmessage = async (event) => {
            try {
                const ciphertext = new Uint8Array(event.data);
                
                let plaintext;
                try {
                    plaintext = await decrypt(key, ciphertext);
                } catch (decryptError) {
                    console.error('解密失败:', decryptError);
                    errorDiv.textContent = '解密失败: ' + decryptError.message;
                    loginBtn.disabled = false;
                    loginBtn.textContent = '登录';
                    return;
                }
                
                let response;
                try {
                    response = JSON.parse(plaintext);
                } catch (parseError) {
                    console.error('JSON解析失败:', parseError);
                    errorDiv.textContent = '响应格式错误';
                    loginBtn.disabled = false;
                    loginBtn.textContent = '登录';
                    return;
                }

                if (response.Success) {
                    sessionId = response.NewId;
                    ws.onmessage = null;
                    document.getElementById('loginContainer').style.display = 'none';
                    document.getElementById('adminContainer').style.display = 'block';
                    loadDirectory('');
                } else {
                    errorDiv.textContent = response.Message || '登录失败';
                    loginBtn.disabled = false;
                    loginBtn.textContent = '登录';
                }
            } catch (e) {
                console.error('处理消息异常:', e);
                errorDiv.textContent = '服务器响应错误: ' + e.message;
                loginBtn.disabled = false;
                loginBtn.textContent = '登录';
            }
        };

        ws.onerror = () => {
            errorDiv.textContent = '连接失败';
            loginBtn.disabled = false;
            loginBtn.textContent = '登录';
        };

        ws.onclose = () => {
            if (!sessionId) {
                errorDiv.textContent = '连接已关闭';
                loginBtn.disabled = false;
                loginBtn.textContent = '登录';
            }
        };

    } catch (e) {
        errorDiv.textContent = '登录失败: ' + e.message;
        loginBtn.disabled = false;
        loginBtn.textContent = '登录';
    }
}

async function sendOperation(action, path, newPath) {
    return new Promise((resolve, reject) => {
        if (!ws || ws.readyState !== WebSocket.OPEN) {
            reject(new Error('未连接'));
            return;
        }

        const operation = {
            SessionId: sessionId,
            Action: action,
            Path: path,
            NewPath: newPath
        };

        const handler = async (event) => {
            try {
                const ciphertext = new Uint8Array(event.data);
                const plaintext = await decrypt(key, ciphertext);
                const response = JSON.parse(plaintext);
                
                if (response.Type === 'server_notification') {
                    alert('服务器通知: ' + response.Message);
                    ws.close();
                    document.getElementById('adminContainer').style.display = 'none';
                    document.getElementById('loginContainer').style.display = 'block';
                    errorDiv.textContent = '服务器已关闭';
                    ws.removeEventListener('message', handler);
                    reject(new Error('服务器已关闭'));
                    return;
                }
                
                ws.removeEventListener('message', handler);

                if (response.NewId) {
                    sessionId = response.NewId;
                }

                resolve(response);
            } catch (e) {
                ws.removeEventListener('message', handler);
                reject(e);
            }
        };

        ws.addEventListener('message', handler);

        encrypt(key, JSON.stringify(operation)).then(encrypted => {
            ws.send(encrypted);
        }).catch(reject);
    });
}

async function loadDirectory(path) {
    currentPath = path;
    updateBreadcrumb();

    try {
        const response = await sendOperation('list_directory', path, null);

        if (response.Success) {
            renderFileList(response.Data);
        } else {
            alert('加载目录失败: ' + response.Message);
        }
    } catch (e) {
        alert('加载目录失败: ' + e.message);
    }
}

function updateBreadcrumb() {
    const breadcrumb = document.getElementById('breadcrumb');
    let html = '<a href=""#"" onclick=""navigateTo(\'\')"">根目录</a>';

    if (currentPath) {
        const parts = currentPath.split('/').filter(p => p);
        let path = '';

        parts.forEach((part, index) => {
            path += (path ? '/' : '') + part;
            html += ' / <a href=""#"" onclick=""navigateTo(\'' + path + '\')"">' + escapeHtml(part) + '</a>';
        });
    }

    breadcrumb.innerHTML = html;
}

function renderFileList(data) {
    const tbody = document.getElementById('fileList');
    let html = '';

    if (currentPath) {
        const parentPath = currentPath.substring(0, currentPath.lastIndexOf('/'));
        html += '<tr><td><a href=""#"" onclick=""navigateTo(\'' + escapeHtml(parentPath) + '\')"" class=""dir"">../</a></td><td>-</td><td>-</td></tr>';
    }

    if (data.directories && data.directories.length > 0) {
        data.directories.forEach(dir => {
            html += '<tr><td><a href=""#"" onclick=""navigateTo(\'' + escapeHtml(dir.path) + '\')"" class=""dir"">' + escapeHtml(dir.name) + '/</a></td><td>-</td><td class=""actions""><button class=""action-btn btn-delete"" onclick=""deleteDirectory(\'' + escapeHtml(dir.path) + '\')"">回收站</button><button class=""action-btn btn-move"" onclick=""showMoveModal(\'' + escapeHtml(dir.path) + '\', \'directory\')"">移动</button></td></tr>';
        });
    }

    if (data.files && data.files.length > 0) {
        data.files.forEach(file => {
            html += '<tr><td class=""file"">' + escapeHtml(file.name) + '</td><td>' + formatFileSize(file.size) + '</td><td class=""actions""><button class=""action-btn"" style=""background: #17a2b8; color: white;"" onclick=""downloadFile(\'' + escapeHtml(file.path) + '\')"">下载</button><button class=""action-btn btn-delete"" onclick=""deleteFile(\'' + escapeHtml(file.path) + '\')"">回收站</button><button class=""action-btn btn-move"" onclick=""showMoveModal(\'' + escapeHtml(file.path) + '\', \'file\')"">移动</button></td></tr>';
        });
    }

    if (!html) {
        html = '<tr><td colspan=""3"" style=""text-align: center; color: #6c757d;"">空目录</td></tr>';
    }

    tbody.innerHTML = html;
}

function navigateTo(path) {
    loadDirectory(path);
    return false;
}

function downloadFile(path) {
    const downloadUrl = '/api/download/' + encodeURIComponent(path);
    window.open(downloadUrl, '_blank');
}

async function deleteFile(path) {
    if (!confirm('确定要将此文件移至回收站吗？')) return;

    try {
        const response = await sendOperation('delete_file', path, null);
        if (response.Success) {
            loadDirectory(currentPath);
        } else {
            alert('操作失败: ' + response.Message);
        }
    } catch (e) {
        alert('操作失败: ' + e.message);
    }
}

async function deleteDirectory(path) {
    if (!confirm('确定要将此目录及其所有内容移至回收站吗？')) return;

    try {
        const response = await sendOperation('delete_directory', path, null);
        if (response.Success) {
            loadDirectory(currentPath);
        } else {
            alert('操作失败: ' + response.Message);
        }
    } catch (e) {
        alert('操作失败: ' + e.message);
    }
}

function showMoveModal(path, type) {
    moveTargetPath = path;
    moveTargetType = type;
    document.getElementById('newPathInput').value = path;
    document.getElementById('moveModal').style.display = 'block';
}

function closeMoveModal() {
    document.getElementById('moveModal').style.display = 'none';
    moveTargetPath = null;
    moveTargetType = null;
}

async function confirmMove() {
    const newPath = document.getElementById('newPathInput').value.trim();

    if (!newPath) {
        alert('请输入新路径');
        return;
    }

    try {
        const action = moveTargetType === 'file' ? 'move_file' : 'move_directory';
        const response = await sendOperation(action, moveTargetPath, newPath);

        if (response.Success) {
            closeMoveModal();
            loadDirectory(currentPath);
        } else {
            alert('移动失败: ' + response.Message);
        }
    } catch (e) {
        alert('移动失败: ' + e.message);
    }
}

function showCreateDirModal() {
    document.getElementById('dirNameInput').value = '';
    document.getElementById('createDirModal').style.display = 'block';
}

function closeCreateDirModal() {
    document.getElementById('createDirModal').style.display = 'none';
}

async function confirmCreateDir() {
    const dirName = document.getElementById('dirNameInput').value.trim();

    if (!dirName) {
        alert('请输入目录名称');
        return;
    }

    const path = currentPath ? currentPath + '/' + dirName : dirName;

    try {
        const response = await sendOperation('create_directory', path, null);

        if (response.Success) {
            closeCreateDirModal();
            loadDirectory(currentPath);
        } else {
            alert('创建失败: ' + response.Message);
        }
    } catch (e) {
        alert('创建失败: ' + e.message);
    }
}

function showUploadModal() {
    document.getElementById('fileInput').click();
}

function handleFileSelect(event) {
    const file = event.target.files[0];
    if (!file) return;

    uploadFile(file);
    event.target.value = '';
}

async function uploadFile(file) {
    const formData = new FormData();
    formData.append('file', file);

    try {
        const response = await fetch('/admin/api/upload?path=' + encodeURIComponent(currentPath), {
            method: 'POST',
            body: formData
        });

        const result = await response.json();

        if (response.ok) {
            loadDirectory(currentPath);
        } else {
            alert('上传失败: ' + (result.message || result));
        }
    } catch (e) {
        alert('上传失败: ' + e.message);
    }
}

function logout() {
    if (ws) {
        ws.close();
    }
    ws = null;
    sessionId = null;
    key = null;
    currentPath = '';
    document.getElementById('adminContainer').style.display = 'none';
    document.getElementById('loginContainer').style.display = 'block';
    document.getElementById('username').value = '';
    document.getElementById('password').value = '';
    document.getElementById('loginBtn').disabled = false;
    document.getElementById('loginBtn').textContent = '登录';
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function formatFileSize(bytes) {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

document.getElementById('password').addEventListener('keypress', function(e) {
    if (e.key === 'Enter') {
        login();
    }
});

window.onclick = function(event) {
    if (event.target.classList.contains('modal')) {
        event.target.style.display = 'none';
    }
};

function showLogsModal() {
    document.getElementById('logsModal').style.display = 'block';
    document.getElementById('logsList').style.display = 'block';
    document.getElementById('logContent').style.display = 'none';
    loadLogFiles();
}

function closeLogsModal() {
    document.getElementById('logsModal').style.display = 'none';
}

async function loadLogFiles() {
    const logsList = document.getElementById('logsList');
    logsList.innerHTML = '加载中...';

    try {
        const response = await sendOperation('get_logs', null, null);

        if (response.Success && response.Data && response.Data.files) {
            if (response.Data.files.length === 0) {
                logsList.innerHTML = '<p style=""color: #6c757d; text-align: center;"">暂无日志文件</p>';
            } else {
                let html = '<table style=""width: 100%;""><thead><tr><th>文件名</th><th>大小</th><th>最后修改</th><th>操作</th></tr></thead><tbody>';
                response.Data.files.forEach(file => {
                    html += '<tr><td>' + escapeHtml(file.name) + '</td><td>' + escapeHtml(file.size) + '</td><td>' + escapeHtml(file.lastModified) + '</td><td><button class=""action-btn"" style=""background: #007bff; color: white;"" onclick=""viewLogFile(\'' + escapeHtml(file.name) + '\')"">查看</button></td></tr>';
                });
                html += '</tbody></table>';
                logsList.innerHTML = html;
            }
        } else {
            logsList.innerHTML = '<p style=""color: #dc3545; text-align: center;"">加载失败: ' + escapeHtml(response.Message) + '</p>';
        }
    } catch (e) {
        logsList.innerHTML = '<p style=""color: #dc3545; text-align: center;"">加载失败: ' + escapeHtml(e.message) + '</p>';
    }
}

async function viewLogFile(fileName) {
    const logsList = document.getElementById('logsList');
    const logContent = document.getElementById('logContent');
    const logText = document.getElementById('logText');
    const currentLogFile = document.getElementById('currentLogFile');

    logText.innerHTML = '加载中...';
    currentLogFile.textContent = fileName;
    logsList.style.display = 'none';
    logContent.style.display = 'block';

    try {
        const response = await sendOperation('get_log', fileName, null);

        if (response.Success && response.Data && response.Data.content) {
            logText.textContent = response.Data.content;
        } else {
            logText.innerHTML = '<span style=""color: #dc3545;"">加载失败: ' + escapeHtml(response.Message) + '</span>';
        }
    } catch (e) {
        logText.innerHTML = '<span style=""color: #dc3545;"">加载失败: ' + escapeHtml(e.message) + '</span>';
    }
}

function backToLogsList() {
    document.getElementById('logsList').style.display = 'block';
    document.getElementById('logContent').style.display = 'none';
}
";
        }
    }
}