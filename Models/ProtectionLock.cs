using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Repository.Models
{
    public class ProtectionLock
    {
        [JsonPropertyName("auth_method")]
        public string AuthMethod { get; set; } = "token";
        
        [JsonPropertyName("token")]
        public string Token { get; set; } = "";
        
        [JsonPropertyName("token_hash")]
        public string TokenHash { get; set; } = "";
        
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = "";
        
        [JsonPropertyName("shared_token")]
        public string SharedToken { get; set; } = "";
        
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        public static string HashToken(string token)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
                return Convert.ToHexString(bytes).ToLower();
            }
        }
        
        public void UpdateHash()
        {
            if (AuthMethod == "token" && !string.IsNullOrEmpty(Token) && string.IsNullOrEmpty(TokenHash))
            {
                TokenHash = HashToken(Token);
            }
        }
        
        public bool VerifyToken(string? providedTokenHash)
        {
            if (AuthMethod != "token")
            {
                return false;
            }
            
            if (string.IsNullOrEmpty(TokenHash) || string.IsNullOrEmpty(providedTokenHash))
            {
                return false;
            }
            
            return string.Equals(TokenHash, providedTokenHash, StringComparison.OrdinalIgnoreCase);
        }
        
        public bool IsSecureAuth()
        {
            return AuthMethod == "secure" && 
                   !string.IsNullOrEmpty(ClientId) && 
                   !string.IsNullOrEmpty(SharedToken);
        }
    }
}
