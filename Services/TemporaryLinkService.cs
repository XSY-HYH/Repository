using System.Collections.Concurrent;

namespace Repository.Services
{
    public class TemporaryLinkService
    {
        private readonly ConcurrentDictionary<string, TemporaryLinkInfo> _temporaryLinks = new();
        private readonly Timer _cleanupTimer;
        
        public TemporaryLinkService()
        {
            // 每5分钟清理一次过期的链接
            _cleanupTimer = new Timer(CleanupExpiredLinks, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }
        
        public string GenerateTemporaryLink(string filePath)
        {
            // 生成一个随机的GUID作为令牌
            var token = Guid.NewGuid().ToString("N")[..8]; // 取前8位作为短令牌
            var expiry = DateTime.UtcNow.AddMinutes(5); // 5分钟后过期
            
            // 存储链接信息
            _temporaryLinks[token] = new TemporaryLinkInfo
            {
                FilePath = filePath,
                Expiry = expiry,
                Created = DateTime.UtcNow
            };
            
            return token;
        }
        
        public string? ValidateAndConsumeToken(string token)
        {
            if (!_temporaryLinks.TryRemove(token, out var linkInfo))
            {
                return null;
            }
            
            // 检查是否过期
            if (DateTime.UtcNow > linkInfo.Expiry)
            {
                return null;
            }
            
            return linkInfo.FilePath;
        }
        
        private void CleanupExpiredLinks(object? state)
        {
            var now = DateTime.UtcNow;
            var expiredTokens = new List<string>();
            
            foreach (var kvp in _temporaryLinks)
            {
                if (now > kvp.Value.Expiry)
                {
                    expiredTokens.Add(kvp.Key);
                }
            }
            
            foreach (var token in expiredTokens)
            {
                _temporaryLinks.TryRemove(token, out _);
            }
        }
        
        private class TemporaryLinkInfo
        {
            public string FilePath { get; set; } = string.Empty;
            public DateTime Expiry { get; set; }
            public DateTime Created { get; set; }
        }
    }
}