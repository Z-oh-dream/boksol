// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
#if !ANDROID
using System.Drawing;
using System.Drawing.Imaging;
#endif
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{
    internal sealed class CoreSession : IDisposable
    {
        private readonly NativeGameCore core;
        private readonly List<RemoteGameLink> remote;
#if ANDROID
        private readonly int[] buffer;
#else
        private readonly Bitmap buffer;
        private readonly Bitmap localizedBuffer;
#endif
        private readonly int[] localizedPixels;
        private readonly object frameLock = new object();
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private Task worker;
        private bool hasFrame, hasLocalizedFrame, disposed;
        private int keyboard, automatic, sunlight, frameNumber, touch = -1, autoTouch = -1;
        public readonly bool IsDs;
        public bool IsRemote { get { return remote != null; } }
        public readonly int Width, Height;
        public Task Completion { get { return worker; } }

        private CoreSession(NativeGameCore core, bool isDs, bool translateDs, List<RemoteGameLink> remote)
        {
            this.core = core;
            this.remote = remote;
            IsDs = isDs;
            Width = isDs ? 256 : 240;
            Height = isDs ? 384 : 160;
            if (translateDs) localizedPixels = new int[Width * Height];
#if ANDROID
            buffer = new int[Width * Height];
#else
            buffer = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            if (translateDs) localizedBuffer = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
#endif
        }

        private static string Library(Game game, string root)
        {
#if ANDROID
            return Path.Combine(root, game.IsDs ? "libgds-ds.so" : "libgds-gba.so");
#else
            return Path.Combine(root, "engines", game.IsDs ? "gds-ds.dll" : "gds-gba.dll");
#endif
        }

        public static void Check(Game game, string root)
        {
            if (!File.Exists(Library(game, root))) throw new IOException("游戏核心缺失，请检查安装包。");
        }

        public static async Task<CoreSession> Open(Game game, PreparedRom rom, string root,
            CodeMatch match, CancellationToken stop)
        {
            var core = new NativeGameCore(Library(game, root));
            List<RemoteGameLink> remote = null;
            try
            {
                string state = rom.HasBattleStart
                    ? Path.Combine(Path.GetDirectoryName(rom.Path), game.Id + (game.IsDs ? ".ml1" : "-01.sgm"))
                    : null;
                int kind = game.Id == "boktai2" && game.KnownHash == rom.Hash ? 2 : 0;
                int role = match?.Seat ?? 0;
                int loaded = await Task.Run(() => core.Open(rom.Path, Path.ChangeExtension(rom.Path, ".sav"), state, kind, role));
                stop.ThrowIfCancellationRequested();
                if (loaded < 0) throw new IOException("游戏核心无法读取原作对战起点。");
                if (loaded == 0) throw new IOException("游戏核心无法读取 ROM。");
                if (match != null)
                {
                    int port = game.IsDs ? 7064 : 5738;
                    if (match.Remote != null)
                    {
                        remote = new List<RemoteGameLink>();
                        IEnumerable<int> peers = match.IsHost ? match.Remote.OtherSeats : new[] { 1 };
                        foreach (int peer in peers)
                            remote.Add(new RemoteGameLink(match.Remote, game.IsDs, match.IsHost, port, peer));
                    }
                    await Connect(core, match, port, remote, stop);
                }
                var session = new CoreSession(core, game.IsDs, game.IsDs && rom.Hash == game.KnownHash, remote);
                session.worker = Task.Factory.StartNew(session.Run, CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
                return session;
            }
            catch
            {
                remote?.ForEach(link => link.Dispose());
                core.Dispose();
                throw;
            }
        }

        private static async Task Connect(NativeGameCore core, CodeMatch match, int port,
            List<RemoteGameLink> remote, CancellationToken stop)
        {
            int linked = 0;
            string address = remote != null ? RemoteGameLink.Loopback :
                (match.IsHost ? match.LocalAddress : match.RemoteAddress);
            int corePort = remote == null || match.IsHost ? port : remote[0].CorePort;
            if (match.IsHost)
            {
                linked = await Task.Run(() => core.Link(match.Count - 1, address, corePort));
                if (linked < 0) throw new IOException("游戏核心无法开启联机端口。");
            }
            if (remote != null)
                foreach (RemoteGameLink connection in remote)
                    await connection.Start();
            string[] responses = await match.ExchangeAll(match.IsHost ? "CORE-LISTEN" : "CORE-JOIN",
                TimeSpan.FromSeconds(30), stop);
            if (match.IsHost ? responses.Any(reply => reply != "CORE-JOIN") :
                responses[0] != "CORE-LISTEN" || responses.Skip(1).Any(reply => reply != "CORE-JOIN"))
                throw new IOException("对方的联机核心状态不匹配。");
            if (!match.IsHost) linked = await Task.Run(() => core.Link(0, address, corePort));
            var limit = Stopwatch.StartNew();
            while (linked == 0 && limit.Elapsed < TimeSpan.FromSeconds(18))
            {
                stop.ThrowIfCancellationRequested();
                if (remote != null)
                    foreach (RemoteGameLink connection in remote)
                        if (connection.Completion.IsCompleted) await connection.Completion;
                linked = await Task.Run(() => core.PollLink());
                if (linked == 0) await Task.Delay(18, stop);
            }
            if (linked != 1) throw new IOException("两边的游戏核心未能建立原作通信。");
            if ((await match.ExchangeAll("CORE-LINKED", TimeSpan.FromSeconds(22), stop)).Any(reply => reply != "CORE-LINKED"))
                throw new IOException("对方的游戏核心尚未连线。");
        }

        private void Run()
        {
            var pixels = new int[Width * Height];
            var samples = new short[4096];
            double frameTime = IsDs ? 16.716 : 16.743;
            var clock = Stopwatch.StartNew();
            double deadline = 0;
            try
            {
                using var output = new CoreAudio();
                while (!shutdown.IsCancellationRequested)
                {
                    if (remote != null)
                        foreach (RemoteGameLink connection in remote)
                            if (connection.Completion.IsCompleted) connection.Completion.GetAwaiter().GetResult();
                    int position = Volatile.Read(ref autoTouch);
                    if (position < 0) position = Volatile.Read(ref touch);
                    int result = core.Step(Volatile.Read(ref keyboard) | Volatile.Read(ref automatic),
                        IsDs ? (position < 0 ? -1 : position & 255) : Volatile.Read(ref sunlight),
                        position < 0 ? -1 : position >> 8, pixels);
                    if (result < 0) return;
                    int length = core.Audio(samples);
                    if (length > 0 && length <= samples.Length) output.Write(samples, length);
                    if (result > 0)
                    {
                        lock (frameLock)
                        {
                            // The original frame remains available to the menu driver.
                            hasLocalizedFrame = localizedPixels != null && DsOfficialText.Apply(pixels, localizedPixels);
#if ANDROID
                            Array.Copy(pixels, buffer, pixels.Length);
#else
                            StorePixels(buffer, pixels);
                            if (hasLocalizedFrame) StorePixels(localizedBuffer, localizedPixels);
#endif
                            hasFrame = true;
                        }
                        if (IsRemote) Interlocked.Increment(ref frameNumber);
                    }
                    double now = clock.Elapsed.TotalMilliseconds;
                    deadline = Math.Max(deadline + frameTime, now - frameTime);
                    int delay = (int)(deadline - now);
                    if (delay > 0) shutdown.Token.WaitHandle.WaitOne(delay);
                }
            }
            finally
            {
                remote?.ForEach(connection => connection.Dispose());
                core.Dispose();
            }
        }

#if ANDROID
        public FramePixels Capture()
        {
            lock (frameLock) return hasFrame && !disposed ? new FramePixels((int[])buffer.Clone(), Width) : null;
        }

        public bool CopyPixels(int[] destination)
        {
            lock (frameLock)
            {
                if (!hasFrame || disposed) return false;
                Array.Copy(hasLocalizedFrame ? localizedPixels : buffer, destination, buffer.Length);
                return true;
            }
        }
#else
        public Bitmap Capture()
        {
            lock (frameLock) return hasFrame && !disposed ? (Bitmap)buffer.Clone() : null;
        }

        public Bitmap CaptureDisplay()
        {
            lock (frameLock)
                return hasFrame && !disposed ? (Bitmap)(hasLocalizedFrame ? localizedBuffer : buffer).Clone() : null;
        }

        public void Draw(Graphics graphics, Rectangle target)
        {
            lock (frameLock) if (hasFrame && !disposed) graphics.DrawImage(hasLocalizedFrame ? localizedBuffer : buffer, target);
        }

        private void StorePixels(Bitmap target, int[] pixels)
        {
            var data = target.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
            finally { target.UnlockBits(data); }
        }
#endif

        public void Input(int keys, int position)
        {
            Volatile.Write(ref keyboard, keys);
            Volatile.Write(ref touch, position);
        }

        public void AdjustSunlight(int amount)
        {
            Volatile.Write(ref sunlight, Math.Max(0, Math.Min(10, Volatile.Read(ref sunlight) + amount)));
        }

        public async Task Press(int mask, int ms, CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            Volatile.Write(ref automatic, mask);
            try { await HoldAutomaticInput(ms, stop); }
            finally { Volatile.Write(ref automatic, 0); }
        }

        public async Task Tap(int x, int y, CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            Volatile.Write(ref autoTouch, ((y - 192) << 8) | x);
            try { await HoldAutomaticInput(110, stop); }
            finally { Volatile.Write(ref autoTouch, -1); }
        }

        private async Task HoldAutomaticInput(int milliseconds, CancellationToken stop)
        {
            int first = Volatile.Read(ref frameNumber);
            await Task.Delay(milliseconds, stop);
            if (!IsRemote) return;
            // A link exchange can hold a remote frame longer than the normal press duration.
            var limit = Stopwatch.StartNew();
            while (Volatile.Read(ref frameNumber) - first < 2 && !worker.IsCompleted &&
                limit.ElapsedMilliseconds < 650)
                await Task.Delay(25, stop);
        }

        public Task Stop() { shutdown.Cancel(); return worker; }

        public void Dispose()
        {
            lock (frameLock)
            {
                if (disposed) return;
                disposed = true;
#if !ANDROID
                buffer.Dispose();
            localizedBuffer?.Dispose();
#endif
            }
            shutdown.Dispose();
        }
    }

    internal sealed class NativeGameCore : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int OpenCore([MarshalAs(UnmanagedType.LPUTF8Str)] string rom,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string save, [MarshalAs(UnmanagedType.LPUTF8Str)] string checkpoint,
            int saveType, int role);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int LinkCore(int host, [MarshalAs(UnmanagedType.LPUTF8Str)] string address, int port);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int PollCore();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int StepCore(int keys, int x, int y, [Out] int[] pixels);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int AudioCore([Out] short[] samples);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void CloseCore();

        private IntPtr module;
        private readonly CloseCore close;
        public readonly OpenCore Open;
        public readonly LinkCore Link;
        public readonly PollCore PollLink;
        public readonly StepCore Step;
        public readonly AudioCore Audio;

        public NativeGameCore(string path)
        {
#if ANDROID
            module = NativeLibrary.Load(path);
#else
            module = LoadLibraryEx(path, IntPtr.Zero, 0x1100);
#endif
            if (module == IntPtr.Zero) throw new IOException("无法加载游戏核心。请检查便携包是否完整。");
            try
            {
                close = Load<CloseCore>("gds_close");
                Open = Load<OpenCore>("gds_open");
                Link = Load<LinkCore>("gds_link");
                PollLink = Load<PollCore>("gds_poll_link");
                Step = Load<StepCore>("gds_step");
                Audio = Load<AudioCore>("gds_audio");
            }
            catch { FreeLibrary(module); module = IntPtr.Zero; throw; }
        }

        private T Load<T>(string name) where T : Delegate
        {
            IntPtr function = GetProcAddress(module, name);
            if (function == IntPtr.Zero) throw new IOException("游戏核心版本不兼容。");
            return Marshal.GetDelegateForFunctionPointer<T>(function);
        }

        public void Dispose()
        {
            if (module == IntPtr.Zero) return;
            try { close(); }
            finally { FreeLibrary(module); module = IntPtr.Zero; }
        }

#if ANDROID
        private static IntPtr GetProcAddress(IntPtr module, string name)
        {
            return NativeLibrary.TryGetExport(module, name, out IntPtr address) ? address : IntPtr.Zero;
        }

        private static void FreeLibrary(IntPtr module) { NativeLibrary.Free(module); }
#else
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32", ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32")] private static extern bool FreeLibrary(IntPtr module);
#endif
    }
}
