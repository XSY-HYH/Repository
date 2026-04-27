using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public class CHAPServer
{
    private Dictionary<byte[], int> _loginIndex = new Dictionary<byte[], int>(ByteArrayComparer.Instance);
    private Dictionary<int, byte[]> _userKeys = new Dictionary<int, byte[]>();
    private Dictionary<int, UserSession> _userSessions = new Dictionary<int, UserSession>();
    private Dictionary<int, UserData> _userData = new Dictionary<int, UserData>();
    
    public class UserSession
    {
        public string CurrentId { get; set; }
        public byte[] Key { get; set; }
    }
    
    public class UserData
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
    
    // Byte array comparer for dictionary keys
    private class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance = new ByteArrayComparer();
        
        public bool Equals(byte[] x, byte[] y)
        {
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
                if (x[i] != y[i]) return false;
            return true;
        }
        
        public int GetHashCode(byte[] obj)
        {
            int hash = 17;
            for (int i = 0; i < obj.Length; i++)
                hash = hash * 31 + obj[i];
            return hash;
        }
    }
    
    public CHAPServer(List<UserData> usersDb)
    {
        // Phase 1: Startup precomputation
        foreach (var user in usersDb)
        {
            // Compute pre-shared key K
            byte[] K = SHA256.HashData(Encoding.UTF8.GetBytes(user.Password));
            
            // Compute expected login ciphertext
            byte[] ciphertext = AesEncrypt(K, user.Username);
            
            // Store hash for O(1) lookup
            byte[] loginHash = SHA256.HashData(ciphertext);
            _loginIndex[loginHash] = user.Id;
            
            // Store K for later use
            _userKeys[user.Id] = K;
            _userData[user.Id] = user;
            
            // Initialize session state
            _userSessions[user.Id] = new UserSession
            {
                CurrentId = null,
                Key = K
            };
        }
    }
    
    public (bool Success, int? UserId, string CurrentId, byte[] Response) Login(byte[] ciphertext)
    {
        // Phase 2: Runtime login with O(1) hash lookup
        byte[] requestHash = SHA256.HashData(ciphertext);
        
        // O(1) lookup
        if (!_loginIndex.TryGetValue(requestHash, out int userId))
        {
            return (false, null, null, null);
        }
        
        // Verify with actual decryption
        byte[] K = _userKeys[userId];
        string plaintext = AesDecrypt(K, ciphertext);
        
        if (plaintext == _userData[userId].Username)
        {
            // Login success - generate first ID
            string currentId = GenerateId();
            _userSessions[userId].CurrentId = currentId;
            
            // Response: OK + ID_1 encrypted with K
            string response = $"OK|{currentId}";
            byte[] encryptedResponse = AesEncrypt(K, response);
            
            return (true, userId, currentId, encryptedResponse);
        }
        
        return (false, null, null, null);
    }
    
    public (bool Success, byte[] Response, string Message) Operation(int userId, byte[] encryptedPacket)
    {
        if (!_userSessions.TryGetValue(userId, out var session))
        {
            return (false, null, "Session not found");
        }
        
        byte[] K = session.Key;
        string currentId = session.CurrentId;
        
        // Decrypt with K
        string plaintext = AesDecrypt(K, encryptedPacket);
        
        // Expected format: "operation_data|id"
        int lastPipe = plaintext.LastIndexOf('|');
        if (lastPipe == -1)
        {
            return (false, null, "Invalid packet format");
        }
        
        string operationData = plaintext.Substring(0, lastPipe);
        string receivedId = plaintext.Substring(lastPipe + 1);
        
        // Verify ID
        if (receivedId != currentId)
        {
            // Out of sync - return recovery packet
            string recoveryPacket = $"resync|{currentId}";
            byte[] encryptedRecovery = AesEncrypt(K, recoveryPacket);
            return (false, encryptedRecovery, "ID mismatch, resync required");
        }
        
        // Execute operation
        string result = ExecuteOperation(operationData);
        
        // Generate new ID
        string newId = GenerateId();
        session.CurrentId = newId;
        
        // Response: result + new_id encrypted with K
        string response = $"{result}|{newId}";
        byte[] encryptedResponse = AesEncrypt(K, response);
        
        return (true, encryptedResponse, "OK");
    }
    
    public (bool Success, byte[] Response, string Message) ResyncConfirm(int userId, byte[] encryptedPacket)
    {
        if (!_userSessions.TryGetValue(userId, out var session))
        {
            return (false, null, "Session not found");
        }
        
        byte[] K = session.Key;
        string plaintext = AesDecrypt(K, encryptedPacket);
        
        if (plaintext.StartsWith("resync_ack|"))
        {
            string receivedId = plaintext.Split('|')[1];
            if (receivedId == session.CurrentId)
            {
                byte[] resyncOk = AesEncrypt(K, "resync_ok");
                return (true, resyncOk, "Resync successful");
            }
        }
        
        return (false, null, "Resync failed");
    }
    
    private byte[] AesEncrypt(byte[] key, string plaintext)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();
            
            byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
            
            using (var encryptor = aes.CreateEncryptor())
            {
                byte[] ciphertext = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                
                // Combine IV + ciphertext
                byte[] result = new byte[aes.IV.Length + ciphertext.Length];
                Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                Buffer.BlockCopy(ciphertext, 0, result, aes.IV.Length, ciphertext.Length);
                
                return result;
            }
        }
    }
    
    private string AesDecrypt(byte[] key, byte[] ciphertextWithIv)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            // Extract IV (first 16 bytes)
            byte[] iv = new byte[16];
            byte[] ciphertext = new byte[ciphertextWithIv.Length - 16];
            Buffer.BlockCopy(ciphertextWithIv, 0, iv, 0, 16);
            Buffer.BlockCopy(ciphertextWithIv, 16, ciphertext, 0, ciphertext.Length);
            
            aes.IV = iv;
            
            using (var decryptor = aes.CreateDecryptor())
            {
                byte[] plainBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                return Encoding.UTF8.GetString(plainBytes);
            }
        }
    }
    
    private string GenerateId()
    {
        byte[] randomBytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        return Convert.ToHexString(randomBytes).ToLower();
    }
    
    private string ExecuteOperation(string operationData)
    {
        // Implementation dependent
        return $"Result of: {operationData}";
    }
}

// SHA256 helper for .NET Framework (if not using .NET 6+)
#if !NET6_0_OR_GREATER
public static class SHA256
{
    public static byte[] HashData(byte[] bytes)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            return sha256.ComputeHash(bytes);
        }
    }
}
#endif
