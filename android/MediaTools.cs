using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Widget;
using Bitmap = Android.Graphics.Bitmap;
using Uri = Android.Net.Uri;

namespace Boksol
{
    public sealed partial class MainActivity
    {
        private const int ExportPicker = 4;
        private Recording recording;
        private Spectating broadcast;
        private string exportPath;
        private string RecordingFolder => Path.Combine(data, "recordings");

        private LinearLayout GameMediaActions(Game game, CoreSession live, string hostAddress)
        {
            var row = Row();
            Button record = null;
            record = Button("录制", () => ToggleRecording(game, live, record));
            row.AddView(record, new LinearLayout.LayoutParams(0, -2, 1));
            if (hostAddress != null)
            {
                Button share = null;
                share = Button("开放观战", () => ToggleBroadcast(live, hostAddress, share));
                row.AddView(share, new LinearLayout.LayoutParams(0, -2, 1));
            }
            return row;
        }

        private async void ToggleRecording(Game game, CoreSession live, Button button)
        {
            if (recording != null)
            {
                recording.Stop();
                return;
            }
            Recording capture = null;
            try
            {
                Directory.CreateDirectory(RecordingFolder);
                string name = game.Id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".avi";
                capture = new Recording(live, Path.Combine(RecordingFolder, name));
                recording = capture;
                button.Text = "结束录制";
                int frames = await capture.Completion;
                Toast.MakeText(this, frames > 0 ? "录像已保存" : "未录到画面", ToastLength.Short).Show();
            }
            catch (Exception ex) { Toast.MakeText(this, "录像失败。 " + ex.Message, ToastLength.Long).Show(); }
            finally
            {
                if (recording == capture) recording = null;
                if (Active && session == live) button.Text = "录制";
            }
        }

        private async void ToggleBroadcast(CoreSession live, string address, Button button)
        {
            if (broadcast != null)
            {
                broadcast.Dispose();
                return;
            }
            Spectating stream = null;
            try
            {
                string code = Spectating.CreateCode();
                stream = new Spectating(live, code);
                broadcast = stream;
                string invite = address + "/" + code;
                CopyText("boksol 观战邀请", invite);
                button.Text = "关闭观战";
                new AlertDialog.Builder(this).SetTitle("观战邀请已复制").SetMessage(invite)
                    .SetPositiveButton("关闭", (_, _) => { }).Show();
                await stream.Completion;
            }
            catch (Exception ex) { Toast.MakeText(this, "观战开启失败。 " + ex.Message, ToastLength.Long).Show(); }
            finally
            {
                stream?.Dispose();
                if (broadcast == stream) broadcast = null;
                if (Active && session == live) button.Text = "开放观战";
            }
        }

        private void StopMedia()
        {
            broadcast?.Dispose();
            recording?.Stop();
        }

        private void ShowWatchPrompt()
        {
            var field = new EditText(this) { Hint = "主机 IPv4 地址/八位观战码" };
            field.SetSingleLine(true);
            new AlertDialog.Builder(this).SetTitle("观看对战").SetView(field)
                .SetNegativeButton("取消", (_, _) => { })
                .SetPositiveButton("连接", (_, _) => Watch(field.Text ?? "")).Show();
        }

        private async void Watch(string invitationText)
        {
            string[] parts = invitationText.Trim().Split('/');
            if (parts.Length != 2 || parts[0].Split('.').Length != 4 ||
                !IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
                !Spectating.IsValidCode(parts[1].ToUpperInvariant()))
            {
                SetStatus("请输入完整的观战邀请。");
                return;
            }
            using var work = new CancellationTokenSource();
            operation = work;
            var page = Column();
            var status = Label("正在连接观战…", 16, Muted);
            var image = new ImageView(this);
            image.SetScaleType(ImageView.ScaleType.FitCenter);
            page.AddView(Button("停止观战", CancelOperation));
            page.AddView(status);
            page.AddView(image, new LinearLayout.LayoutParams(-1, 0, 1));
            SetContentView(page);
            Window.AddFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
            Bitmap displayed = null;
            try
            {
                await Spectating.Watch(address.ToString(), parts[1].ToUpperInvariant(), frame =>
                {
                    if (frame == null || work.IsCancellationRequested) return;
                    var decoded = GameCapture.Decode(frame);
                    RunOnUiThread(() =>
                    {
                        if (!Active || !ReferenceEquals(operation, work) || work.IsCancellationRequested)
                        {
                            decoded.Dispose();
                            return;
                        }
                        image.SetImageBitmap(decoded);
                        displayed?.Dispose();
                        displayed = decoded;
                        status.Text = "正在观看对战";
                    });
                }, work.Token);
            }
            catch (Exception ex) { notice = work.IsCancellationRequested ? "已退出观战。" : "观战已结束。 " + ex.Message; }
            finally
            {
                image.SetImageDrawable(null);
                displayed?.Dispose();
                operation = null;
                ShowHome();
            }
        }

        private void ShowRecordings()
        {
            if (!Directory.Exists(RecordingFolder))
            {
                SetStatus("暂无录像。");
                return;
            }
            var files = Directory.GetFiles(RecordingFolder, "*.avi").OrderByDescending(File.GetLastWriteTime).ToArray();
            if (files.Length == 0)
            {
                SetStatus("暂无录像。");
                return;
            }
            new AlertDialog.Builder(this).SetTitle("录像")
                .SetItems(files.Select(Path.GetFileName).ToArray(), (_, args) => PreviewRecording(files[args.Which]))
                .SetNegativeButton("关闭", (_, _) => { }).Show();
        }

        private void PreviewRecording(string path)
        {
            try
            {
                var thumbnail = Recording.Preview(path);
                var preview = new ImageView(this);
                preview.SetAdjustViewBounds(true);
                if (thumbnail != null) preview.SetImageBitmap(thumbnail);
                var dialog = new AlertDialog.Builder(this).SetTitle(Path.GetFileName(path)).SetView(preview)
                    .SetPositiveButton("导出 AVI", (_, _) => PickExport(path))
                    .SetNegativeButton("关闭", (_, _) => { }).Create();
                dialog.DismissEvent += (_, _) =>
                {
                    preview.SetImageDrawable(null);
                    thumbnail?.Dispose();
                };
                dialog.Show();
            }
            catch (Exception ex) { SetStatus("无法打开录像。 " + ex.Message); }
        }

        private void PickExport(string path)
        {
            using var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("video/x-msvideo");
            intent.PutExtra(Intent.ExtraTitle, Path.GetFileName(path));
            exportPath = path;
            try { StartActivityForResult(intent, ExportPicker); }
            catch (ActivityNotFoundException)
            {
                exportPath = null;
                SetStatus("设备上找不到文件选择器。");
            }
        }

        private async Task ExportRecording(Result result, Uri uri)
        {
            string path = exportPath;
            exportPath = null;
            if (result != Result.Ok || uri == null || path == null) return;
            try
            {
                await Task.Run(() =>
                {
                    using var source = File.OpenRead(path);
                    using var output = ContentResolver.OpenOutputStream(uri, "w") ?? throw new IOException("无法创建文件。");
                    source.CopyTo(output);
                });
                SetStatus("AVI 已导出。");
            }
            catch (Exception ex) { SetStatus("导出失败。 " + ex.Message); }
        }
    }
}
