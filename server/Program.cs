using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

var app = WebApplication.CreateBuilder(args).Build();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20),
    KeepAliveTimeout = TimeSpan.FromSeconds(15)
});
var rooms = new RoomRelay(app.Lifetime.ApplicationStopping);
app.MapGet("/healthz", () => Results.Text("ok"));
app.MapGet("/v1/room", rooms.Handle);
await app.RunAsync();

internal sealed class RoomRelay(CancellationToken shuttingDown)
{
    private const int MaxControl = 512, MaxGame = 65508;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Regex Registration = new(
        @"\ABOKSOL2\|([A-Z0-9-]{4,16})\|(boktai2|boktai3|boktaids)\|([0-9a-f]{64})\|([234])\|([A-Za-z0-9+/=]{0,64})\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly SemaphoreSlim connections = new(256);
    private readonly Dictionary<string, Lobby> rooms = new(StringComparer.Ordinal);

    public async Task Handle(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var disconnected = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, shuttingDown);
        if (!connections.Wait(0))
        {
            await Reject(socket, "ERROR|BUSY", disconnected.Token);
            return;
        }
        Peer? peer = null;
        try
        {
            JoinRequest? request = await ReadRegistration(socket, disconnected.Token);
            if (request == null)
            {
                await Reject(socket, "ERROR|PROTOCOL", disconnected.Token);
                return;
            }

            peer = new Peer(socket, request, disconnected.Token);
            string? error = Arrive(peer);
            if (error != null)
            {
                await Reject(socket, error, disconnected.Token);
                return;
            }

            try
            {
                await WaitForStart(peer);
            }
            catch (TimeoutException)
            {
                await Reject(socket, "ERROR|TIMEOUT", disconnected.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException) { }
        finally
        {
            if (peer != null)
            {
                RemoveWaiting(peer);
                disconnected.Cancel();
                socket.Abort();
                await Observe(peer.Incoming);
            }
            connections.Release();
        }
    }

    private static async Task<JoinRequest?> ReadRegistration(WebSocket socket, CancellationToken stop)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        byte[] buffer = new byte[MaxControl];
        Frame? first = await ReadFrame(socket, buffer, deadline.Token);
        if (first is not { Type: WebSocketMessageType.Text } message) return null;
        var fields = Registration.Match(Encoding.ASCII.GetString(buffer, 0, message.Length)).Groups;
        if (!fields[0].Success) return null;
        string game = fields[2].Value, encodedName = fields[5].Value;
        int seats = fields[4].Value[0] - '0';
        if (game == "boktai3" && seats != 2) return null;
        try
        {
            string name = Utf8.GetString(Convert.FromBase64String(encodedName));
            if (name.Length > 16 || name != name.Trim() || name.Any(Char.IsControl)) return null;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { return null; }
        return new JoinRequest(fields[1].Value, game, fields[3].Value, seats, encodedName);
    }

    private string? Arrive(Peer peer)
    {
        var request = peer.Request;
        lock (rooms)
        {
            if (!rooms.TryGetValue(request.Code, out var lobby))
            {
                lobby = new Lobby(request.Game, request.Hash, request.Seats);
                rooms.Add(request.Code, lobby);
            }
            if (lobby.Game != request.Game) return "ERROR|GAME";
            if (lobby.Hash != request.Hash) return "ERROR|ROM";
            if (lobby.Session != null) return "ERROR|FULL";

            lobby.Players.RemoveAll(player => player.Incoming.IsCompleted || player.Stop.IsCancellationRequested);
            lobby.Players.Add(peer);
            if (lobby.Players.Count == lobby.Capacity)
            {
                lobby.Session = new RoomSession(lobby.Players.ToArray());
                for (int i = 0; i < lobby.Players.Count; i++) lobby.Players[i].Seat = i + 1;
                _ = RunSession(request.Code, lobby.Session);
            }
            PublishRoster(lobby);
            return null;
        }
    }

    private void RemoveWaiting(Peer peer)
    {
        lock (rooms)
        {
            if (!rooms.TryGetValue(peer.Request.Code, out var lobby) || lobby.Session != null ||
                !lobby.Players.Remove(peer)) return;
            if (lobby.Players.Count == 0) rooms.Remove(peer.Request.Code);
            else PublishRoster(lobby);
        }
    }

    private static void PublishRoster(Lobby lobby)
    {
        string text = "ROOM|" + lobby.Capacity + "|" +
            String.Join("|", lobby.Players.Select(player => player.Request.EncodedName));
        foreach (Peer player in lobby.Players) player.Updates.Writer.TryWrite(new RoomUpdate(text, lobby.Session));
    }

    private static async Task WaitForStart(Peer peer)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(peer.Stop);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            while (true)
            {
                Task<RoomUpdate> update = peer.Updates.Reader.ReadAsync(deadline.Token).AsTask();
                if (await Task.WhenAny(update, peer.Incoming) == peer.Incoming && !update.IsCompletedSuccessfully)
                    return;
                var next = await update;
                await SendText(peer.Socket, next.Roster, deadline.Token);
                if (next.Session == null) continue;
                await SendText(peer.Socket, "OK|" + peer.Seat, deadline.Token);
                peer.Ready.TrySetResult();
                await next.Session.Finished.Task.WaitAsync(peer.Stop);
                return;
            }
        }
        catch (OperationCanceledException) when (!peer.Stop.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    private async Task RunSession(string code, RoomSession session)
    {
        Peer[] members = session.Members;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(members.Select(peer => peer.Stop).ToArray());
        try
        {
            await Task.WhenAll(members.Select(peer => peer.Ready.Task)).WaitAsync(stop.Token);
            Task[] pumps = members.Select(peer => Forward(peer, members, stop.Token)).ToArray();
            await Task.WhenAny(pumps);
            stop.Cancel();
            await Task.WhenAll(pumps.Select(Observe));
        }
        catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException) { }
        finally
        {
            lock (rooms)
            {
                if (rooms.TryGetValue(code, out var lobby) && lobby.Session == session) rooms.Remove(code);
            }
            session.Finished.TrySetResult();
        }
    }

    private static async Task Forward(Peer source, Peer[] members, CancellationToken stop)
    {
        Frame? packet = await source.Incoming.WaitAsync(stop);
        while (packet is Frame message)
        {
            if (message.Type == WebSocketMessageType.Binary)
            {
                if (message.Length < 1) throw new IOException("Missing destination seat.");
                int destination = source.Buffer[0];
                if (destination < 1 || destination > members.Length || destination == source.Seat ||
                    source.Seat != 1 && destination != 1)
                    throw new IOException("Invalid destination seat.");
                source.Buffer[0] = (byte)source.Seat;
                await members[destination - 1].Write(source.Buffer.AsMemory(0, message.Length), message.Type, stop);
            }
            else
            {
                var tagged = new byte[message.Length + 2];
                tagged[0] = (byte)('0' + source.Seat);
                tagged[1] = (byte)'|';
                source.Buffer.AsSpan(0, message.Length).CopyTo(tagged.AsSpan(2));
                await Task.WhenAll(members.Where(peer => peer != source).Select(peer => peer.Write(tagged, message.Type, stop)));
            }
            packet = await ReadFrame(source.Socket, source.Buffer, stop);
        }
    }

    private static async Task Reject(WebSocket socket, string error, CancellationToken stop)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await SendText(socket, error, deadline.Token);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", deadline.Token);
        }
        catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException) { }
    }

    private static Task SendText(WebSocket socket, string text, CancellationToken stop) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes(text)), WebSocketMessageType.Text, true, stop);

    private static async Task<Frame?> ReadFrame(WebSocket socket, Memory<byte> buffer, CancellationToken stop)
    {
        int length = 0;
        while (length < buffer.Length)
        {
            var chunk = await socket.ReceiveAsync(buffer[length..], stop);
            if (chunk.MessageType == WebSocketMessageType.Close) return null;
            length += chunk.Count;
            if (chunk.MessageType == WebSocketMessageType.Text && length > MaxControl - 2)
                throw new IOException("Control message exceeds the limit.");
            if (chunk.EndOfMessage) return new Frame(chunk.MessageType, length);
        }
        throw new IOException("Message exceeds the limit.");
    }

    private static async Task Observe(Task task)
    {
        try { await task; }
        catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException) { }
    }

    private readonly record struct Frame(WebSocketMessageType Type, int Length);
    private sealed record JoinRequest(string Code, string Game, string Hash, int Seats, string EncodedName);
    private sealed record RoomUpdate(string Roster, RoomSession? Session);

    private sealed class Lobby(string game, string hash, int capacity)
    {
        public readonly string Game = game, Hash = hash;
        public readonly int Capacity = capacity;
        public readonly List<Peer> Players = new(4);
        public RoomSession? Session;
    }

    private sealed class Peer
    {
        public readonly WebSocket Socket;
        public readonly JoinRequest Request;
        public readonly CancellationToken Stop;
        public readonly byte[] Buffer = new byte[MaxGame];
        public readonly Task<Frame?> Incoming;
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly Channel<RoomUpdate> Updates = Channel.CreateBounded<RoomUpdate>(
            new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        private readonly SemaphoreSlim writer = new(1, 1);
        public int Seat;

        public Peer(WebSocket socket, JoinRequest request, CancellationToken stop)
        {
            Socket = socket;
            Request = request;
            Stop = stop;
            Incoming = ReadFrame(socket, Buffer, stop);
        }

        public async Task Write(ReadOnlyMemory<byte> data, WebSocketMessageType type, CancellationToken stop)
        {
            await writer.WaitAsync(stop);
            try { await Socket.SendAsync(data, type, true, stop); }
            finally { writer.Release(); }
        }
    }

    private sealed record RoomSession(Peer[] Members)
    {
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
