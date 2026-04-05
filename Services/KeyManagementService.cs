using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Repository.Services
{
    public class KeyManagementService : IDisposable
    {
        private readonly Logger _logger;
        private readonly ConfigManager _configManager;
        private RSA _serverPrivateKey;
        private RSA _serverPublicKey;
        private readonly ConcurrentDictionary<string, RSA> _clientPublicKeys = new();
        private readonly ConcurrentDictionary<string, byte[]> _sharedTokens = new();
        private readonly string _keysDirectory;

        public KeyManagementService(Logger logger, ConfigManager configManager)
        {
            _logger = logger;
            _configManager = configManager;
            _keysDirectory = Path.Combine(configManager.GetConfig().RepositoryPath, ".keys");
            
            InitializeKeys();
        }

        private void InitializeKeys()
        {
            try
            {
                if (!Directory.Exists(_keysDirectory))
                {
                    Directory.CreateDirectory(_keysDirectory);
                }

                var privateKeyPath = Path.Combine(_keysDirectory, "server_private.pem");
                var publicKeyPath = Path.Combine(_keysDirectory, "server_public.pem");

                if (File.Exists(privateKeyPath) && File.Exists(publicKeyPath))
                {
                    _serverPrivateKey = RSA.Create();
                    _serverPrivateKey.ImportFromPem(File.ReadAllText(privateKeyPath));

                    _serverPublicKey = RSA.Create();
                    _serverPublicKey.ImportFromPem(File.ReadAllText(publicKeyPath));
                    
                    _logger.LogInfo(I18nService.Instance.T("key.loaded"));
                }
                else
                {
                    GenerateAndSaveKeyPair(privateKeyPath, publicKeyPath);
                    _logger.LogInfo(I18nService.Instance.T("key.generated"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("key.init_failed"));
                GenerateNewKeyPair();
            }
        }

        private void GenerateAndSaveKeyPair(string privateKeyPath, string publicKeyPath)
        {
            _serverPrivateKey = RSA.Create(2048);
            _serverPublicKey = RSA.Create();

            var privateKeyPem = _serverPrivateKey.ExportRSAPrivateKeyPem();
            var publicKeyPem = _serverPrivateKey.ExportRSAPublicKeyPem();

            _serverPublicKey.ImportFromPem(publicKeyPem);

            File.WriteAllText(privateKeyPath, privateKeyPem);
            File.WriteAllText(publicKeyPath, publicKeyPem);

            File.SetAttributes(privateKeyPath, FileAttributes.Hidden | FileAttributes.ReadOnly);
        }

        private void GenerateNewKeyPair()
        {
            _serverPrivateKey = RSA.Create(2048);
            _serverPublicKey = RSA.Create();
            
            var publicKeyPem = _serverPrivateKey.ExportRSAPublicKeyPem();
            _serverPublicKey.ImportFromPem(publicKeyPem);
        }

        public string GetServerPublicKeyPem()
        {
            return _serverPrivateKey.ExportRSAPublicKeyPem();
        }

        public byte[] GetServerPublicKeyBytes()
        {
            return _serverPrivateKey.ExportRSAPublicKey();
        }

        public bool RegisterClientPublicKey(string clientId, string publicKeyPem)
        {
            try
            {
                var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                _clientPublicKeys[clientId] = rsa;
                
                _logger.LogInfo(I18nService.Instance.T("key.client_registered", clientId));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("key.client_register_failed", clientId));
                return false;
            }
        }

        public bool RegisterClientPublicKey(string clientId, byte[] publicKeyBytes)
        {
            try
            {
                var rsa = RSA.Create();
                rsa.ImportRSAPublicKey(publicKeyBytes, out _);
                _clientPublicKeys[clientId] = rsa;
                
                _logger.LogInfo(I18nService.Instance.T("key.client_registered", clientId));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("key.client_register_failed", clientId));
                return false;
            }
        }

        public void SetSharedToken(string clientId, byte[] token)
        {
            _sharedTokens[clientId] = token;
        }

        public void SetSharedToken(string clientId, string token)
        {
            _sharedTokens[clientId] = Encoding.UTF8.GetBytes(token);
        }

        public SecureServerHandler CreateSecureServerHandler(string clientId, int timeoutSeconds = 60)
        {
            if (!_clientPublicKeys.TryGetValue(clientId, out var clientPublicKey))
            {
                _logger.LogWarning(I18nService.Instance.T("key.client_not_found", clientId));
                return null;
            }

            if (!_sharedTokens.TryGetValue(clientId, out var sharedToken))
            {
                _logger.LogWarning(I18nService.Instance.T("key.token_not_found", clientId));
                return null;
            }

            var privateKeyPem = Encoding.UTF8.GetBytes(_serverPrivateKey.ExportRSAPrivateKeyPem());
            var publicKeyPem = Encoding.UTF8.GetBytes(clientPublicKey.ExportRSAPublicKeyPem());

            return new SecureServerHandler(privateKeyPem, publicKeyPem, sharedToken, timeoutSeconds);
        }

        public bool HasClientKey(string clientId)
        {
            return _clientPublicKeys.ContainsKey(clientId);
        }

        public void RemoveClient(string clientId)
        {
            _clientPublicKeys.TryRemove(clientId, out _);
            _sharedTokens.TryRemove(clientId, out _);
            _logger.LogInfo(I18nService.Instance.T("key.client_removed", clientId));
        }

        public RSA GetServerPrivateKey()
        {
            return _serverPrivateKey;
        }

        public RSA GetServerPublicKey()
        {
            return _serverPublicKey;
        }

        public void Dispose()
        {
            _serverPrivateKey?.Dispose();
            _serverPublicKey?.Dispose();
            
            foreach (var key in _clientPublicKeys.Values)
            {
                key?.Dispose();
            }
            _clientPublicKeys.Clear();
            _sharedTokens.Clear();
        }
    }
}
