using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{
    internal sealed class RemoteRoom
    {
        internal const string SettingsKey = "signaling-server";
        private const string RoomPath = "/v1/room";
        private const int MaxControl = 512, MaxGame = 65508;
        private readonly ClientWebSocket socket;
        private readonly CancellationTokenSource closed = new CancellationTokenSource();
        private readonly SemaphoreSlim writer = new SemaphoreSlim(1, 1);
        private readonly PacketQueue[] control;
        private readonly PacketQueue[] game;
        public int Seat { get; }
        public int Count => game.Length;
        public IEnumerable<int> OtherSeats => Enumerable.Range(1, Count).Where(seat => seat != Seat);
        private int owners = 1;

        private RemoteRoom(ClientWebSocket socket, int seat, int count)
        {
            this.socket = socket;
            Seat = seat;
            control = Enumerable.Range(1, count).Select(_ => new PacketQueue()).ToArray();
            game = Enumerable.Range(1, count).Select(_ => new PacketQueue()).ToArray();
        }

        public static bool TryAddress(string input, out Uri address)
        {
            address = null;
            string value = (input ?? "").Trim();
            if (value.Length == 0) return true;
            if (value.Length > 512 || value.Any(Char.IsWhiteSpace) ||
                !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != "ws" && parsed.Scheme != "wss") || parsed.Host.Length == 0 ||
                parsed.Port == 0 || parsed.UserInfo.Length != 0 || parsed.Fragment.Length != 0) return false;
            address = parsed.AbsolutePath == "/" ? new UriBuilder(parsed) { Path = RoomPath }.Uri : parsed;
            return true;
        }

        public static async Task<CodeMatch> Connect(Uri address, string code, string game, string hash,
            string name, int seats, IProgress<RoomRoster> progress, CancellationToken stop)
        {
            var socket = new ClientWebSocket();
            bool matched = false;
            try
            {
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(12));
                    try { await socket.ConnectAsync(address, deadline.Token).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException)
                    {
                        stop.ThrowIfCancellationRequested();
                        throw new IOException("无法连接信令服务器，请检查地址与网络。", ex);
                    }
                }
                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    wait.CancelAfter(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(5));
                    try
                    {
                        byte[] request = Encoding.ASCII.GetBytes("BOKSOL2|" + code + "|" + game + "|" + hash +
                            "|" + seats + "|" + RoomRoster.EncodeName(name));
                        await socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, wait.Token)
                            .ConfigureAwait(false);
                        RoomRoster roster = null;
                        byte[] lobbyBuffer = new byte[MaxControl];
                        while (true)
                        {
                            var frame = await Receive(socket, lobbyBuffer, wait.Token).ConfigureAwait(false);
                            string reply = frame.Type == WebSocketMessageType.Text ? Encoding.ASCII.GetString(frame.Data) : "";
                            string[] fields = reply.Split('|');
                            switch (reply)
                            {
                                case "ERROR|GAME": throw new IOException("已找到联机码，但双方选了不同的作品。");
                                case "ERROR|ROM": throw new IOException("已找到联机码，但双方 ROM 或汉化补丁不同。");
                                case "ERROR|BUSY": throw new IOException("信令服务器暂时繁忙，请稍后重试。");
                                case "ERROR|FULL": throw new IOException("这个房间已满。");
                                case "ERROR|TIMEOUT": throw new IOException("远程配对超时。请确认双方使用同一信令地址和联机码。");
                            }
                            if (fields[0] == "ROOM" && fields.Length >= 3 &&
                                Int32.TryParse(fields[1], out int capacity) && capacity >= 2 && capacity <= 4 &&
                                fields.Length - 2 <= capacity)
                            {
                                var names = new string[fields.Length - 2];
                                for (int i = 0; i < names.Length; i++)
                                    if (!RoomRoster.TryDecodeName(fields[i + 2], out names[i]))
                                        throw new IOException("房间昵称格式不兼容。");
                                roster = new RoomRoster(capacity, names);
                                progress?.Report(roster);
                                continue;
                            }
                            if (fields[0] == "OK" && fields.Length == 2 && roster != null &&
                                roster.Joined == roster.Capacity && Int32.TryParse(fields[1], out int seat) &&
                                seat >= 1 && seat <= roster.Capacity)
                            {
                                var remote = new RemoteRoom(socket, seat, roster.Capacity);
                                _ = remote.ReceivePackets();
                                matched = true;
                                return new CodeMatch(remote, seat, roster);
                            }
                            throw new IOException("信令服务器的配对协议不兼容。");
                        }
                    }
                    catch (Exception ex) when (wait.IsCancellationRequested)
                    {
                        stop.ThrowIfCancellationRequested();
                        throw new IOException("远程配对超时。请确认双方使用同一信令地址和联机码。", ex);
                    }
                    catch (WebSocketException ex) { throw new IOException("信令服务器在配对时断开了连接。", ex); }
                }
            }
            finally { if (!matched) socket.Dispose(); }
        }

        internal void Retain() { Interlocked.Increment(ref owners); }

        internal void Release()
        {
            if (Interlocked.Decrement(ref owners) != 0) return;
            closed.Cancel();
            socket.Abort();
            socket.Dispose();
        }

        internal Task SendControl(string line, CancellationToken stop)
        {
            return Send(new ArraySegment<byte>(Encoding.ASCII.GetBytes(line)), WebSocketMessageType.Text, stop);
        }

        internal Task SendGame(int destination, ArraySegment<byte> packet, CancellationToken stop)
        {
            byte[] frame = new byte[packet.Count + 1];
            frame[0] = (byte)destination;
            Buffer.BlockCopy(packet.Array, packet.Offset, frame, 1, packet.Count);
            return Send(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, stop);
        }

        internal Task<string[]> ReadControls(CancellationToken stop)
        {
            return Task.WhenAll(OtherSeats.Select(async seat =>
                Encoding.ASCII.GetString(await Read(control[seat - 1], stop).ConfigureAwait(false))));
        }

        internal Task<byte[]> ReadGame(int source, CancellationToken stop) { return Read(game[source - 1], stop); }

        private async Task<byte[]> Read(PacketQueue queue, CancellationToken stop)
        {
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(stop, closed.Token))
            {
                try { return await queue.Take(wait.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                {
                    throw new IOException("远程联机已断开。");
                }
            }
        }

        private async Task Send(ArraySegment<byte> packet, WebSocketMessageType type, CancellationToken stop)
        {
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(stop, closed.Token))
            {
                try
                {
                    await writer.WaitAsync(wait.Token).ConfigureAwait(false);
                    try
                    {
                        wait.Token.ThrowIfCancellationRequested();
                        // Bound a stalled write while allowing the other channel to finish its current frame.
                        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(closed.Token))
                        {
                            deadline.CancelAfter(TimeSpan.FromSeconds(10));
                            await socket.SendAsync(packet, type, true, deadline.Token).ConfigureAwait(false);
                        }
                    }
                    finally { writer.Release(); }
                }
                catch (Exception ex) when (ex is WebSocketException || ex is ObjectDisposedException ||
                    ex is OperationCanceledException && !stop.IsCancellationRequested)
                {
                    throw new IOException("远程联机已断开。", ex);
                }
            }
        }

        private async Task ReceivePackets()
        {
            byte[] buffer = new byte[MaxGame];
            try
            {
                while (!closed.IsCancellationRequested)
                {
                    var frame = await Receive(socket, buffer, closed.Token).ConfigureAwait(false);
                    bool text = frame.Type == WebSocketMessageType.Text;
                    int prefix = text ? 2 : 1;
                    if (frame.Data.Length < prefix || text && (frame.Data.Length > MaxControl || frame.Data[1] != '|'))
                        throw new IOException("远程消息格式错误。");
                    int source = text ? frame.Data[0] - '0' : frame.Data[0];
                    if (source < 1 || source > Count || source == Seat)
                        throw new IOException("远程消息的玩家编号无效。");
                    byte[] payload = new byte[frame.Data.Length - prefix];
                    Buffer.BlockCopy(frame.Data, prefix, payload, 0, payload.Length);
                    (text ? control : game)[source - 1].Put(payload);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is WebSocketException ||
                ex is InvalidOperationException || ex is OperationCanceledException) { }
            finally { closed.Cancel(); }
        }

        private static async Task<(WebSocketMessageType Type, byte[] Data)> Receive(ClientWebSocket socket,
            byte[] buffer, CancellationToken stop)
        {
            int size = 0;
            while (size < buffer.Length)
            {
                var frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, size, buffer.Length - size), stop)
                    .ConfigureAwait(false);
                if (frame.MessageType == WebSocketMessageType.Close) throw new IOException("远程联机已断开。");
                size += frame.Count;
                if (!frame.EndOfMessage) continue;
                byte[] data = new byte[size];
                Buffer.BlockCopy(buffer, 0, data, 0, size);
                return (frame.MessageType, data);
            }
            throw new IOException("远程消息超出长度限制。");
        }

        private sealed class PacketQueue
        {
            private readonly Queue<byte[]> packets = new Queue<byte[]>();
            private readonly SemaphoreSlim ready = new SemaphoreSlim(0);
            private int bytes;

            internal void Put(byte[] data)
            {
                lock (packets)
                {
                    if (packets.Count == 256 || bytes + data.Length > 512 * 1024)
                        throw new IOException("远程联机接收队列已满。");
                    packets.Enqueue(data);
                    bytes += data.Length;
                }
                ready.Release();
            }

            internal async Task<byte[]> Take(CancellationToken stop)
            {
                if (!ready.Wait(0)) await ready.WaitAsync(stop).ConfigureAwait(false);
                lock (packets)
                {
                    byte[] data = packets.Dequeue();
                    bytes -= data.Length;
                    return data;
                }
            }
        }
    }
}
