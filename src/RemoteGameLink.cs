using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{
    internal sealed class RemoteGameLink : IDisposable
    {
        private readonly RemoteRoom room;
        private readonly bool host;
        private readonly int peer;
        private readonly TcpListener listener;
        private readonly UdpClient udp;
        private TcpClient hostSocket;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private int disposed;
        public const string Loopback = "127.0.0.1";
        public int CorePort { get; }
        public Task Completion { get; private set; }

        public RemoteGameLink(RemoteRoom room, bool ds, bool host, int hostPort, int peer)
        {
            this.room = room;
            this.host = host;
            this.peer = peer;
            CorePort = hostPort;
            if (ds)
            {
                udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                if (host) udp.Connect(IPAddress.Loopback, hostPort);
                else CorePort = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
            }
            else if (!host)
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(1);
                CorePort = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            room.Retain();
        }

        public async Task Start()
        {
            if (host && udp == null)
            {
                hostSocket = new TcpClient(AddressFamily.InterNetwork);
                await hostSocket.ConnectAsync(IPAddress.Loopback, CorePort).ConfigureAwait(false);
            }
            Completion = Task.Run(async () =>
            {
                try
                {
                    if (udp != null) await RunUdp().ConfigureAwait(false);
                    else await RunTcp().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException ||
                    ex is ObjectDisposedException || ex is OperationCanceledException)
                {
                    if (Volatile.Read(ref disposed) == 0) throw new IOException("远程游戏连线已断开。", ex);
                }
            });
        }

        private async Task RunTcp()
        {
            using (TcpClient core = host ? hostSocket :
                await listener.AcceptTcpClientAsync().ConfigureAwait(false))
            using (stop.Token.Register(core.Close))
            {
                core.NoDelay = true;
                var stream = core.GetStream();
                await Relay(PumpTcp(stream), DeliverTcp(stream), core.Close).ConfigureAwait(false);
            }
        }

        private async Task PumpTcp(NetworkStream stream)
        {
            byte[] buffer = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, stop.Token).ConfigureAwait(false);
                if (read == 0) throw new IOException("本地 GBA 核心已断开。");
                await room.SendGame(peer, new ArraySegment<byte>(buffer, 0, read), stop.Token).ConfigureAwait(false);
            }
        }

        private async Task DeliverTcp(NetworkStream stream)
        {
            while (true)
            {
                byte[] data = await room.ReadGame(peer, stop.Token).ConfigureAwait(false);
                await stream.WriteAsync(data, 0, data.Length, stop.Token).ConfigureAwait(false);
            }
        }

        private async Task RunUdp()
        {
            var source = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (stop.Token.Register(() => source.TrySetCanceled()))
                await Relay(PumpUdp(source), DeliverUdp(source.Task), udp.Close).ConfigureAwait(false);
        }

        private async Task PumpUdp(TaskCompletionSource<IPEndPoint> source)
        {
            IPEndPoint core = null;
            while (true)
            {
                var packet = await udp.ReceiveAsync().ConfigureAwait(false);
                if (core == null)
                {
                    core = packet.RemoteEndPoint;
                    source.TrySetResult(core);
                }
                if (core.Equals(packet.RemoteEndPoint))
                    await room.SendGame(peer, new ArraySegment<byte>(packet.Buffer), stop.Token).ConfigureAwait(false);
            }
        }

        private async Task DeliverUdp(Task<IPEndPoint> source)
        {
            IPEndPoint core = host ? null : await source.ConfigureAwait(false);
            while (true)
            {
                byte[] packet = await room.ReadGame(peer, stop.Token).ConfigureAwait(false);
                await udp.SendAsync(packet, packet.Length, core).ConfigureAwait(false);
            }
        }

        private async Task Relay(Task outbound, Task inbound, Action closeSocket)
        {
            Task first = await Task.WhenAny(outbound, inbound).ConfigureAwait(false);
            try { await first.ConfigureAwait(false); }
            finally
            {
                closeSocket();
                // Stop the other pump without cancelling pending control messages.
                stop.Cancel();
                try { await (first == outbound ? inbound : outbound).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException || ex is SocketException ||
                    ex is ObjectDisposedException || ex is OperationCanceledException) { }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            stop.Cancel();
            listener?.Stop();
            udp?.Close();
            hostSocket?.Close();
            room.Release();
        }
    }
}
