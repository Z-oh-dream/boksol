using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{
    internal sealed class Recording
    {
        internal const int Width = 768, Height = 576;
        private const int Fps = 15;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        public readonly Task<int> Completion;

        public Recording(CoreSession session, string destination)
        {
            Completion = Task.Run(() => Capture(session, destination, stop.Token));
        }

        public void Stop() { stop.Cancel(); }

        private static int Capture(CoreSession session, string destination, CancellationToken token)
        {
            string partial = destination + ".partial";
            try
            {
                int frames;
                using (var avi = new Movie(partial))
                {
                    var clock = Stopwatch.StartNew();
                    while (!token.IsCancellationRequested && !session.Completion.IsCompleted &&
                           avi.FrameCount < Fps * 60 * 20 && avi.Length < 1500000000)
                    {
                        byte[] frame = GameCapture.Frame(session);
                        if (frame != null) avi.Frame(frame);
                        int wait = Math.Max(1, 1000 / Fps - (int)clock.ElapsedMilliseconds);
                        token.WaitHandle.WaitOne(wait);
                        clock.Restart();
                    }
                    frames = avi.FrameCount;
                    avi.Finish();
                }
                if (frames > 0) File.Move(partial, destination);
                return frames;
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }

        public static Image Preview(string path)
        {
            using (var file = new BinaryReader(File.OpenRead(path)))
            {
                if (new string(file.ReadChars(4)) != "RIFF") return null;
                file.BaseStream.Position = 12;
                while (file.BaseStream.Position + 12 <= file.BaseStream.Length)
                {
                    string chunk = new string(file.ReadChars(4));
                    uint length = file.ReadUInt32();
                    long start = file.BaseStream.Position;
                    if (length < 4 || start + length > file.BaseStream.Length) return null;
                    if (chunk == "LIST" && new string(file.ReadChars(4)) == "movi")
                    {
                        if (new string(file.ReadChars(4)) != "00dc") return null;
                        uint size = file.ReadUInt32();
                        if (size > GameCapture.MaxFrame || size > length - 12) return null;
                        return GameCapture.Decode(file.ReadBytes((int)size));
                    }
                    file.BaseStream.Position = start + length + (length & 1);
                }
            }
            return null;
        }

        private sealed class Movie : IDisposable
        {
            private readonly BinaryWriter file;
            private readonly List<Tuple<int, int>> index = new List<Tuple<int, int>>();
            private readonly long riffSize, movieSize, movieId, framesMain, framesStream, bufferMain, bufferStream;
            private int maximumFrame;
            public int FrameCount { get { return index.Count; } }
            public long Length { get { return file.BaseStream.Position; } }

            public Movie(string path)
            {
                file = new BinaryWriter(File.Open(path, FileMode.CreateNew));
                riffSize = Start("RIFF"); Four("AVI ");
                long header = Start("LIST"); Four("hdrl");
                Four("avih"); Int(56);
                Int(1000000 / Fps); Int(0); Int(0); Int(16);
                framesMain = Length; Int(0); Int(0); Int(1);
                bufferMain = Length; Int(0); Int(Width); Int(Height);
                for (int i = 0; i < 4; i++) Int(0);
                long stream = Start("LIST"); Four("strl");
                Four("strh"); Int(56); Four("vids"); Four("MJPG");
                Int(0); Int(0); Int(0); Int(1); Int(Fps); Int(0);
                framesStream = Length; Int(0);
                bufferStream = Length; Int(0); Int(-1); Int(0);
                file.Write((short)0); file.Write((short)0); file.Write((short)Width); file.Write((short)Height);
                Four("strf"); Int(40); Int(40); Int(Width); Int(Height);
                file.Write((short)1); file.Write((short)24); Four("MJPG");
                Int(Width * Height * 3);
                for (int i = 0; i < 4; i++) Int(0);
                End(stream); End(header);
                movieSize = Start("LIST"); movieId = Length; Four("movi");
            }

            public void Frame(byte[] jpg)
            {
                index.Add(Tuple.Create((int)(Length - movieId), jpg.Length));
                maximumFrame = Math.Max(maximumFrame, jpg.Length);
                Four("00dc"); Int(jpg.Length); file.Write(jpg);
                if ((jpg.Length & 1) != 0) file.Write((byte)0);
            }

            public void Finish()
            {
                End(movieSize);
                Four("idx1"); Int(index.Count * 16);
                foreach (var frame in index)
                {
                    Four("00dc"); Int(16); Int(frame.Item1); Int(frame.Item2);
                }
                End(riffSize);
                Update(framesMain, index.Count); Update(framesStream, index.Count);
                Update(bufferMain, maximumFrame); Update(bufferStream, maximumFrame);
            }

            private long Start(string id) { Four(id); long at = Length; Int(0); return at; }
            private void End(long at) { Update(at, (int)(Length - at - 4)); }
            private void Four(string value) { file.Write(Encoding.ASCII.GetBytes(value)); }
            private void Int(int value) { file.Write(value); }
            private void Update(long at, int value)
            {
                long end = Length;
                file.BaseStream.Position = at;
                Int(value);
                file.BaseStream.Position = end;
            }
            public void Dispose() { file.Dispose(); }
        }
    }

    internal static class GameCapture
    {
        public const int MaxFrame = 512 * 1024;
        public static byte[] Frame(CoreSession session)
        {
            using (var frame = session.Capture())
            {
                if (frame == null) return null;
                using (var scaled = new Bitmap(Recording.Width, Recording.Height, PixelFormat.Format24bppRgb))
                using (var canvas = Graphics.FromImage(scaled))
                using (var output = new MemoryStream())
                {
                    double scale = Math.Min((double)scaled.Width / frame.Width, (double)scaled.Height / frame.Height);
                    int width = (int)(frame.Width * scale), height = (int)(frame.Height * scale);
                    canvas.Clear(Color.Black);
                    canvas.DrawImage(frame, (scaled.Width - width) / 2, (scaled.Height - height) / 2, width, height);
                    scaled.Save(output, ImageFormat.Jpeg);
                    return output.Length <= MaxFrame ? output.ToArray() : null;
                }
            }
        }

        public static Image Decode(byte[] bytes)
        {
            using (var memory = new MemoryStream(bytes, false))
            using (var image = Image.FromStream(memory, false, true))
            {
                if (image.Width != Recording.Width || image.Height != Recording.Height)
                    throw new InvalidDataException("视频的画面尺寸不匹配。");
                return new Bitmap(image);
            }
        }

    }
}
