// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Boksol
{
    internal sealed partial class Lobby
    {
        private string KeyBindingsPath { get { return Path.Combine(data, "key-bindings.txt"); } }

        private Keys MenuConfirmKey
        {
            get
            {
                return UsesGameSwitchKey(Keys.Enter) || bindings[GameButton.Up] == Keys.Enter ||
                    bindings[GameButton.Down] == Keys.Enter ? bindings[GameButton.A] : Keys.Enter;
            }
        }

        private bool ReplayExportShortcut { get { return !UsesGameSwitchKey(Keys.E) && MenuConfirmKey != Keys.E; } }

        private bool UsesGameSwitchKey(Keys key)
        {
            return key == bindings[GameButton.L] || key == bindings[GameButton.R];
        }

        private void ShowKeyBindings()
        {
            viewport.ResetInput();
            using (var editor = new KeyBindingsDialog(bindings, KeyBindingsPath))
                Dialog(AppIdentity.Name + " · 游戏键位", new Size(578, 403), editor).ShowDialog(this);
            RefreshKeyHelp();
            if (pageIndex == 3) viewport.Focus();
        }

        private void RefreshKeyHelp()
        {
            string switchGame = BoundKeyName(GameButton.L) + " / " + BoundKeyName(GameButton.R) + "  切换作品";
            string confirm = GameKeyBindings.DisplayName(MenuConfirmKey) + "  确定";
            switch (pageIndex)
            {
                case -1:
                    keyHelp.Text = BoundKeyName(GameButton.Up) + " " + BoundKeyName(GameButton.Down) +
                        "  选择菜单     " + confirm + "     " + switchGame;
                    break;
                case 1:
                    keyHelp.Text = "↑ ↓  选择录像     Enter  播放     " + (ReplayExportShortcut ? "E  导出     " : "") +
                        "Delete  删除     Esc  返回菜单     " + switchGame;
                    break;
                case 3:
                    keyHelp.Text = GameKeyHelp();
                    break;
                default:
                    keyHelp.Text = "Tab  移动焦点     " + confirm + "     Esc  返回菜单     " + switchGame;
                    break;
            }
        }

        private bool HandleMenuKey(Keys key)
        {
            if (key == (Keys.ShiftKey | Keys.Shift)) key = Keys.ShiftKey;
            if (key == (Keys.ControlKey | Keys.Control)) key = Keys.ControlKey;
            if (UsesGameSwitchKey(key))
            {
                SwitchGame(key == bindings[GameButton.L] ? -1 : 1);
                return true;
            }
            if (pageIndex < 0 && (key == bindings[GameButton.Up] || key == bindings[GameButton.Down]))
            {
                int direction = key == bindings[GameButton.Down] ? 1 : -1;
                int current = Array.FindIndex(navButtons, b => b.Focused);
                if (current < 0) current = direction > 0 ? -1 : 0;
                navButtons[(current + direction + navButtons.Length) % navButtons.Length].Focus();
                return true;
            }
            if (key == MenuConfirmKey && ActiveControl is Button button)
            {
                button.PerformClick();
                return true;
            }
            return pageIndex < 0 && (key == Keys.Up || key == Keys.Down);
        }

        private string BoundKeyName(GameButton button)
        {
            return GameKeyBindings.DisplayName(bindings[button]);
        }

        private string GameKeyHelp()
        {
            string directions = BoundKeyName(GameButton.Up) + " " + BoundKeyName(GameButton.Down) + " " +
                BoundKeyName(GameButton.Left) + " " + BoundKeyName(GameButton.Right);
            string extra = Game.IsDs ? "X " + BoundKeyName(GameButton.X) + " / Y " + BoundKeyName(GameButton.Y) + " · 鼠标下屏"
                : "日照 " + BoundKeyName(GameButton.SunlightDown) + " / " + BoundKeyName(GameButton.SunlightUp);
            return directions + "  ·  A " + BoundKeyName(GameButton.A) + " / B " + BoundKeyName(GameButton.B) +
                "  ·  L " + BoundKeyName(GameButton.L) + " / R " + BoundKeyName(GameButton.R) + "  ·  " + extra + "  ·  Esc 返回对战";
        }

        private sealed class KeyBindingsDialog : Form
        {
            private const string Hint = "选择一项后按新键。若该键已被使用，两项键位会互换。";
            private readonly GameKeyBindings bindings;
            private readonly string path;
            private readonly Label status;
            private readonly Dictionary<GameButton, Button> buttons = new Dictionary<GameButton, Button>();
            private GameButton waiting;

            public KeyBindingsDialog(GameKeyBindings bindings, string path)
            {
                this.bindings = bindings;
                this.path = path;
                AutoScaleMode = AutoScaleMode.Dpi;
                status = Caption(Hint, 10, Quiet);
                Place(this, status, 18, 13, 542, 40);

                AddBinding(GameButton.Up, "上", 0, 0);
                AddBinding(GameButton.Down, "下", 0, 1);
                AddBinding(GameButton.Left, "左", 0, 2);
                AddBinding(GameButton.Right, "右", 0, 3);
                AddBinding(GameButton.Start, "START", 0, 4);
                AddBinding(GameButton.Select, "SELECT", 0, 5);
                AddBinding(GameButton.SunlightDown, "减少日照 (GBA)", 0, 6);
                AddBinding(GameButton.A, "A", 1, 0);
                AddBinding(GameButton.B, "B", 1, 1);
                AddBinding(GameButton.L, "L", 1, 2);
                AddBinding(GameButton.R, "R", 1, 3);
                AddBinding(GameButton.X, "X (DS)", 1, 4);
                AddBinding(GameButton.Y, "Y (DS)", 1, 5);
                AddBinding(GameButton.SunlightUp, "增加日照 (GBA)", 1, 6);

                Place(this, ActionButton("恢复默认", () => { bindings.Reset(); Save(); }), 18, 346, 128, 38);
                var done = ActionButton("完成", Close, true);
                CancelButton = done;
                Place(this, done, 444, 346, 116, 38);
                Deactivate += (sender, args) => { if (waiting != GameButton.None) StopWaiting(); };
                RefreshButtons();
            }

            private void AddBinding(GameButton binding, string title, int column, int row)
            {
                int x = 18 + column * 282;
                int y = 61 + row * 38;
                Place(this, Caption(title, 10, Ink), x, y, 125, 32);
                var button = ActionButton("", () =>
                {
                    waiting = binding;
                    status.Text = "按新键，或按 Esc 取消。";
                    RefreshButtons();
                });
                button.AccessibleName = title;
                button.LostFocus += (sender, args) => { if (waiting == binding) StopWaiting(); };
                Place(this, button, x + 130, y, 130, 32);
                buttons.Add(binding, button);
            }

            private void RefreshButtons()
            {
                foreach (var pair in buttons)
                    pair.Value.Text = pair.Key == waiting ? "等待按键…" : GameKeyBindings.DisplayName(bindings[pair.Key]);
            }

            private void StopWaiting()
            {
                waiting = GameButton.None;
                status.Text = Hint;
                RefreshButtons();
            }

            private void Save()
            {
                StopWaiting();
                try { bindings.Save(path); }
                catch (IOException) { status.Text = "键位已生效，但无法保存到本地。"; }
                catch (UnauthorizedAccessException) { status.Text = "键位已生效，但无法保存到本地。"; }
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                Keys key = keyData & Keys.KeyCode;
                bool repeat = (msg.LParam.ToInt64() & (1L << 30)) != 0;
                if (waiting == GameButton.None && !(repeat && key == Keys.Enter))
                    return base.ProcessCmdKey(ref msg, keyData);
                if (repeat) return true;
                if (key == Keys.Tab)
                {
                    StopWaiting();
                    return base.ProcessCmdKey(ref msg, keyData);
                }
                if (key == Keys.Escape) StopWaiting();
                else if (bindings.TryBind(waiting, key)) Save();
                else status.Text = "此键不可绑定。请按其他键，或按 Esc 取消。";
                return true;
            }
        }
    }
}
