using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Repository.Services
{
    public class AdminConnectionManager
    {
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        private readonly ConcurrentDictionary<string, byte[]> _sessionKeys = new();
        private readonly Logger _logger;
        private readonly ConfigManager _configManager;

        public AdminConnectionManager(Logger logger, ConfigManager configManager)
        {
            _logger = logger;
            _configManager = configManager;
        }

        public string RegisterConnection(WebSocket webSocket, byte[] sessionKey)
        {
            var connectionId = Guid.NewGuid().ToString("N");
            _connections[connectionId] = webSocket;
            _sessionKeys[connectionId] = sessionKey;
            _logger.LogInfo(I18nService.Instance.T("admin.connection_registered", connectionId));
            return connectionId;
        }

        public void UpdateSessionKey(string connectionId, byte[] sessionKey)
        {
            if (_connections.ContainsKey(connectionId))
            {
                _sessionKeys[connectionId] = sessionKey;
            }
        }

        public void RemoveConnection(string connectionId)
        {
            _connections.TryRemove(connectionId, out _);
            _sessionKeys.TryRemove(connectionId, out _);
            _logger.LogInfo(I18nService.Instance.T("admin.connection_removed", connectionId));
        }

        public async Task NotifyAllAsync(string message)
        {
            if (_connections.IsEmpty)
            {
                return;
            }

            var notification = new
            {
                Type = "server_notification",
                Message = message,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            var json = JsonSerializer.Serialize(notification);
            var tasks = new List<Task>();

            foreach (var kvp in _connections.ToList())
            {
                var connectionId = kvp.Key;
                var webSocket = kvp.Value;

                if (webSocket.State == WebSocketState.Open && _sessionKeys.TryGetValue(connectionId, out var key))
                {
                    tasks.Add(SendEncryptedMessageAsync(webSocket, key, json, connectionId));
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        private async Task SendEncryptedMessageAsync(WebSocket webSocket, byte[] key, string message, string connectionId)
        {
            try
            {
                var encrypted = Encrypt(key, message);
                await webSocket.SendAsync(new ArraySegment<byte>(encrypted), WebSocketMessageType.Binary, true, CancellationToken.None);
                _logger.LogInfo(I18nService.Instance.T("admin.notification_sent", connectionId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("admin.notification_failed", connectionId));
            }
        }

        public async Task CloseAllAsync(string reason = "")
        {
            if (_connections.IsEmpty)
            {
                return;
            }

            var actualReason = string.IsNullOrEmpty(reason) ? I18nService.Instance.T("admin.server_shutdown") : reason;
            _logger.LogInfo(I18nService.Instance.T("admin.closing_all", actualReason));

            var tasks = new List<Task>();

            foreach (var kvp in _connections.ToList())
            {
                var webSocket = kvp.Value;
                tasks.Add(SafeCloseAsync(webSocket, actualReason));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }

            _connections.Clear();
            _sessionKeys.Clear();
        }

        private async Task SafeCloseAsync(WebSocket webSocket, string reason)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("admin.websocket_close_error"));
            }
        }

        public int ConnectionCount => _connections.Count;

        private byte[] Encrypt(byte[] key, string plaintext)
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = key;
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;
            aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            var result = new byte[aes.IV.Length + ciphertext.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(ciphertext, 0, result, aes.IV.Length, ciphertext.Length);

            return result;
        }
    }
}
