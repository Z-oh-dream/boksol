// SPDX-License-Identifier: MPL-2.0

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{
    internal sealed class CodeRoom : IDisposable
    {
        public const int DiscoveryPort = 39465;
        private const int CodeLength = 6;
        private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        private const string Protocol = "GDS5";
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        public CancellationToken Token { get { return stop.Token; } }

        public static string CreateCode()
        {
            return CreateCode(CodeLength, CodeAlphabet);
        }

        public static string FormatCode(string code)
        {
            return code.Insert(CodeLength / 2, "-");
        }

        internal static string CreateCode(int length, string alphabet)
        {
            var code = new char[length];
            var seed = new byte[1];
            int limit = 256 - 256 % alphabet.Length;
            using (var random = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < code.Length; i++)
                {
                    do { random.GetBytes(seed); } while (seed[0] >= limit);
                    code[i] = alphabet[seed[0] % alphabet.Length];
                }
            }
            return new string(code);
        }

        public static bool TryCode(string input, out string code)
        {
            code = (input ?? String.Empty).Trim().ToUpperInvariant();
            if (code.Length == CodeLength + 1 && (code[3] == '-' || code[3] == ' '))
            {
                string ungrouped = code.Remove(3, 1);
                if (ungrouped.All(IsCodeCharacter)) code = ungrouped;
            }
            return code.Length >= 4 && code.Length <= 16 && code.All(c => IsCodeCharacter(c) || c == '-');
        }

        private static bool IsCodeCharacter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        public async Task<CodeMatch> Find(string code, string game, string hash)
        {
            string ticket = Game.Hash(Encoding.ASCII.GetBytes(Protocol + "|" + code));
            string identity = Guid.NewGuid().ToString("N");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                var listener = new TcpListener(IPAddress.Any, 0);
                using (var discovery = new UdpClient(AddressFamily.InterNetwork))
                using (var sender = new UdpClient(AddressFamily.InterNetwork))
                using (timeout.Token.Register(listener.Stop))
                using (timeout.Token.Register(discovery.Close))
                {
                    discovery.Client.ExclusiveAddressUse = false;
                    discovery.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    discovery.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                    sender.EnableBroadcast = true;
                    IPAddress[] addresses = BroadcastAddresses();
                    Task<TcpClient> incoming = null;
                    Task<UdpReceiveResult> message = null;
                    try
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        listener.Start(4);
                        byte[] announce = Encoding.ASCII.GetBytes(Protocol + "|" + ticket + "|" + identity + "|" +
                            ((IPEndPoint)listener.LocalEndpoint).Port);
                        incoming = listener.AcceptTcpClientAsync();
                        message = discovery.ReceiveAsync();
                        while (true)
                        {
                            timeout.Token.ThrowIfCancellationRequested();
                            foreach (IPAddress address in addresses)
                            {
                                try { await sender.SendAsync(announce, announce.Length, new IPEndPoint(address, DiscoveryPort)); }
                                catch (SocketException) { }
                            }
                            var repeat = Task.Delay(550, timeout.Token);
                            while (!repeat.IsCompleted)
                            {
                                Task next = await Task.WhenAny(incoming, message, repeat);
                                timeout.Token.ThrowIfCancellationRequested();
                                if (next == incoming)
                                {
                                    var candidate = await incoming;
                                    incoming = null;
                                    CodeMatch match = await Handshake(candidate, true, ticket, identity, game, hash, timeout.Token);
                                    if (match != null) return match;
                                    incoming = listener.AcceptTcpClientAsync();
                                }
                                if (next == message)
                                {
                                    UdpReceiveResult packet = await message;
                                    message = discovery.ReceiveAsync();
                                    if (packet.Buffer.Length > 115) continue;
                                    string[] fields = Encoding.ASCII.GetString(packet.Buffer).Split('|');
                                    int peerPort;
                                    Guid peerId;
                                    if (fields.Length != 4 || fields[0] != Protocol || fields[1] != ticket ||
                                        !Guid.TryParseExact(fields[2], "N", out peerId) ||
                                        String.CompareOrdinal(identity, fields[2]) <= 0 ||
                                        !Int32.TryParse(fields[3], out peerPort) || peerPort < 1 || peerPort > 65535)
                                        continue;
                                    var candidate = new TcpClient(AddressFamily.InterNetwork);
                                    using (var connect = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
                                    {
                                        connect.CancelAfter(1200);
                                        using (connect.Token.Register(candidate.Close))
                                        {
                                            try { await candidate.ConnectAsync(packet.RemoteEndPoint.Address, peerPort); }
                                            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
                                            { candidate.Close(); continue; }
                                        }
                                    }
                                    CodeMatch match = await Handshake(candidate, false, ticket, identity, game, hash, timeout.Token);
                                    if (match != null) return match;
                                }
                            }
                        }
                    }
                    catch (Exception) when (timeout.IsCancellationRequested)
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        throw new IOException("配对超时。请确认双方选了同一游戏、填写了同一个联机码，并在同一局域网。");
                    }
                    finally
                    {
                        listener.Stop();
                        ObserveAccept(incoming);
                        Observe(message);
                    }
                }
            }
        }

        private static async Task<CodeMatch> Handshake(TcpClient candidate, bool host, string ticket,
            string identity, string game, string hash, CancellationToken stop)
        {
            CodeMatch match;
            try { match = new CodeMatch(candidate, host); }
            catch (Exception ex) when (ex is SocketException || ex is InvalidOperationException || ex is IOException)
            {
                candidate.Close();
                return null;
            }
            bool keep = false;
            try
            {
                string packet = await match.Exchange(Protocol + "|" + ticket + "|" + identity + "|" + game + "|" + hash,
                    TimeSpan.FromSeconds(3), stop);
                string[] fields = packet.Split('|');
                Guid peerId;
                if (fields.Length != 5 || fields[0] != Protocol || fields[1] != ticket ||
                    !Guid.TryParseExact(fields[2], "N", out peerId)) return null;
                int difference = String.CompareOrdinal(identity, fields[2]);
                if (difference == 0 || difference < 0 != host) return null;
                if (fields[3] != game) throw new InvalidOperationException("已找到联机码，但双方选了不同的作品。");
                if (fields[4] != hash) throw new InvalidOperationException("已找到联机码，但双方 ROM 或汉化补丁不同。");
                keep = true;
                return match;
            }
            catch (IOException) { return null; }
            catch (SocketException) { return null; }
            finally { if (!keep) match.Dispose(); }
        }

        private static async void ObserveAccept(Task<TcpClient> task)
        {
            if (task == null) return;
            try { using (await task) { } }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException) { }
        }

        private static async void Observe(Task<UdpReceiveResult> task)
        {
            if (task == null) return;
            try { await task; }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException) { }
        }

        private static IPAddress[] BroadcastAddresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask != null)
                .Select(unicast =>
                {
                    var subnet = unicast.IPv4Mask.GetAddressBytes();
                    var address = unicast.Address.GetAddressBytes();
                    for (int i = 0; i < address.Length; i++) address[i] |= (byte)~subnet[i];
                    return new IPAddress(address);
                }).Concat(new[] { IPAddress.Broadcast }).Distinct().ToArray();
        }

        public void Dispose() { stop.Cancel(); }
    }

    internal sealed class CodeMatch : IDisposable
    {
        public readonly bool IsHost;
        public string LocalAddress { get { return ((IPEndPoint)peer.Client.LocalEndPoint).Address.ToString(); } }
        public string RemoteAddress { get { return ((IPEndPoint)peer.Client.RemoteEndPoint).Address.ToString(); } }
        private readonly TcpClient peer;
        private readonly NetworkStream stream;

        public CodeMatch(TcpClient peer, bool host)
        {
            this.peer = peer;
            IsHost = host;
            peer.NoDelay = true;
            stream = peer.GetStream();
        }

        public async Task<string> Exchange(string line, TimeSpan wait, CancellationToken stop)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                timeout.CancelAfter(wait);
                using (timeout.Token.Register(peer.Close))
                {
                    try
                    {
                        byte[] outgoing = Encoding.ASCII.GetBytes(line + "\n");
                        await stream.WriteAsync(outgoing, 0, outgoing.Length);
                        var response = new StringBuilder();
                        byte[] b = new byte[1];
                        while (response.Length < 210 && await stream.ReadAsync(b, 0, 1) == 1)
                        {
                            if (b[0] == 10)
                            {
                                string message = response.ToString();
                                if (message.StartsWith("ERROR|", StringComparison.Ordinal))
                                    throw ReadPeerFailure(message.Substring(6));
                                return message;
                            }
                            if (b[0] < 32 || b[0] > 126) break;
                            response.Append((char)b[0]);
                        }
                        throw new IOException("对方断开了配对连接。");
                    }
                    catch (Exception ex) when (timeout.IsCancellationRequested)
                    {
                        stop.ThrowIfCancellationRequested();
                        throw new IOException("对方准备游戏超时。", ex);
                    }
                    catch (IOException ex) when (ex.InnerException is SocketException)
                    {
                        throw new IOException("对方在准备游戏时断开了连接。请查看对方软件显示的失败原因。", ex);
                    }
                }
            }
        }

        public async Task ReportFailure(string reason)
        {
            if (String.IsNullOrWhiteSpace(reason)) return;
            // Keep the report within the existing bounded, ASCII-framed control protocol.
            string brief = reason.Substring(0, Math.Min(42, reason.Length));
            byte[] data = Encoding.ASCII.GetBytes("ERROR|" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(brief)) + "\n");
            try
            {
                using (var deadline = new CancellationTokenSource(500))
                using (deadline.Token.Register(peer.Close))
                    await stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException) { }
        }

        private static IOException ReadPeerFailure(string payload)
        {
            try { return new PeerMatchException("对方准备失败。 " + Encoding.UTF8.GetString(Convert.FromBase64String(payload))); }
            catch (FormatException) { return new PeerMatchException("对方准备游戏失败。"); }
        }

        public void Dispose() { peer.Close(); }
    }

    internal sealed class PeerMatchException : IOException
    {
        public PeerMatchException(string message) : base(message) { }
    }
}
