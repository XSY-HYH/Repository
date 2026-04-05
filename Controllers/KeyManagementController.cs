using Microsoft.AspNetCore.Mvc;
using Repository.Services;
using System;

namespace Repository.Controllers
{
    public class KeyManagementController : Controller
    {
        private readonly KeyManagementService _keyManagementService;
        private readonly ProtectionService _protectionService;
        private readonly Logger _logger;
        private readonly ConfigManager _configManager;

        public KeyManagementController(KeyManagementService keyManagementService, ProtectionService protectionService, Logger logger, ConfigManager configManager)
        {
            _keyManagementService = keyManagementService;
            _protectionService = protectionService;
            _logger = logger;
            _configManager = configManager;
        }

        [HttpGet("api/keys/server")]
        public IActionResult GetServerPublicKey()
        {
            var clientIP = GetClientIP();
            
            try
            {
                var publicKeyPem = _keyManagementService.GetServerPublicKeyPem();
                
                _logger.LogInfo($"服务器公钥请求，客户端IP: {clientIP}");
                
                return Ok(new
                {
                    success = true,
                    publicKey = publicKeyPem
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取服务器公钥失败，客户端IP: {clientIP}");
                return StatusCode(500, "获取公钥失败");
            }
        }

        [HttpPost("api/keys/register")]
        public IActionResult RegisterClientPublicKey([FromBody] ClientKeyRequest request)
        {
            var clientIP = GetClientIP();
            
            try
            {
                if (string.IsNullOrEmpty(request.ClientId) || string.IsNullOrEmpty(request.PublicKey))
                {
                    return BadRequest("客户端ID和公钥不能为空");
                }

                if (string.IsNullOrEmpty(request.SharedToken))
                {
                    return BadRequest("共享令牌不能为空");
                }

                var success = _keyManagementService.RegisterClientPublicKey(request.ClientId, request.PublicKey);
                
                if (success)
                {
                    _keyManagementService.SetSharedToken(request.ClientId, request.SharedToken);
                    _protectionService.RefreshSecureHandlerForClient(request.ClientId);
                    _logger.LogInfo($"客户端公钥注册成功，客户端ID: {request.ClientId}，IP: {clientIP}");
                    
                    return Ok(new
                    {
                        success = true,
                        message = "客户端公钥注册成功",
                        clientId = request.ClientId
                    });
                }
                
                return BadRequest("公钥格式无效");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"注册客户端公钥失败，客户端IP: {clientIP}");
                return StatusCode(500, "注册失败");
            }
        }

        [HttpPost("api/keys/verify")]
        public async Task<IActionResult> VerifyRequest([FromBody] VerifyRequest request)
        {
            var clientIP = GetClientIP();
            
            try
            {
                if (string.IsNullOrEmpty(request.ClientId) || request.EncryptedRequest == null || request.EncryptedRequest.Length == 0)
                {
                    return BadRequest("请求参数不完整");
                }

                if (string.IsNullOrEmpty(request.Path))
                {
                    return BadRequest("目标路径不能为空");
                }

                var (success, sessionId, response) = await _protectionService.VerifySecureRequestAsync(request.Path, request.EncryptedRequest);
                
                if (success)
                {
                    _logger.LogInfo($"验证请求成功，客户端ID: {request.ClientId}，IP: {clientIP}，路径: {request.Path}");
                    return Ok(new
                    {
                        success = true,
                        sessionId = sessionId,
                        response = Convert.ToBase64String(response)
                    });
                }
                
                _logger.LogWarning($"验证请求失败，客户端ID: {request.ClientId}，IP: {clientIP}");
                return Unauthorized("验证失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"验证请求异常，客户端IP: {clientIP}");
                return StatusCode(500, "验证异常");
            }
        }

        [HttpDelete("api/keys/client/{clientId}")]
        public IActionResult RemoveClient(string clientId)
        {
            var clientIP = GetClientIP();
            
            try
            {
                _keyManagementService.RemoveClient(clientId);
                _logger.LogInfo($"移除客户端，客户端ID: {clientId}，IP: {clientIP}");
                
                return Ok(new
                {
                    success = true,
                    message = "客户端已移除"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"移除客户端失败，客户端IP: {clientIP}");
                return StatusCode(500, "移除失败");
            }
        }

        private string GetClientIP()
        {
            var forwardedFor = Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (ips.Length > 0)
                {
                    return ips[0].Trim();
                }
            }
            
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }

    public class ClientKeyRequest
    {
        public string ClientId { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public string SharedToken { get; set; } = "";
    }

    public class VerifyRequest
    {
        public string ClientId { get; set; } = "";
        public byte[] EncryptedRequest { get; set; } = Array.Empty<byte>();
        public string Path { get; set; } = "";
    }
}
