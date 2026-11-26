// SPDX-License-Identifier: MPL-2.0

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Boksol
{
    internal class SolarPanel : Panel
    {
        public Image Artwork { get; set; }

        public SolarPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            Graphics g = e.Graphics;
            bool showcase = Artwork != null;
            using (var fill = new LinearGradientBrush(ClientRectangle,
                showcase ? Color.FromArgb(31, 45, 51) : Color.FromArgb(16, 26, 37),
                Color.FromArgb(11, 17, 26), showcase ? 123f : 108f))
                g.FillRectangle(fill, ClientRectangle);

            using (var grid = new SolidBrush(Color.FromArgb(34, 132, 168, 171)))
                for (int y = 12; y < Height; y += 20)
                    for (int x = 12; x < Width; x += 20)
                        g.FillRectangle(grid, x, y, 1, 1);

            if (!showcase) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float horizonX = Width * 0.62f, horizonY = Height * 0.43f;
            float radius = Math.Min(Width, Height) * 0.22f;
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(horizonX - radius * 1.35f, horizonY - radius * 1.35f, radius * 2.7f, radius * 2.7f);
                using (var fill = new PathGradientBrush(glow))
                {
                    fill.CenterColor = Color.FromArgb(102, 230, 146, 49);
                    fill.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(fill, glow);
                }
            }
            using (var ring = new Pen(Color.FromArgb(91, 241, 166, 82)))
                for (int i = 1; i <= 3; i++)
                {
                    float r = radius * (0.63f + i * 0.29f);
                    g.DrawEllipse(ring, horizonX - r, horizonY - r, r * 2, r * 2);
                }
            using (var mark = new Pen(Color.FromArgb(42, 146, 206, 198)))
            {
                g.DrawLine(mark, horizonX, 12, horizonX, Height - 12);
                g.DrawLine(mark, 12, horizonY, Width - 12, horizonY);
                g.DrawRectangle(mark, 0, 0, Width - 1, Height - 1);
            }
            int size = (int)(Math.Min(Width, Height) * 0.76);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Artwork, (Width - size) / 2, Math.Max(0, (int)(Height * 0.40f) - size / 2), size, size);
        }
    }

    internal sealed class SolarButton : Button
    {
        private readonly Color border, hover;
        private bool hovering;

        public SolarButton(Color border, Color hover)
        {
            this.border = border;
            this.hover = hover;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle box = ClientRectangle;
            box.Width -= 1;
            box.Height -= 1;
            e.Graphics.Clear(Enabled && hovering ? hover : BackColor);
            if (FlatAppearance.BorderSize > 0 || Focused)
                using (var outline = new Pen(Focused ? Color.FromArgb(122, 222, 203) : border))
                    e.Graphics.DrawRectangle(outline, box);

            bool left = TextAlign == ContentAlignment.MiddleLeft;
            if (left) { box.X += Padding.Left; box.Width -= Padding.Left; }
            TextRenderer.DrawText(e.Graphics, Text, Font, box,
                Enabled ? ForeColor : Color.FromArgb(125, 145, 159),
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                (left ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
        }
    }
}
