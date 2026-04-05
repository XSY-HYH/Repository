using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Repository.Services;

public class CertificateGenerator
{
    private readonly Logger _logger;

    public CertificateGenerator(Logger logger)
    {
        _logger = logger;
    }

    public (string? certPath, string? keyPath) GenerateSelfSignedCertificate(string domain, string basePath)
    {
        try
        {
            string subject = string.IsNullOrEmpty(domain) ? "127.0.0.1" : domain;
            string safeName = subject.Replace(".", "_").Replace(":", "_");
            string certFileName = $"webdata_{safeName}.crt";
            string keyFileName = $"webdata_{safeName}.key";
            string certPath = Path.Combine(basePath, certFileName);
            string keyPath = Path.Combine(basePath, keyFileName);
            
            if (File.Exists(certPath) && File.Exists(keyPath))
            {
                _logger.LogInfo(I18nService.Instance.T("cert.found", certFileName, keyFileName));
                return (certPath, keyPath);
            }
            
            _logger.LogInfo(I18nService.Instance.T("cert.generating", subject));
            
            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
                
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection 
                        { 
                            new Oid("1.3.6.1.5.5.7.3.1"),
                            new Oid("1.3.6.1.5.5.7.3.2")
                        }, false));
                
                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName(subject);
                
                if (!string.IsNullOrEmpty(domain) && domain != "127.0.0.1" && domain != "localhost")
                {
                    sanBuilder.AddDnsName($"*.{domain}");
                }
                sanBuilder.AddDnsName("localhost");
                sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
                sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
                
                request.CertificateExtensions.Add(sanBuilder.Build());
                
                var certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(5));
                
                string pemCert = ExportToPem(certificate);
                File.WriteAllText(certPath, pemCert);
                
                string pemKey = ExportPrivateKeyToPem(rsa);
                File.WriteAllText(keyPath, pemKey);
                
                _logger.LogInfo(I18nService.Instance.T("cert.generated"));
                _logger.LogInfo(I18nService.Instance.T("cert.cert_path", certPath));
                _logger.LogInfo(I18nService.Instance.T("cert.key_path", keyPath));
                _logger.LogInfo(I18nService.Instance.T("cert.validity"));
                _logger.LogInfo(I18nService.Instance.T("cert.warning"));
                
                return (certPath, keyPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, I18nService.Instance.T("cert.generate_failed"));
            return (null, null);
        }
    }
    
    private string ExportToPem(X509Certificate2 cert)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN CERTIFICATE-----");
        builder.AppendLine(Convert.ToBase64String(cert.Export(X509ContentType.Cert), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END CERTIFICATE-----");
        return builder.ToString();
    }
    
    private string ExportPrivateKeyToPem(RSA rsa)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN RSA PRIVATE KEY-----");
        builder.AppendLine(Convert.ToBase64String(rsa.ExportRSAPrivateKey(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END RSA PRIVATE KEY-----");
        return builder.ToString();
    }
}
