using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;

public class SecureServerHandler
{
    private readonly RSA _serverPrivateKey;
    private readonly RSA _clientPublicKey;
    private readonly byte[] _sharedToken;
    private readonly NonceCache _nonceCache;
    private readonly int _timeoutSeconds;

    public SecureServerHandler(byte[] serverPrivateKeyPem, byte[] clientPublicKeyPem, byte[] sharedToken, int timeoutSeconds = 60)
    {
        _serverPrivateKey = RSA.Create();
        _serverPrivateKey.ImportFromPem(System.Text.Encoding.UTF8.GetString(serverPrivateKeyPem));
        
        _clientPublicKey = RSA.Create();
        _clientPublicKey.ImportFromPem(System.Text.Encoding.UTF8.GetString(clientPublicKeyPem));
        
        _sharedToken = sharedToken;
        _timeoutSeconds = timeoutSeconds;
        _nonceCache = new NonceCache(timeoutSeconds * 2);
    }

    private class NonceCache
    {
        private readonly Dictionary<byte[], DateTime> _cache = new Dictionary<byte[], DateTime>(new ByteArrayComparer());
        private readonly int _timeoutSeconds;
        private readonly Timer _cleanupTimer;

        public NonceCache(int timeoutSeconds)
        {
            _timeoutSeconds = timeoutSeconds;
            _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        private void Cleanup(object state)
        {
            lock (_cache)
            {
                var expired = DateTime.UtcNow.AddSeconds(-_timeoutSeconds);
                var keysToRemove = new List<byte[]>();
                foreach (var kvp in _cache)
                {
                    if (kvp.Value < expired)
                        keysToRemove.Add(kvp.Key);
                }
                foreach (var key in keysToRemove)
                    _cache.Remove(key);
            }
        }

        public bool TryAdd(byte[] nonce)
        {
            lock (_cache)
            {
                if (_cache.ContainsKey(nonce))
                    return false;
                
                _cache[nonce] = DateTime.UtcNow;
                return true;
            }
        }

        private class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public bool Equals(byte[] x, byte[] y)
            {
                if (x == null || y == null) return false;
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                    if (x[i] != y[i]) return false;
                return true;
            }

            public int GetHashCode(byte[] obj)
            {
                int hash = 0;
                foreach (byte b in obj)
                    hash = (hash << 8) ^ b;
                return hash;
            }
        }
    }

    private byte[] RsaDecrypt(RSA key, byte[] data)
    {
        return key.Decrypt(data, RSAEncryptionPadding.OaepSHA256);
    }

    private byte[] RsaSign(RSA key, byte[] data)
    {
        return key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    private bool RsaVerify(RSA key, byte[] signature, byte[] data)
    {
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    private byte[] AesEncrypt(byte[] key, byte[] plaintext)
    {
        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            byte[] iv = aes.IV;
            using (var encryptor = aes.CreateEncryptor())
            {
                byte[] ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
                byte[] result = new byte[iv.Length + ciphertext.Length];
                Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
                Buffer.BlockCopy(ciphertext, 0, result, iv.Length, ciphertext.Length);
                return result;
            }
        }
    }

    private byte[] AesDecrypt(byte[] key, byte[] ciphertext)
    {
        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            byte[] iv = new byte[16];
            byte[] actualCiphertext = new byte[ciphertext.Length - 16];
            Buffer.BlockCopy(ciphertext, 0, iv, 0, 16);
            Buffer.BlockCopy(ciphertext, 16, actualCiphertext, 0, actualCiphertext.Length);
            
            aes.IV = iv;
            using (var decryptor = aes.CreateDecryptor())
            {
                return decryptor.TransformFinalBlock(actualCiphertext, 0, actualCiphertext.Length);
            }
        }
    }

    private bool TimeConstantCompare(byte[] a, byte[] b)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        
        int result = 0;
        for (int i = 0; i < a.Length; i++)
            result |= a[i] ^ b[i];
        return result == 0;
    }

    public async Task<(bool success, byte[] response)> HandleRequestAsync(byte[] requestCipher)
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1. 解密请求
                byte[] requestPlain;
                try
                {
                    requestPlain = RsaDecrypt(_serverPrivateKey, requestCipher);
                }
                catch
                {
                    return (false, null);
                }

                if (requestPlain.Length < 25) // version(1) + nonce(16) + timestamp(8) = 25
                    return (false, null);

                // 2. 解析请求
                byte version = requestPlain[0];
                if (version != 0x01)
                    return (false, null);

                byte[] nonce = new byte[16];
                byte[] timestampBytes = new byte[8];
                Buffer.BlockCopy(requestPlain, 1, nonce, 0, 16);
                Buffer.BlockCopy(requestPlain, 17, timestampBytes, 0, 8);

                int tokenHashLength = requestPlain.Length - 25;
                byte[] tokenHash = new byte[tokenHashLength];
                Buffer.BlockCopy(requestPlain, 25, tokenHash, 0, tokenHashLength);

                // 3. 时间戳验证 (大端序)
                long reqTime = 0;
                for (int i = 0; i < 8; i++)
                {
                    reqTime = (reqTime << 8) | timestampBytes[i];
                }
                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                
                long timeDiff = reqTime > currentTime ? reqTime - currentTime : currentTime - reqTime;
                if (timeDiff > _timeoutSeconds)
                    return (false, null);

                // 4. Nonce重放检查
                if (!_nonceCache.TryAdd(nonce))
                    return (false, null);

                // 5. 验证令牌哈希
                using (var hmac = new HMACSHA256(_sharedToken))
                {
                    byte[] versionNonceTimestamp = new byte[1 + 16 + 8];
                    versionNonceTimestamp[0] = version;
                    Buffer.BlockCopy(nonce, 0, versionNonceTimestamp, 1, 16);
                    Buffer.BlockCopy(timestampBytes, 0, versionNonceTimestamp, 17, 8);
                    
                    byte[] expectedHash = hmac.ComputeHash(versionNonceTimestamp);
                    if (!TimeConstantCompare(tokenHash, expectedHash))
                        return (false, null);
                }

                // 6. 生成会话密钥
                byte[] sessionKey = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(sessionKey);
                }

                // 7. 用客户端公钥加密会话密钥
                byte[] encryptedSessionKey;
                try
                {
                    encryptedSessionKey = RsaEncrypt(_clientPublicKey, sessionKey);
                }
                catch
                {
                    return (false, null);
                }

                // 8. 准备响应数据
                byte[] responseData = System.Text.Encoding.UTF8.GetBytes(@"{""status"":""success"",""message"":""Hello, client!""}");

                // 9. 用会话密钥加密响应
                byte[] encryptedResponse = AesEncrypt(sessionKey, responseData);

                // 10. 服务器签名
                byte[] dataToSign = new byte[nonce.Length + encryptedResponse.Length];
                Buffer.BlockCopy(nonce, 0, dataToSign, 0, nonce.Length);
                Buffer.BlockCopy(encryptedResponse, 0, dataToSign, nonce.Length, encryptedResponse.Length);
                
                byte[] signature = RsaSign(_serverPrivateKey, dataToSign);

                // 11. 构造响应包
                byte[] responsePacket = BuildResponsePacket(encryptedSessionKey, signature, encryptedResponse);
                return (true, responsePacket);
            }
            catch
            {
                return (false, null);
            }
        });
    }

    private byte[] RsaEncrypt(RSA key, byte[] data)
    {
        return key.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
    }

    private byte[] BuildResponsePacket(byte[] encryptedSessionKey, byte[] signature, byte[] encryptedResponse)
    {
        int totalLength = 4 + encryptedSessionKey.Length + 4 + signature.Length + encryptedResponse.Length;
        byte[] packet = new byte[totalLength];
        int offset = 0;

        // 加密会话密钥长度 + 数据
        byte[] keyLenBytes = BitConverter.GetBytes(encryptedSessionKey.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(keyLenBytes);
        Buffer.BlockCopy(keyLenBytes, 0, packet, offset, 4);
        offset += 4;
        Buffer.BlockCopy(encryptedSessionKey, 0, packet, offset, encryptedSessionKey.Length);
        offset += encryptedSessionKey.Length;

        // 签名长度 + 数据
        byte[] sigLenBytes = BitConverter.GetBytes(signature.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(sigLenBytes);
        Buffer.BlockCopy(sigLenBytes, 0, packet, offset, 4);
        offset += 4;
        Buffer.BlockCopy(signature, 0, packet, offset, signature.Length);
        offset += signature.Length;

        // 加密响应数据
        Buffer.BlockCopy(encryptedResponse, 0, packet, offset, encryptedResponse.Length);

        return packet;
    }

    public void Dispose()
    {
        _serverPrivateKey?.Dispose();
        _clientPublicKey?.Dispose();
    }
}