// SPDX-License-Identifier: MPL-2.0

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{
    internal sealed class CoreSession : IDisposable
    {
        private readonly NativeGameCore core;
        private readonly Bitmap buffer;
        private readonly object frameLock = new object();
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private Task worker;
        private bool hasFrame, disposed;
        private int keyboard, automatic, sunlight, touch = -1, autoTouch = -1;
        public readonly bool IsDs;
        public readonly int Width, Height;
        public Task Completion { get { return worker; } }

        private CoreSession(NativeGameCore core, bool isDs)
        {
            this.core = core;
            IsDs = isDs;
            Width = isDs ? 256 : 240;
            Height = isDs ? 384 : 160;
            buffer = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        }

        private static string Library(Game game, string root)
        {
            return Path.Combine(root, "engines", game.IsDs ? "gds-ds.dll" : "gds-gba.dll");
        }

        public static void Check(Game game, string root)
        {
            if (!File.Exists(Library(game, root))) throw new IOException("游戏核心缺失。请完整解压便携版。");
        }

        public static async Task<CoreSession> Open(Game game, PreparedRom rom, string root,
            CodeMatch match, CancellationToken stop)
        {
            var core = new NativeGameCore(Library(game, root));
            try
            {
                string state = rom.HasBattleStart
                    ? Path.Combine(Path.GetDirectoryName(rom.Path), game.Id + (game.IsDs ? ".ml1" : "-01.sgm"))
                    : null;
                int kind = game.Id == "boktai2" && game.KnownHash == rom.Hash ? 2 : 0;
                int role = match == null ? 0 : match.IsHost ? 1 : 2;
                int loaded = await Task.Run(() => core.Open(rom.Path, Path.ChangeExtension(rom.Path, ".sav"), state, kind, role));
                stop.ThrowIfCancellationRequested();
                if (loaded < 0) throw new IOException("游戏核心无法读取原作对战起点。");
                if (loaded == 0) throw new IOException("游戏核心无法读取 ROM。");
                if (match != null) await Connect(core, game.IsDs, match, stop);
                var session = new CoreSession(core, game.IsDs);
                session.worker = Task.Run(() => session.Run());
                return session;
            }
            catch
            {
                core.Dispose();
                throw;
            }
        }

        private static async Task Connect(NativeGameCore core, bool ds, CodeMatch match, CancellationToken stop)
        {
            int port = ds ? 7064 : 5738;
            int linked = 0;
            if (match.IsHost)
            {
                linked = await Task.Run(() => core.Link(1, match.LocalAddress, port));
                if (linked < 0) throw new IOException("游戏核心无法开启联机端口。");
            }
            string response = await match.Exchange(match.IsHost ? "CORE-LISTEN" : "CORE-JOIN",
                TimeSpan.FromSeconds(30), stop);
            if (response != (match.IsHost ? "CORE-JOIN" : "CORE-LISTEN"))
                throw new IOException("对方的联机核心状态不匹配。");
            if (!match.IsHost) linked = await Task.Run(() => core.Link(0, match.RemoteAddress, port));
            var limit = Stopwatch.StartNew();
            while (linked == 0 && limit.Elapsed < TimeSpan.FromSeconds(18))
            {
                stop.ThrowIfCancellationRequested();
                linked = await Task.Run(() => core.PollLink());
                if (linked == 0) await Task.Delay(18, stop);
            }
            if (linked != 1) throw new IOException("两边的游戏核心未能建立原作通信。");
            if (await match.Exchange("CORE-LINKED", TimeSpan.FromSeconds(22), stop) != "CORE-LINKED")
                throw new IOException("对方的游戏核心尚未连线。");
        }

        private void Run()
        {
            var pixels = new int[Width * Height];
            var samples = new short[4096];
            double frameTime = IsDs ? 16.716 : 16.743;
            var clock = Stopwatch.StartNew();
            double deadline = 0;
            using (var output = new CoreAudio())
            try
            {
                while (!shutdown.IsCancellationRequested)
                {
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
                            var image = buffer.LockBits(new Rectangle(0, 0, Width, Height),
                                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                            try { Marshal.Copy(pixels, 0, image.Scan0, pixels.Length); }
                            finally { buffer.UnlockBits(image); }
                            hasFrame = true;
                        }
                    }
                    double now = clock.Elapsed.TotalMilliseconds;
                    deadline = Math.Max(deadline + frameTime, now - frameTime);
                    int delay = (int)(deadline - now);
                    if (delay > 0) shutdown.Token.WaitHandle.WaitOne(delay);
                }
            }
            finally { core.Dispose(); }
        }

        public Bitmap Capture()
        {
            lock (frameLock) return hasFrame && !disposed ? (Bitmap)buffer.Clone() : null;
        }

        public void Draw(Graphics graphics, Rectangle target)
        {
            lock (frameLock) if (hasFrame && !disposed) graphics.DrawImage(buffer, target);
        }

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
            try { await Task.Delay(ms, stop); }
            finally { Volatile.Write(ref automatic, 0); }
        }

        public async Task Tap(int x, int y, CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            Volatile.Write(ref autoTouch, ((y - 192) << 8) | x);
            try { await Task.Delay(110, stop); }
            finally { Volatile.Write(ref autoTouch, -1); }
        }

        public Task Stop() { shutdown.Cancel(); return worker; }

        public void Dispose()
        {
            lock (frameLock)
            {
                if (disposed) return;
                disposed = true;
                buffer.Dispose();
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
            module = LoadLibraryEx(path, IntPtr.Zero, 0x1100);
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

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32", ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32")] private static extern bool FreeLibrary(IntPtr module);
    }
}
