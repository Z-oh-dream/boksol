// SPDX-License-Identifier: MPL-2.0

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Boksol
{
    internal sealed class GameViewport : Panel
    {
        private readonly Timer refresh;
        private readonly GameKeyBindings bindings;
        private CoreSession session;
        private int keys, touch = -1;

        public GameViewport() : this(new GameKeyBindings()) { }

        public GameViewport(GameKeyBindings bindings)
        {
            this.bindings = bindings;
            BackColor = Color.Black;
            DoubleBuffered = true;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
            refresh = new Timer { Interval = 16 };
            refresh.Tick += (sender, args) => Invalidate();
        }

        public void Mount(CoreSession current)
        {
            session = current;
            ResetInput();
            refresh.Start();
            Focus();
            Invalidate();
        }

        public void Forget()
        {
            refresh.Stop();
            session = null;
            Invalidate();
        }

        private Rectangle ScreenBounds()
        {
            if (session == null || Width == 0 || Height == 0) return Rectangle.Empty;
            double scale = Math.Min(Width / (double)session.Width, Height / (double)session.Height);
            var size = new Size((int)(session.Width * scale), (int)(session.Height * scale));
            return new Rectangle((Width - size.Width) / 2, (Height - size.Height) / 2, size.Width, size.Height);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (session == null) return;
            args.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            args.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            session.Draw(args.Graphics, ScreenBounds());
        }

        protected override bool IsInputKey(Keys key)
        {
            return session != null && bindings.Find(key & Keys.KeyCode, session.IsDs) != GameButton.None || base.IsInputKey(key);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (session == null) return;
            GameButton button = bindings.Find(e.KeyCode, session.IsDs);
            if (button == GameButton.None) return;
            if (button == GameButton.SunlightDown || button == GameButton.SunlightUp)
            {
                session.AdjustSunlight(button == GameButton.SunlightDown ? -1 : 1);
            }
            else
            {
                keys |= (int)button;
                session.Input(keys, touch);
            }
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (session == null) return;
            GameButton button = bindings.Find(e.KeyCode, session.IsDs);
            if (button == GameButton.None || button == GameButton.SunlightDown || button == GameButton.SunlightUp) return;
            keys &= ~(int)button;
            session.Input(keys, touch);
            e.Handled = true;
        }

        protected override void OnLostFocus(EventArgs e)
        {
            ResetInput();
            base.OnLostFocus(e);
        }

        public void ResetInput()
        {
            keys = 0;
            touch = -1;
            if (session != null) session.Input(keys, touch);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            UpdateTouch(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateTouch(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            touch = -1;
            if (session != null) session.Input(keys, touch);
        }

        private void UpdateTouch(MouseEventArgs e)
        {
            if (session == null || !session.IsDs || e.Button != MouseButtons.Left) return;
            Rectangle area = ScreenBounds();
            touch = -1;
            if (area.Contains(e.Location))
            {
                int x = (e.X - area.X) * 256 / area.Width;
                int y = (e.Y - area.Y) * 384 / area.Height - 192;
                if (y >= 0) touch = (y << 8) | x;
            }
            session.Input(keys, touch);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) refresh.Dispose();
            base.Dispose(disposing);
        }

    }
}
