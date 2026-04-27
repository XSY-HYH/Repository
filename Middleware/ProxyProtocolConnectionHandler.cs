using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Repository.Services;

namespace Repository
{
    public class ProxyProtocolConnectionHandler : ConnectionHandler
    {
        private readonly ConnectionDelegate _next;
        private readonly ProxyProtocolService _proxyProtocolService;
        private readonly Logger _logger;

        public ProxyProtocolConnectionHandler(ConnectionDelegate next, ProxyProtocolService proxyProtocolService, Logger logger)
        {
            _next = next;
            _proxyProtocolService = proxyProtocolService;
            _logger = logger;
        }

        public override async Task OnConnectedAsync(ConnectionContext context)
        {
            var input = context.Transport.Input;
            var result = await input.ReadAsync();
            var buffer = result.Buffer;

            if (!buffer.IsEmpty)
            {
                var data = buffer.First.ToArray();
                _logger.LogDebug($"[{context.ConnectionId}] Received {data.Length} bytes: {BitConverter.ToString(data)}");
                
                var (sourceIP, bytesConsumed) = _proxyProtocolService.ParseHeader(data);

                if (bytesConsumed > 0 && sourceIP != null)
                {
                    _logger.LogDebug($"[{context.ConnectionId}] PROXY Protocol detected: {sourceIP}, header size: {bytesConsumed} bytes");
                    _proxyProtocolService.ConnectionIPs[context.ConnectionId] = sourceIP;
                    input.AdvanceTo(buffer.GetPosition(bytesConsumed), buffer.End);
                }
                else
                {
                    input.AdvanceTo(buffer.Start, buffer.End);
                }
            }

            await _next(context);
        }
    }
}
