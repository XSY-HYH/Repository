using System.Collections.Concurrent;
using System.Net;

namespace Repository.Services
{
    public class ProxyProtocolService
    {
        private static readonly byte[] V2_SIGNATURE = new byte[] { 0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A };
        private readonly Logger _logger;
        public ConcurrentDictionary<string, string> ConnectionIPs { get; } = new();

        public ProxyProtocolService(Logger logger)
        {
            _logger = logger;
        }

        public (string? SourceIP, int BytesConsumed) ParseHeader(byte[] data)
        {
            if (data == null || data.Length < 6)
                return (null, 0);

            if (IsProxyProtocolV1(data))
                return ParseV1Header(data);
            else if (IsProxyProtocolV2(data))
                return ParseV2Header(data);

            return (null, 0);
        }

        private bool IsProxyProtocolV1(byte[] data)
        {
            if (data.Length < 6)
                return false;

            var prefix = System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(6, data.Length));
            return prefix == "PROXY ";
        }

        private bool IsProxyProtocolV2(byte[] data)
        {
            if (data.Length < V2_SIGNATURE.Length)
                return false;

            for (int i = 0; i < V2_SIGNATURE.Length; i++)
            {
                if (data[i] != V2_SIGNATURE[i])
                    return false;
            }
            return true;
        }

        private (string? SourceIP, int BytesConsumed) ParseV1Header(byte[] data)
        {
            int crlfPos = -1;
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == 0x0D && data[i + 1] == 0x0A)
                {
                    crlfPos = i;
                    break;
                }
            }

            if (crlfPos == -1)
                return (null, 0);

            var headerLine = System.Text.Encoding.ASCII.GetString(data, 0, crlfPos);
            var parts = headerLine.Split(' ');

            if (parts.Length < 6 || parts[0] != "PROXY")
                return (null, 0);

            return (parts[2], crlfPos + 2);
        }

        private (string? SourceIP, int BytesConsumed) ParseV2Header(byte[] data)
        {
            if (data.Length < 16)
                return (null, 0);

            byte verCmd = data[12];
            byte fam = data[13];
            int addrLen = (data[14] << 8) | data[15];

            if (data.Length < 16 + addrLen)
                return (null, 0);

            int command = verCmd & 0x0F;
            int family = (fam >> 4) & 0x0F;

            if (command == 0)
                return ("LOCAL", 16 + addrLen);

            if (family == 1 && addrLen >= 12)
            {
                return ($"{data[16]}.{data[17]}.{data[18]}.{data[19]}", 16 + addrLen);
            }
            else if (family == 2 && addrLen >= 36)
            {
                return (FormatIPv6(data, 16), 16 + addrLen);
            }

            return (null, 0);
        }

        private string FormatIPv6(byte[] data, int offset)
        {
            var parts = new string[8];
            for (int i = 0; i < 8; i++)
            {
                parts[i] = ((data[offset + i * 2] << 8) | data[offset + i * 2 + 1]).ToString("x");
            }
            return string.Join(":", parts);
        }

        public void LogParsedIP(string connectionId, string? sourceIP)
        {
            if (!string.IsNullOrEmpty(sourceIP) && sourceIP != "LOCAL")
            {
                ConnectionIPs[connectionId] = sourceIP;
                _logger.LogInfo(I18nService.Instance.T("proxy_protocol.real_ip", connectionId, sourceIP));
            }
        }
    }
}
