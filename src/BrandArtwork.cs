using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Boksol
{
    internal static class BrandArtwork
    {
        public static Bitmap Create(Color gold, Color mint)
        {
            var image = new Bitmap(256, 256);
            using (var canvas = Graphics.FromImage(image))
            using (var ring = new Pen(mint, 4))
            using (var rays = new Pen(gold, 9) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var disc = new LinearGradientBrush(new Rectangle(75, 75, 106, 106), gold,
                Color.FromArgb(231, 119, 63), 65f))
            {
                canvas.SmoothingMode = SmoothingMode.AntiAlias;
                canvas.DrawEllipse(ring, 17, 17, 222, 222);
                canvas.FillEllipse(disc, 75, 75, 106, 106);
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    float x = (float)Math.Cos(angle), y = (float)Math.Sin(angle);
                    canvas.DrawLine(rays, 128 + 69 * x, 128 + 69 * y, 128 + 91 * x, 128 + 91 * y);
                }
            }
            return image;
        }
    }
}
