using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordTelegramFrontier
{
    internal static class FrontierHttp
    {
        private static readonly TimeSpan AddressTimeout = TimeSpan.FromSeconds(5);

        public static HttpClient Create(TimeSpan timeout) => new(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = ConnectAsync
        })
        {
            Timeout = timeout
        };

        private static async ValueTask<System.IO.Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
            Exception last = null;

            foreach (var address in addresses.OrderBy(address => address.AddressFamily == AddressFamily.InterNetworkV6 ? 1 : 0))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    attempt.CancelAfter(AddressTimeout);
                    await socket.ConnectAsync(address, context.DnsEndPoint.Port, attempt.Token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    socket.Dispose();
                    last = ex;
                }
            }

            throw new HttpRequestException($"Could not connect to {context.DnsEndPoint.Host}", last);
        }
    }
}
