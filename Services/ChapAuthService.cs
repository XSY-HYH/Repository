using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Repository.Models;

namespace Repository.Services
{
    public class ChapSession
    {
        public string SessionId { get; set; } = "";
        public string ClientIP { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivity { get; set; }
    }

    public class ChapResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? NewId { get; set; }
        public object? Data { get; set; }
        public bool IsRecovery { get; set; }
    }

    public class ChapAuthService
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, ChapSession> _sessions = new();
        private readonly ConcurrentDictionary<string, byte[]> _sessionCurrentKeys = new();
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        public ChapAuthService(ConfigManager configManager, Logger logger)
        {
            _configManager = configManager;
            _logger = logger;
        }

        private byte[] GetK()
        {
            var config = _configManager.GetConfig();
            return GetKeyFromPassword(config.AdminPassword ?? "");
        }

        public byte[] GetKeyFromPassword(string password)
        {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        }

        public byte[] Encrypt(byte[] key, string plaintext)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            var result = new byte[aes.IV.Length + ciphertext.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(ciphertext, 0, result, aes.IV.Length, ciphertext.Length);

            return result;
        }

        public string? Decrypt(byte[] key, byte[] ciphertext)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var iv = new byte[16];
                Buffer.BlockCopy(ciphertext, 0, iv, 0, 16);
                aes.IV = iv;

                var actualCiphertext = new byte[ciphertext.Length - 16];
                Buffer.BlockCopy(ciphertext, 16, actualCiphertext, 0, actualCiphertext.Length);

                using var decryptor = aes.CreateDecryptor();
                var plaintextBytes = decryptor.TransformFinalBlock(actualCiphertext, 0, actualCiphertext.Length);
                return Encoding.UTF8.GetString(plaintextBytes);
            }
            catch
            {
                return null;
            }
        }

        public string GenerateId()
        {
            var bytes = new byte[32];
            _rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        public byte[] GenerateIdBytes()
        {
            var bytes = new byte[32];
            _rng.GetBytes(bytes);
            return bytes;
        }

        public (bool success, ChapResponse response, byte[]? encryptKey) HandleLogin(byte[] encryptedData, string clientIP)
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.admin_disabled") }, null);
            }

            if (string.IsNullOrEmpty(config.AdminPassword))
            {
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.password_not_configured") }, null);
            }

            var k = GetK();
            var decrypted = Decrypt(k, encryptedData);

            if (decrypted == null)
            {
                _logger.LogWarning(I18nService.Instance.T("chap.decrypt_failed_log", clientIP));
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.decrypt_failed") }, null);
            }

            var username = decrypted.Trim();

            if (username != config.AdminUsername)
            {
                _logger.LogWarning(I18nService.Instance.T("chap.username_wrong_log", clientIP, username));
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.auth_failed") }, null);
            }

            var id1 = GenerateId();
            var id1Bytes = GenerateIdBytes();
            var sessionId = GenerateSessionId();

            var session = new ChapSession
            {
                SessionId = sessionId,
                ClientIP = clientIP,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow
            };

            _sessions[sessionId] = session;
            _sessionCurrentKeys[sessionId] = id1Bytes;

            _logger.LogInfo(I18nService.Instance.T("chap.login_success_log", clientIP, sessionId));

            return (true, new ChapResponse
            {
                Success = true,
                Message = I18nService.Instance.T("chap.login_success"),
                NewId = id1
            }, k);
        }

        public (bool success, ChapResponse response, byte[]? encryptKey) HandleOperation(byte[] encryptedData, string clientIP)
        {
            var config = _configManager.GetConfig();

            if (!config.AdminEnabled)
            {
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.admin_disabled") }, null);
            }

            string? matchedSessionId = null;
            string? decryptedPlaintext = null;
            byte[]? oldKey = null;

            foreach (var kvp in _sessionCurrentKeys)
            {
                var plaintext = Decrypt(kvp.Value, encryptedData);
                if (plaintext != null)
                {
                    matchedSessionId = kvp.Key;
                    decryptedPlaintext = plaintext;
                    oldKey = kvp.Value;
                    break;
                }
            }

            if (decryptedPlaintext == null || matchedSessionId == null)
            {
                var k = GetK();
                var recoveryPlaintext = Decrypt(k, encryptedData);

                if (recoveryPlaintext != null)
                {
                    _logger.LogInfo(I18nService.Instance.T("chap.recovery_ack_log", clientIP));
                    var latestSession = _sessions.OrderByDescending(s => s.Value.LastActivity).FirstOrDefault();
                    if (latestSession.Value != null && _sessionCurrentKeys.TryGetValue(latestSession.Key, out var currentKey))
                    {
                        var currentIdBase64 = Convert.ToBase64String(currentKey);
                        return (true, new ChapResponse
                        {
                            Success = true,
                            Message = I18nService.Instance.T("chap.resync_success"),
                            NewId = currentIdBase64,
                            IsRecovery = true
                        }, k);
                    }
                }

                _logger.LogWarning(I18nService.Instance.T("chap.operation_decrypt_failed_log", clientIP));
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.decrypt_failed") }, null);
            }

            if (!_sessions.TryGetValue(matchedSessionId, out var session))
            {
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.session_expired") }, null);
            }

            if (session.ClientIP != clientIP)
            {
                _logger.LogWarning(I18nService.Instance.T("chap.ip_mismatch_log", clientIP, session.ClientIP));
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.ip_mismatch") }, oldKey);
            }

            try
            {
                var operation = JsonSerializer.Deserialize<ChapOperation>(decryptedPlaintext);
                if (operation == null || string.IsNullOrEmpty(operation.SessionId))
                {
                    return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.invalid_request") }, oldKey);
                }

                if (operation.SessionId != matchedSessionId)
                {
                    return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.session_mismatch") }, oldKey);
                }

                var nextId = GenerateId();
                var nextIdBytes = GenerateIdBytes();

                session.LastActivity = DateTime.UtcNow;
                _sessionCurrentKeys[matchedSessionId] = nextIdBytes;

                return (true, new ChapResponse
                {
                    Success = true,
                    Message = I18nService.Instance.T("chap.operation_success"),
                    NewId = nextId,
                    Data = operation
                }, oldKey);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("chap.json_parse_error_log"));
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.json_parse_error") }, oldKey);
            }
        }

        public (bool success, ChapResponse response, byte[]? encryptKey) HandleRecoveryRequest(string clientIP)
        {
            var k = GetK();
            var latestSession = _sessions.OrderByDescending(s => s.Value.LastActivity).FirstOrDefault();

            if (latestSession.Value == null || !_sessionCurrentKeys.TryGetValue(latestSession.Key, out var currentKey))
            {
                return (false, new ChapResponse { Success = false, Message = I18nService.Instance.T("chap.session_expired") }, null);
            }

            var currentIdBase64 = Convert.ToBase64String(currentKey);

            _logger.LogInfo(I18nService.Instance.T("chap.recovery_sent_log", clientIP));

            return (true, new ChapResponse
            {
                Success = true,
                Message = I18nService.Instance.T("chap.resync_success"),
                NewId = currentIdBase64,
                IsRecovery = true
            }, k);
        }

        public bool ValidateSession(string sessionId, string clientIP)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return false;
            }

            if (session.ClientIP != clientIP)
            {
                return false;
            }

            if (DateTime.UtcNow - session.LastActivity > TimeSpan.FromHours(1))
            {
                _sessions.TryRemove(sessionId, out _);
                _sessionCurrentKeys.TryRemove(sessionId, out _);
                return false;
            }

            session.LastActivity = DateTime.UtcNow;
            return true;
        }

        public void RemoveSession(string sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
            _sessionCurrentKeys.TryRemove(sessionId, out _);
        }

        public void CleanupExpiredSessions()
        {
            var expiredSessions = _sessions
                .Where(s => DateTime.UtcNow - s.Value.LastActivity > TimeSpan.FromHours(1))
                .Select(s => s.Key)
                .ToList();

            foreach (var sessionId in expiredSessions)
            {
                _sessions.TryRemove(sessionId, out _);
                _sessionCurrentKeys.TryRemove(sessionId, out _);
            }
        }

        private string GenerateSessionId()
        {
            var bytes = new byte[16];
            _rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }
    }

    public class ChapOperation
    {
        public string SessionId { get; set; } = "";
        public string Action { get; set; } = "";
        public string? Path { get; set; }
        public string? NewPath { get; set; }
        public string? TargetPath { get; set; }
        public Dictionary<string, object?>? Settings { get; set; }
    }
}
