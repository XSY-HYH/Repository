using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public class CHAPIEMServer
{
    private Dictionary<byte[], int> _loginIndex = new Dictionary<byte[], int>(ByteArrayComparer.Instance);
    private Dictionary<int, byte[]> _userKeys = new Dictionary<int, byte[]>();
    private Dictionary<int, UserSession> _userSessions = new Dictionary<int, UserSession>();
    private Dictionary<int, UserData> _userData = new Dictionary<int, UserData>();
    
    public class UserSession
    {
        public string CurrentKey { get; set; }  // Current ID used as encryption key
        public string NextId { get; set; }
        public byte[] K { get; set; }           // Pre-shared key for re-auth
    }
    
    public class UserData
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
    
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
    
    public CHAPIEMServer(List<UserData> usersDb)
    {
        // Phase 1: Startup precomputation
        foreach (var user in usersDb)
        {
            byte[] K = SHA256.HashData(Encoding.UTF8.GetBytes(user.Password));
            byte[] ciphertext = AesEncrypt(K, user.Username);
            byte[] loginHash = SHA256.HashData(ciphertext);
            
            _loginIndex[loginHash] = user.Id;
            _userKeys[user.Id] = K;
            _userData[user.Id] = user;
            
            _userSessions[user.Id] = new UserSession
            {
                CurrentKey = null,
                NextId = null,
                K = K
            };
        }
    }
    
    public (bool Success, int? UserId, string CurrentId, byte[] Response) Login(byte[] ciphertext)
    {
        byte[] requestHash = SHA256.HashData(ciphertext);
        
        if (!_loginIndex.TryGetValue(requestHash, out int userId))
        {
            return (false, null, null, null);
        }
        
        byte[] K = _userKeys[userId];
        string plaintext = AesDecrypt(K, ciphertext);
        
        if (plaintext == _userData[userId].Username)
        {
            string currentId = GenerateId();
            
            // In CHAP-IEM, ID_1 becomes the encryption key
            _userSessions[userId].CurrentKey = currentId;
            _userSessions[userId].NextId = null;
            
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
        
        string currentKey = session.CurrentKey;
        
        if (currentKey == null)
        {
            return (false, null, "No valid session key");
        }
        
        string plaintext;
        try
        {
            plaintext = AesDecrypt(currentKey, encryptedPacket);
        }
        catch
        {
            return (false, null, "Decryption failed, possible key mismatch");
        }
        
        // In CHAP-IEM, packet contains only operation data
        string operationData = plaintext;
        
        // Execute operation
        string result = ExecuteOperation(operationData);
        
        // Generate new ID
        string newId = GenerateId();
        string oldKey = currentKey;
        session.CurrentKey = newId;
        
        // Response encrypted with OLD key
        string response = $"{result}|{newId}";
        byte[] encryptedResponse = AesEncrypt(oldKey, response);
        
        return (true, encryptedResponse, "OK");
    }
    
    public (bool Success, byte[] Response) Reauthenticate(int userId)
    {
        if (!_userSessions.TryGetValue(userId, out var session))
        {
            return (false, null);
        }
        
        byte[] K = session.K;
        session.CurrentKey = null;
        session.NextId = null;
        
        byte[] instruction = AesEncrypt(K, "out_of_sync");
        return (true, instruction);
    }
    
    public (bool Success, byte[] Response, string Message) ResyncAttempt(int userId, byte[] encryptedPacket)
    {
        if (!_userSessions.TryGetValue(userId, out var session))
        {
            return (false, null, "Session not found");
        }
        
        // CHAP-IEM cannot resync - force re-authentication
        byte[] K = session.K;
        byte[] instruction = AesEncrypt(K, "out_of_sync");
        return (false, instruction, "Key out of sync, please re-authenticate");
    }
    
    private byte[] AesEncrypt(string keyHex, string plaintext)
    {
        return AesEncrypt(Convert.FromHexString(keyHex), plaintext);
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
                
                byte[] result = new byte[aes.IV.Length + ciphertext.Length];
                Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                Buffer.BlockCopy(ciphertext, 0, result, aes.IV.Length, ciphertext.Length);
                
                return result;
            }
        }
    }
    
    private string AesDecrypt(string keyHex, byte[] ciphertextWithIv)
    {
        return AesDecrypt(Convert.FromHexString(keyHex), ciphertextWithIv);
    }
    
    private string AesDecrypt(byte[] key, byte[] ciphertextWithIv)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
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
        return $"Result of: {operationData}";
    }
}
