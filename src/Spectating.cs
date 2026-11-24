using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{
    internal sealed class Spectating : IDisposable
    {
        public const int Port = 39466;
        private const int CodeLength = 8;
        private const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const int MaxViewers = 4;
        private const string Protocol = "GDSV1 ";
        private readonly TcpListener listener = new TcpListener(IPAddress.Any, Port);
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        private readonly CoreSession session;
        private readonly string code;
        private volatile byte[] latest;
        public readonly Task Completion;

        public static string CreateCode() { return CodeRoom.CreateCode(CodeLength, CodeAlphabet); }

        public static bool IsValidCode(string code)
        {
            return code.Length == CodeLength && code.All(c => CodeAlphabet.IndexOf(c) >= 0);
        }

        public Spectating(CoreSession session, string code)
        {
            this.session = session;
            this.code = code;
            listener.Start(MaxViewers);
            Completion = Task.Run(() => Run());
        }

        private async Task Run()
        {
            Task accept = Accept();
            try
            {
                while (!stop.IsCancellationRequested && !session.Completion.IsCompleted && !accept.IsCompleted)
                {
                    latest = GameCapture.Frame(session);
                    await Task.Delay(100, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            finally { Dispose(); }
            await accept;
        }

        private async Task Accept()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient peer;
                try { peer = await listener.AcceptTcpClientAsync(); }
                catch (Exception) when (stop.IsCancellationRequested) { return; }
                lock (clients)
                {
                    if (stop.IsCancellationRequested || clients.Count >= MaxViewers)
                    {
                        peer.Close();
                        continue;
                    }
                    clients.Add(peer);
                }
                _ = Serve(peer);
            }
        }

        private async Task Serve(TcpClient peer)
        {
            try
            {
                using (peer)
                using (var stream = peer.GetStream())
                {
                    byte[] expected = Encoding.ASCII.GetBytes(Protocol + code);
                    var request = new byte[expected.Length];
                    await Timed(peer, () => ReadExactly(stream, request));
                    for (int i = 0; i < request.Length; i++)
                    {
                        if (request[i] == expected[i]) continue;
                        await Timed(peer, () => Send(stream, -1, null));
                        return;
                    }
                    byte[] previous = null;
                    while (!stop.IsCancellationRequested)
                    {
                        byte[] frame = latest;
                        if (frame == previous) frame = null;
                        await Timed(peer, () => Send(stream, frame == null ? 0 : frame.Length, frame));
                        previous = frame ?? previous;
                        await Task.Delay(100, stop.Token);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is OperationCanceledException) { }
            finally { lock (clients) clients.Remove(peer); }
        }

        public static async Task Watch(string address, string code, Action<byte[]> picture, CancellationToken stop)
        {
            using (var peer = new TcpClient(AddressFamily.InterNetwork))
            using (stop.Register(peer.Close))
            {
                await Timed(peer, () => peer.ConnectAsync(IPAddress.Parse(address), Port));
                using (var stream = peer.GetStream())
                {
                    var request = Encoding.ASCII.GetBytes(Protocol + code);
                    await Timed(peer, () => stream.WriteAsync(request, 0, request.Length));
                    byte[] header = new byte[4];
                    while (!stop.IsCancellationRequested)
                    {
                        await Timed(peer, () => ReadExactly(stream, header));
                        int length = BitConverter.ToInt32(header, 0);
                        if (length == -1) throw new IOException("观战码有误。");
                        if (length < 0 || length > GameCapture.MaxFrame) throw new IOException("观战画面格式错误。");
                        if (length == 0) { picture(null); continue; }
                        var frame = new byte[length];
                        await Timed(peer, () => ReadExactly(stream, frame));
                        picture(frame);
                    }
                }
            }
        }

        private static async Task Timed(TcpClient peer, Func<Task> action)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7)))
            using (timeout.Token.Register(peer.Close))
            {
                try { await action().ConfigureAwait(false); }
                catch (Exception ex) when (timeout.IsCancellationRequested)
                { throw new IOException("观战连接超时。", ex); }
            }
        }

        private static async Task Send(NetworkStream stream, int length, byte[] frame)
        {
            byte[] header = BitConverter.GetBytes(length);
            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            if (frame != null) await stream.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false);
        }

        private static async Task ReadExactly(NetworkStream stream, byte[] bytes)
        {
            int position = 0;
            while (position < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes, position, bytes.Length - position).ConfigureAwait(false);
                if (read == 0) throw new IOException("观战连接已关闭。");
                position += read;
            }
        }

        public void Dispose()
        {
            lock (clients)
            {
                if (stop.IsCancellationRequested) return;
                stop.Cancel();
                listener.Stop();
                foreach (var peer in new List<TcpClient>(clients)) peer.Close();
            }
        }
    }
}
