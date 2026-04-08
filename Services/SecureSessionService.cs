using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Repository.Services
{
    public class SecureSession
    {
        public string SessionId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ProtectedPath { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public byte[] SessionKey { get; set; } = Array.Empty<byte>();
        
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }

    public class SecureSessionService
    {
        private readonly ConcurrentDictionary<string, SecureSession> _sessions = new();
        private readonly int _sessionTimeoutMinutes;
        private readonly Logger _logger;
        private readonly Timer _cleanupTimer;

        public SecureSessionService(Logger logger, int sessionTimeoutMinutes = 30)
        {
            _logger = logger;
            _sessionTimeoutMinutes = sessionTimeoutMinutes;
            _cleanupTimer = new Timer(CleanupExpiredSessions, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public SecureSession CreateSession(string clientId, string protectedPath, byte[] sessionKey)
        {
            var sessionId = GenerateSessionId();
            var session = new SecureSession
            {
                SessionId = sessionId,
                ClientId = clientId,
                ProtectedPath = protectedPath,
                SessionKey = sessionKey,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_sessionTimeoutMinutes)
            };
            
            _sessions[sessionId] = session;
            _logger.LogInfo(I18nService.Instance.T("secure_session.created", sessionId, clientId, protectedPath));
            
            return session;
        }

        public SecureSession? GetSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                if (session.IsExpired)
                {
                    _sessions.TryRemove(sessionId, out _);
                    return null;
                }
                
                session.ExpiresAt = DateTime.UtcNow.AddMinutes(_sessionTimeoutMinutes);
                return session;
            }
            return null;
        }

        public bool ValidateSessionForPath(string sessionId, string path)
        {
            var session = GetSession(sessionId);
            if (session == null)
            {
                return false;
            }
            
            var normalizedPath = path.Replace('\\', '/').TrimEnd('/');
            var protectedPath = session.ProtectedPath.TrimEnd('/');
            
            return normalizedPath.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalizedPath, protectedPath, StringComparison.OrdinalIgnoreCase);
        }

        public void RemoveSession(string sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
            _logger.LogInfo(I18nService.Instance.T("secure_session.removed", sessionId));
        }

        private string GenerateSessionId()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[32];
            rng.GetBytes(bytes);
            return Convert.ToHexString(bytes).ToLower();
        }

        private void CleanupExpiredSessions(object? state)
        {
            var expiredCount = 0;
            foreach (var kvp in _sessions)
            {
                if (kvp.Value.IsExpired)
                {
                    _sessions.TryRemove(kvp.Key, out _);
                    expiredCount++;
                }
            }
            
            if (expiredCount > 0)
            {
                _logger.LogInfo(I18nService.Instance.T("secure_session.cleaned", expiredCount));
            }
        }

        public int GetActiveSessionCount()
        {
            return _sessions.Count;
        }
    }
}
