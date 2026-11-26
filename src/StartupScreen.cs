// SPDX-License-Identifier: MPL-2.0

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Boksol
{
    internal interface IStartupUpdateCheck
    {
        Task CheckAsync(IProgress<int> progress, CancellationToken cancellationToken);
    }

    internal sealed class OfflineStartupUpdateCheck : IStartupUpdateCheck
    {
        public async Task CheckAsync(IProgress<int> progress, CancellationToken cancellationToken)
        {
            for (int percent = 5; percent <= 100; percent += 5)
            {
                await Task.Delay(65, cancellationToken).ConfigureAwait(false);
                progress.Report(percent);
            }
            await Task.Delay(220, cancellationToken).ConfigureAwait(false);
        }
    }

    internal sealed partial class Lobby
    {
        private void ShowStartup(IStartupUpdateCheck updateCheck)
        {
            header.Visible = footer.Visible = body.Visible = false;
            startup = new StartupScreen(art, Gold, Quiet) { Dock = DockStyle.Fill };
            Controls.Add(startup);
            startup.BringToFront();
            Shown += async (sender, args) => await FinishStartup(updateCheck);
        }

        private async Task FinishStartup(IStartupUpdateCheck updateCheck)
        {
            StartupScreen screen = startup;
            using (var stop = new CancellationTokenSource())
            {
                EventHandler cancel = (sender, args) => stop.Cancel();
                Disposed += cancel;
                try
                {
                    await updateCheck.CheckAsync(new Progress<int>(screen.SetProgress), stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (Exception error) { Trace.TraceWarning("Startup update check failed: {0}", error.Message); }
                finally { Disposed -= cancel; }
            }
            if (IsDisposed || Disposing) return;

            startup = null;
            SuspendLayout();
            Controls.Remove(screen);
            screen.Dispose();
            header.Visible = footer.Visible = body.Visible = true;
            ResumeLayout(true);
            SelectPage(-1);
            ChangeGame();
        }
    }

    internal sealed class StartupScreen : SolarPanel
    {
        private readonly Image emblem;
        private readonly Color titleColor, statusColor;
        private int progress;
        private string Status { get { return progress < 100 ? "正在检查更新" : "正在进入主菜单"; } }

        public StartupScreen(Image emblem, Color titleColor, Color statusColor)
        {
            this.emblem = emblem;
            this.titleColor = titleColor;
            this.statusColor = statusColor;
            AccessibleName = AppIdentity.Name;
            AccessibleDescription = Status;
        }

        public void SetProgress(int value)
        {
            if (IsDisposed) return;
            progress = Math.Max(0, Math.Min(value, 100));
            AccessibleDescription = Status;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;

            float scale = Math.Min(Width / 1920f, Height / 1200f);
            float centerX = Width / 2f, emblemY = Height * 0.391f;
            float radius = 133 * scale;
            var halo = new RectangleF(centerX - radius, emblemY - radius, radius * 2, radius * 2);
            Graphics canvas = e.Graphics;
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            using (var disc = new GraphicsPath())
            {
                disc.AddEllipse(halo);
                using (var glow = new PathGradientBrush(disc))
                {
                    glow.CenterColor = Color.FromArgb(68, titleColor);
                    glow.SurroundColors = new[] { Color.Transparent };
                    canvas.FillPath(glow, disc);
                }
            }
            using (var ring = new Pen(Color.FromArgb(95, titleColor), Math.Max(1, scale)))
                canvas.DrawEllipse(ring, halo);
            float emblemSize = 228 * scale;
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.DrawImage(emblem, centerX - emblemSize / 2, emblemY - emblemSize / 2, emblemSize, emblemSize);

            var title = new Rectangle(0, (int)(Height * 0.513f), Width, (int)(Height * 0.113f));
            using (var font = new Font("Microsoft YaHei UI", 84 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                TextRenderer.DrawText(canvas, AppIdentity.Name, font, title, titleColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            float barWidth = 720 * scale;
            float barHeight = Math.Max(3, 6 * scale);
            float barLeft = centerX - barWidth / 2;
            float barTop = Height * 0.667f;
            using (var track = new SolidBrush(Color.FromArgb(51, 76, 84)))
            using (var fill = new SolidBrush(titleColor))
            {
                canvas.FillRectangle(track, barLeft, barTop, barWidth, barHeight);
                canvas.FillRectangle(fill, barLeft, barTop, barWidth * progress / 100f, barHeight);
            }

            int statusTop = (int)(barTop + 24 * scale);
            using (var font = new Font("Microsoft YaHei UI", Math.Max(12, 18 * scale), FontStyle.Regular, GraphicsUnit.Pixel))
                TextRenderer.DrawText(canvas, Status, font, new Point((int)barLeft, statusTop), statusColor, TextFormatFlags.NoPadding);
        }
    }
}
