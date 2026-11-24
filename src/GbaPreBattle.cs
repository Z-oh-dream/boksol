using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{
    internal static class GbaPreBattle
    {
        public static Task<bool> OpenRaceSetup(CoreSession session, CancellationToken stop)
        {
            return StartPreparedSession(session, true,
                image => CloseTo(image, 4, 4, 74, 140, 189) && CloseTo(image, 222, 7, 255, 255, 231) &&
                    CloseTo(image, 4, 31, 239, 231, 189), stop,
                image => CloseTo(image, 4, 4, 74, 140, 189) && CloseTo(image, 222, 7, 74, 140, 189) &&
                    CloseTo(image, 27, 77, 181, 24, 24) && CloseTo(image, 218, 79, 181, 24, 24));
        }

        public static Task<bool> StartBattle(CoreSession session, bool isHost, CancellationToken stop)
        {
            return StartPreparedSession(session, isHost,
                image => CloseTo(image, 35, 12, 0, 255, 0) && CloseTo(image, 42, 20, 255, 214, 0) &&
                    CloseTo(image, 4, 21, 255, 255, 107), stop,
                image => CloseTo(image, 4, 5, 181, 107, 107) && CloseTo(image, 49, 48, 255, 74, 74) &&
                    CloseTo(image, 148, 48, 255, 74, 74) && CloseTo(image, 25, 76, 206, 206, 206),
                image => CloseTo(image, 4, 5, 181, 107, 107) && CloseTo(image, 90, 49, 255, 255, 231) &&
                    CloseTo(image, 189, 49, 255, 255, 231) && CloseTo(image, 25, 76, 231, 156, 132) &&
                    CloseTo(image, 11, 100, 247, 198, 165) && CloseTo(image, 10, 108, 231, 156, 132));
        }

        private static async Task<bool> StartPreparedSession(CoreSession session, bool isHost,
            Func<Bitmap, bool> opened, CancellationToken stop, params Func<Bitmap, bool>[] confirmations)
        {
            int step = -1;
            long stableSince = -1;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 20000 && !session.Completion.IsCompleted)
            {
                stop.ThrowIfCancellationRequested();
                using (Bitmap image = session.Capture())
                {
                    if (image != null)
                    {
                        if (opened(image)) return true;
                        int current = isHost ? Array.FindIndex(confirmations, test => test(image)) : -1;
                        if (current != step)
                        {
                            step = current;
                            stableSince = watch.ElapsedMilliseconds;
                        }
                        // The WAIT cards appear before the GBA games enable the Start key.
                        if (step >= 0 && watch.ElapsedMilliseconds - stableSince >= 2900)
                        {
                            await session.Press(8, 160, stop);
                            stableSince = watch.ElapsedMilliseconds;
                        }
                    }
                }
                await Task.Delay(120, stop);
            }
            return false;
        }

        private static bool CloseTo(Bitmap frame, int x, int y, int r, int g, int b)
        {
            return Difference(Pixel(frame, x, y), Color.FromArgb(r, g, b)) <= 18;
        }

        private static Color Pixel(Bitmap frame, int x, int y)
        {
            return frame.GetPixel(x, y);
        }

        private static int Difference(Color a, Color b)
        {
            return Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
        }

    }
}
