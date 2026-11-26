// SPDX-License-Identifier: MPL-2.0

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Boksol
{


    internal enum DsBattleEntry { Manual, NeedsSave, OpenedLink, GuestLobby, Rules, Loadout }

    internal static class DsPreBattle
    {
        private enum Page
        {
            Unknown, Mode, Save, Groups, ConfirmGroup, Lobby, ConfirmEnrollment,
            Rules, Loadout
        }

        public static async Task<DsBattleEntry> TryEnter(Game game, PreparedRom rom, CoreSession session, int slot,
            bool isHost, CancellationToken stop)
        {
            if (!game.IsDs || rom.Hash != game.KnownHash || slot < 1 || slot > 4) return DsBattleEntry.Manual;
            if (!File.Exists(Path.ChangeExtension(rom.Path, ".sav"))) return DsBattleEntry.NeedsSave;

            var watch = Stopwatch.StartNew();
            const int confirmKey = 1, downKey = 128, leftKey = 32, rightKey = 16;
            bool checkpoint = false, matching = false, choseGroup = false;
            Page previousAction = Page.Unknown;
            int repeated = 0;

            while (watch.ElapsedMilliseconds < 58000 && !session.Completion.IsCompleted)
            {
                stop.ThrowIfCancellationRequested();
                using (Frame frame = Capture(session))
                {
                    if (frame != null)
                    {
                        Page page = Recognize(frame);
                        if (page == Page.Mode) checkpoint = true;
                        if (!checkpoint)
                        {
                            if (watch.ElapsedMilliseconds > 1800) return DsBattleEntry.Manual;
                        }
                        else
                        {
                            Point point = Point.Empty;
                            int key = 0;
                            switch (page)
                            {
                                case Page.Mode:
                                    key = frame.Count(30, 247, 180, 4, PaleRed) > 90 ? confirmKey : downKey;
                                    point = new Point(128, 239);
                                    break;
                                case Page.Save:
                                    int active = FindSlot(frame);
                                    if (active == slot)
                                    {
                                        if (frame.Count(18, 282, 44, 46, color => !Cream(color)) < 100)
                                            return DsBattleEntry.NeedsSave;
                                        key = confirmKey;
                                    }
                                    else if (active > 0) key = active < slot ? rightKey : leftKey;
                                    point = new Point(45 + (slot - 1) * 55, 240);
                                    break;
                                case Page.Groups:
                                    matching = true;
                                    int row = isHost ? -1 : FindGroup(frame);
                                    if (isHost || row >= 0)
                                    {
                                        choseGroup = true;
                                        point = isHost ? new Point(178, 335) : new Point(128, 231 + row * 24);
                                    }
                                    break;
                                case Page.ConfirmGroup:
                                    if (choseGroup)
                                    {
                                        key = confirmKey;
                                        point = new Point(159, 375);
                                    }
                                    break;
                                case Page.Lobby:
                                    if (!isHost) return DsBattleEntry.GuestLobby;
                                    matching = true;
                                    if (HasOpponent(frame))
                                    {
                                        key = frame.Count(156, 326, 70, 3, PaleRed) > 50 ? confirmKey : rightKey;
                                        point = new Point(188, 334);
                                    }
                                    break;
                                case Page.ConfirmEnrollment:
                                    if (isHost && HasOpponent(frame))
                                    {
                                        key = confirmKey;
                                        point = new Point(159, 375);
                                    }
                                    break;
                                case Page.Rules:
                                    return DsBattleEntry.Rules;
                                case Page.Loadout:
                                    return DsBattleEntry.Loadout;
                            }

                            if (!point.IsEmpty)
                            {
                                if (previousAction != page) repeated = 0;
                                previousAction = page;
                                if (++repeated > (page == Page.Save ? 5 : 4))
                                    return matching ? DsBattleEntry.OpenedLink : DsBattleEntry.Manual;
                                if (key > 0) await session.Press(key, 125, stop);
                                else await session.Tap(point.X, point.Y, stop);
                                await Task.Delay(300, stop);
                            }
                        }
                    }
                }
                await Task.Delay(135, stop);
            }
            return matching ? DsBattleEntry.OpenedLink : DsBattleEntry.Manual;
        }

        private static Page Recognize(Frame frame)
        {
            if (frame.Near(14, 187, 0) && frame.Near(236, 170, 0))
            {
                if (frame.Near(4, 202, 0x187151) && frame.Near(251, 340, 0x187151) &&
                    frame.Near(125, 322, 0xaaebd3) && frame.Near(231, 266, 0xfbfbe3))
                    return Page.Mode;
                if (frame.Near(12, 197, 0x009a8a) && frame.Near(243, 197, 0x009a8a) &&
                    frame.Near(190, 341, 0xfbfbe3) && frame.Near(251, 320, 0x592010))
                    return Page.Save;
            }

            if (frame.Near(9, 30, 0xfbfbe3) && frame.Near(16, 20, 0x009a8a) &&
                frame.Near(12, 197, 0x009a8a) && frame.Near(108, 262, 0x009a8a) &&
                frame.Near(239, 215, 0x005159) && frame.Near(35, 287, 0xfbfbe3))
                return Page.Loadout;
            if (!frame.Near(9, 30, 0xaaebd3) || !frame.Near(242, 175, 0xaaebd3))
                return Page.Unknown;
            if (!frame.Near(12, 197, 0x009a8a) || !frame.Near(221, 197, 0x009a8a))
                return Page.Unknown;
            if (frame.Near(108, 262, 0x009a8a) && frame.Near(170, 287, 0x009a8a) &&
                frame.Near(236, 295, 0xfbfbe3) && frame.Near(189, 334, 0x00d39a))
                return Page.Rules;
            if (!frame.Near(228, 305, 0xfbfbe3)) return Page.Unknown;
            if (frame.Near(189, 334, 0x00d39a) && frame.Near(10, 333, 0xaaebd3) &&
                frame.Near(141, 333, 0xaaebd3) && frame.Near(235, 333, 0xaaebd3))
                return Confirmation(frame) ? Page.ConfirmEnrollment : Page.Lobby;
            if (frame.Near(162, 334, 0x00d39a) && frame.Near(52, 334, 0x00d39a))
                return Confirmation(frame) ? Page.ConfirmGroup : Page.Groups;
            return Page.Unknown;
        }

        private static bool Confirmation(Frame frame)
        {
            return frame.Count(132, 365, 53, 17, PaleRed) > 35 &&
                frame.Count(4, 352, 114, 12, Brown) > 25;
        }

        private static bool HasOpponent(Frame frame) { return frame.Count(76, 250, 158, 11, Brown) > 35; }

        private static int FindGroup(Frame frame)
        {
            int match = -1;
            for (int row = 0; row < 4; row++)
            {
                if (frame.Count(17, 225 + row * 24, 221, 11, Brown) < 25) continue;
                if (match >= 0) return -1;
                match = row;
            }
            return match;
        }

        private static int FindSlot(Frame frame)
        {
            for (int slot = 1; slot <= 4; slot++)
                if (frame.Count(40 + (slot - 1) * 55, 226, 11, 6, Turquoise) > 24) return slot;
            return 0;
        }

        private static bool Brown(Color color)
        {
            return color.R > 140 && color.G < 210 && color.B > 28 && color.R > color.G + 20 &&
                color.G > 78;
        }

        private static bool PaleRed(Color color)
        {
            return color.R > 193 && color.R > color.G + 24 && Math.Abs(color.G - color.B) < 12;
        }

        private static bool LightGray(Color color)
        {
            return color.R > 125 && Math.Abs(color.R - color.G) < 12 && Math.Abs(color.G - color.B) < 12;
        }

        private static bool Turquoise(Color color) { return color.R < 47 && color.G > 150 && color.B > 130; }
        private static bool Cream(Color color) { return color.R > 219 && color.G > 219 && color.B > 180; }

        private sealed class Frame : IDisposable
        {
            private readonly Bitmap bitmap;
            
            public Frame(Bitmap bitmap) { this.bitmap = bitmap; }

            private Color Pixel(int x, int y) { return bitmap.GetPixel(x, y); }

            public bool Near(int x, int y, int rgb)
            {
                Color color = Pixel(x, y);
                return Math.Abs(color.R - ((rgb >> 16) & 255)) <= 20 &&
                    Math.Abs(color.G - ((rgb >> 8) & 255)) <= 20 && Math.Abs(color.B - (rgb & 255)) <= 20;
            }

            public int Count(int x, int y, int width, int height, Func<Color, bool> predicate)
            {
                int count = 0;
                for (int row = y; row < y + height; row++)
                    for (int column = x; column < x + width; column++)
                        if (predicate(Pixel(column, row))) count++;
                return count;
            }

            public void Dispose() { bitmap.Dispose(); }
        }

        private static Frame Capture(CoreSession session)
        {
            Bitmap image = session.Capture();
            return image == null ? null : new Frame(image);
        }
    }
}
