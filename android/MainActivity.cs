using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;
using OperationCanceledException = System.OperationCanceledException;
using Uri = Android.Net.Uri;

namespace Boksol
{
    [Activity(Label = "我太 boksol", MainLauncher = true, Exported = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
    public sealed partial class MainActivity : Activity
    {
        internal static readonly Color Background = Color.Rgb(14, 20, 28);
        internal static readonly Color Panel = Color.Rgb(23, 34, 45);
        internal static readonly Color Gold = Color.Rgb(255, 185, 78);
        internal static readonly Color Muted = Color.Rgb(157, 180, 193);
        private const int RomPicker = 1, SavePicker = 2, PatchPicker = 3;
        private const string Unset = "未导入";
        private ISharedPreferences preferences;
        private string data, engines, notice = "", codeText = "", playerName = "", rosterSummary = "";
        private int selected, roomSize;
        private CancellationTokenSource operation;
        private CodeRoom room;
        private CoreSession session;
        private GameDisplay display;
        private TextView homeStatus, roomPlayers;
        private EditText invitation;
        private System.Uri signalingServer;
        private Game Current => Game.All[selected];
        private string Imports => Path.Combine(data, "imports");
        private bool Active => !IsFinishing && !IsDestroyed;

        protected override void OnCreate(Bundle state)
        {
            base.OnCreate(state);
            data = FilesDir.AbsolutePath;
            engines = ApplicationInfo.NativeLibraryDir;
            preferences = GetSharedPreferences("boksol", FileCreationMode.Private);
            RemoteRoom.TryAddress(preferences.GetString(RemoteRoom.SettingsKey, ""), out signalingServer);
            playerName = RoomRoster.CleanName(preferences.GetString(RoomRoster.NameKey, ""));
            roomSize = Math.Clamp(preferences.GetInt(RoomRoster.SeatsKey, 2), 2, 4);
            selected = Math.Clamp(preferences.GetInt("game", 0), 0, Game.All.Length - 1);
            Directory.CreateDirectory(Imports);
            foreach (var game in Game.All)
            {
                var rom = Path.Combine(Imports, game.Id + game.Extension);
                var patch = Path.Combine(Imports, game.Id + ".patch");
                if (File.Exists(rom)) game.RomPath = rom;
                if (File.Exists(patch)) game.PatchPath = patch;
                game.BattleSaveId = preferences.GetString(game.Id + ".save", null);
            }
            Window.SetStatusBarColor(Background);
            Window.SetNavigationBarColor(Background);
            ShowHome();
        }

        private void ShowHome()
        {
            if (!Active) return;
            display = null;
            Window.ClearFlags(WindowManagerFlags.KeepScreenOn);
            var content = Page();
            content.AddView(Label("我太 boksol", 32, Gold));
            content.AddView(Label("0.5.4 · Android", 13, Muted));
            content.AddView(Label("游戏 ROM", 19, Gold));
            for (int i = 0; i < Game.All.Length; i++)
            {
                int index = i;
                Game game = Game.All[i];
                var row = Button(game.Title + "   ·   " + (game.RomPath == null ? Unset : "已导入"),
                    () => SelectGame(index), i == selected);
                row.Gravity = GravityFlags.CenterVertical | GravityFlags.Left;
                row.SetPadding(Dp(16), 0, Dp(12), 0);
                row.Enabled = operation == null;
                content.AddView(row);
            }
            content.AddView(ActionRow(("导入 ROM", () => Pick(RomPicker, true)), ("汉化补丁", ConfigurePatch)));
            content.AddView(Label(Current.Title, 21, Gold));
            content.AddView(Label(Current.Guide, 14, Muted));
            content.AddView(ActionRow(("设置对战存档", ChooseSave), ("本地游戏", () => Start(fresh: false))));
            content.AddView(ActionRow(("新建本地存档", () => Start(fresh: true)), ("TPL 术语", Glossary)));
            var keys = Button("键盘与手柄键位", ShowKeySettings);
            keys.Enabled = operation == null;
            content.AddView(keys);
            content.AddView(Label(signalingServer == null ? "局域网对战" : "远程对战", 19, Gold));
            var signaling = Button("联机设置", ShowSignalingSettings);
            signaling.Enabled = operation == null;
            content.AddView(signaling);
            invitation = new EditText(this)
            {
                Hint = "输入联机码",
                Text = codeText,
                Enabled = operation == null,
                InputType = InputTypes.ClassText | InputTypes.TextFlagCapCharacters
            };
            invitation.SetSingleLine(true);
            invitation.SetTextColor(Color.White);
            invitation.SetHintTextColor(Muted);
            invitation.TextChanged += (_, _) => codeText = invitation.Text ?? "";
            content.AddView(invitation);
            content.AddView(ActionRow(("生成六位码", GenerateCode), ("复制", CopyCode)));
            content.AddView(ActionRow(("录像", ShowRecordings), ("观看对战", ShowWatchPrompt)));
            if (operation == null)
                content.AddView(Button("开始配对", () => Start(multiplayer: true), true));
            else
                content.AddView(Button("取消", CancelOperation));
            homeStatus = Label(notice, 14, Muted);
            content.AddView(homeStatus);
            roomPlayers = Label(rosterSummary, 13, Gold);
            roomPlayers.Visibility = rosterSummary.Length == 0 ? ViewStates.Gone : ViewStates.Visible;
            content.AddView(roomPlayers);
        }

        private void SelectGame(int index)
        {
            selected = index;
            preferences.Edit().PutInt("game", index).Apply();
            notice = "";
            ShowHome();
        }

        private void GenerateCode()
        {
            invitation.Text = CodeRoom.FormatCode(CodeRoom.CreateCode());
            SetStatus(signalingServer == null ? "把联机码发给同一局域网中的玩家。" : "把联机码发给使用同一信令服务器的玩家。");
        }

        private void ShowSignalingSettings()
        {
            var fields = Column();
            fields.SetPadding(Dp(20), 0, Dp(20), 0);
            fields.AddView(Label("名字", 14, Muted));
            var name = new EditText(this) { Text = playerName };
            name.SetSingleLine(true);
            name.SetFilters(new IInputFilter[] { new InputFilterLengthFilter(RoomRoster.NameLimit) });
            fields.AddView(name);
            fields.AddView(Label("信令服务器地址", 14, Muted));
            var input = new EditText(this)
            {
                Text = signalingServer?.AbsoluteUri ?? "",
                InputType = InputTypes.ClassText | InputTypes.TextVariationUri
            };
            input.SetSingleLine(true);
            input.SetFilters(new IInputFilter[] { new InputFilterLengthFilter(512) });
            fields.AddView(input);
            fields.AddView(Label("远程房间人数", 14, Muted));
            var seats = new Spinner(this);
            var choices = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem,
                new[] { "2 人", "3 人", "4 人" });
            choices.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
            seats.Adapter = choices;
            seats.SetSelection(roomSize - 2);
            fields.AddView(seats);
            var dialog = new AlertDialog.Builder(this).SetTitle("联机设置")
                .SetMessage("留空使用局域网。远程玩家使用同一 ws:// 或 wss:// 地址。")
                .SetView(fields).SetPositiveButton("保存", (_, _) => { })
                .SetNegativeButton("取消", (_, _) => { }).Show();
            dialog.GetButton((int)DialogButtonType.Positive).Click += (_, _) =>
            {
                if (!RemoteRoom.TryAddress(input.Text, out var address))
                {
                    input.Error = "请输入有效的 ws:// 或 wss:// 地址。";
                    return;
                }
                signalingServer = address;
                playerName = RoomRoster.CleanName(name.Text);
                roomSize = seats.SelectedItemPosition + 2;
                using var edit = preferences.Edit();
                if (address == null) edit.Remove(RemoteRoom.SettingsKey);
                else edit.PutString(RemoteRoom.SettingsKey, address.AbsoluteUri);
                edit.PutString(RoomRoster.NameKey, playerName).PutInt(RoomRoster.SeatsKey, roomSize);
                edit.Apply();
                dialog.Dismiss();
                notice = address == null ? "已切换为局域网配对。" : "已保存信令服务器地址。";
                ShowHome();
            };
        }

        private void CopyCode()
        {
            if (!CodeRoom.TryCode(codeText, out _))
            {
                SetStatus("请先输入或生成联机码。");
                return;
            }
            CopyText("boksol 联机码", codeText.Trim());
            SetStatus("联机码已复制。");
        }

        private void CopyText(string label, string value)
        {
            var clipboard = (global::Android.Content.ClipboardManager)GetSystemService(ClipboardService);
            clipboard.PrimaryClip = ClipData.NewPlainText(label, value);
        }

        private void Pick(int request, bool multiple = false)
        {
            if (operation != null) return;
            using var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            if (multiple) intent.PutExtra(Intent.ExtraAllowMultiple, true);
            try { StartActivityForResult(intent, request); }
            catch (ActivityNotFoundException) { SetStatus("设备上找不到文件选择器。"); }
        }

        protected override async void OnActivityResult(int requestCode, Result resultCode, Intent intent)
        {
            base.OnActivityResult(requestCode, resultCode, intent);
            if (requestCode == ExportPicker)
            {
                await ExportRecording(resultCode, intent?.Data);
                return;
            }
            if (resultCode != Result.Ok || intent == null || operation != null ||
                requestCode is not (RomPicker or SavePicker or PatchPicker)) return;
            var uris = new List<Uri>();
            if (intent.ClipData is { } files)
                for (int i = 0; i < files.ItemCount; i++) uris.Add(files.GetItemAt(i).Uri);
            else if (intent.Data is { } file) uris.Add(file);
            if (uris.Count == 0) return;
            var game = Current;
            using var work = new CancellationTokenSource();
            operation = work;
            notice = "正在导入文件…";
            ShowHome();
            try
            {
                var messages = await Task.Run(() => ImportFiles(requestCode, uris, game, work.Token), work.Token);
                notice = string.Join("\n", messages);
            }
            catch (OperationCanceledException) { notice = "已取消导入。"; }
            catch (Exception ex) { notice = "导入失败。 " + ex.Message; }
            finally
            {
                operation = null;
                ShowHome();
            }
        }

        private List<string> ImportFiles(int request, List<Uri> uris, Game selectedGame, CancellationToken token)
        {
            var messages = new List<string>();
            foreach (Uri uri in uris)
            {
                token.ThrowIfCancellationRequested();
                string name = DocumentName(uri);
                string folder = Path.Combine(Imports, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string staged = Path.Combine(folder, name);
                try
                {
                    CopyDocument(uri, staged, request == SavePicker ? 1024 * 1024 : Patch.MaxSize, token);
                    token.ThrowIfCancellationRequested();
                    if (request == RomPicker)
                    {
                        var game = Game.IdentifyRom(staged);
                        string target = Path.Combine(Imports, game.Id + game.Extension);
                        File.Move(staged, target, true);
                        game.RomPath = target;
                        if (game.PatchPath != null) File.Delete(game.PatchPath);
                        game.PatchPath = null;
                        messages.Add(game.Title + " 已导入");
                    }
                    else if (request == SavePicker)
                    {
                        var image = selectedGame.ReadImage();
                        var save = BattleSaves.Import(data, selectedGame, image.Hash, staged);
                        SelectSave(selectedGame, save);
                        messages.Add(save.Name);
                    }
                    else
                    {
                        byte[] patched = Patch.Apply(File.ReadAllBytes(selectedGame.RomPath), File.ReadAllBytes(staged));
                        selectedGame.Validate(patched);
                        token.ThrowIfCancellationRequested();
                        string target = Path.Combine(Imports, selectedGame.Id + ".patch");
                        File.Move(staged, target, true);
                        selectedGame.PatchPath = target;
                        SelectSave(selectedGame, null);
                        messages.Add("汉化补丁已应用");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { messages.Add(name + " · " + ex.Message); }
                finally
                {
                    if (File.Exists(staged)) File.Delete(staged);
                    Directory.Delete(folder);
                }
            }
            return messages;
        }

        private string DocumentName(Uri uri)
        {
            using var cursor = ContentResolver.Query(uri, new[] { "_display_name" }, null, null, null);
            string name = cursor?.MoveToFirst() == true ? cursor.GetString(0) : uri.LastPathSegment;
            name = Path.GetFileName(name?.Replace('\\', '/'));
            return string.IsNullOrWhiteSpace(name) || name is "." or ".." ? "import" : name;
        }

        private void CopyDocument(Uri uri, string target, int limit, CancellationToken token)
        {
            using var source = ContentResolver.OpenInputStream(uri) ?? throw new IOException("无法读取文件。");
            using var output = File.OpenWrite(target);
            var buffer = new byte[65536];
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                if (output.Length + count > limit) throw new InvalidDataException("文件大小超出支持范围。");
                output.Write(buffer, 0, count);
            }
        }

        private void SelectSave(Game game, BattleSave save)
        {
            game.BattleSaveId = save?.Id;
            preferences.Edit().PutString(game.Id + ".save", save?.Id).Apply();
        }

        private async void ChooseSave()
        {
            if (!RequireRom()) return;
            var game = Current;
            try
            {
                var (saves, caption) = await Task.Run(() =>
                {
                    game.OriginalHash = game.ReadHash(game.RomPath);
                    var hash = game.ReadImage().Hash;
                    return (BattleSaves.List(data, game, hash), BattleSaves.Default(data, game, hash)?.Name ?? "未选择");
                });
                if (!Active || Current != game || operation != null || session != null) return;
                new AlertDialog.Builder(this).SetTitle("对战存档 · " + caption)
                    .SetItems(saves.Select(x => x.Name).ToArray(), (_, args) =>
                    {
                        SelectSave(game, saves[args.Which]);
                        SetStatus(saves[args.Which].Name);
                    })
                    .SetNeutralButton("导入存档", (_, _) => Pick(SavePicker))
                    .SetNegativeButton("关闭", (_, _) => { }).Show();
            }
            catch (Exception ex) { SetStatus(ex.Message); }
        }

        private void ConfigurePatch()
        {
            if (!RequireRom()) return;
            Game game = Current;
            var dialog = new AlertDialog.Builder(this).SetTitle("汉化补丁")
                .SetMessage(game.PatchPath != null ? "已应用汉化补丁。" : OfficialText.Supports(game, game.OriginalHash)
                    ? "当前原版 ROM 已启用内置联机中文。" : "可导入 IPS 或 BPS 补丁。")
                .SetPositiveButton("导入补丁", (_, _) => Pick(PatchPicker))
                .SetNegativeButton("关闭", (_, _) => { });
            if (game.PatchPath != null)
                dialog.SetNeutralButton("移除补丁", (_, _) =>
                {
                    try
                    {
                        File.Delete(game.PatchPath);
                        game.PatchPath = null;
                        SelectSave(game, null);
                        SetStatus("补丁已移除。");
                    }
                    catch (IOException ex) { SetStatus(ex.Message); }
                });
            dialog.Show();
        }

        private void Glossary()
        {
            new AlertDialog.Builder(this).SetTitle("TPL 术语")
                .SetMessage(Current.Guide + "\n\nDjango   强戈\nSolar Boy Django   太阳少年强戈\n" +
                    "Otenko   天鼓、天鼓大人\nGun Del Sol   太阳铳\nCrossover Battle 2   跨界对战2\nSabata   萨巴塔")
                .SetPositiveButton("关闭", (_, _) => { }).Show();
        }

        private bool RequireRom()
        {
            if (Current.RomPath != null) return true;
            SetStatus("请先导入该作品的 ROM。");
            return false;
        }

        private async void Start(bool multiplayer = false, bool fresh = false)
        {
            if (operation != null || session != null || !RequireRom()) return;
            string code = null;
            if (multiplayer && !CodeRoom.TryCode(codeText, out code))
            {
                SetStatus("请输入有效的联机码。");
                return;
            }
            Game game = Current;
            if (multiplayer) rosterSummary = "";
            using var work = new CancellationTokenSource();
            operation = work;
            notice = "正在准备 ROM 与存档…";
            ShowHome();
            try
            {
                CoreSession.Check(game, engines);
                if (multiplayer)
                {
                    room = new CodeRoom();
                    using var registration = work.Token.Register(room.Dispose);
                    await FindPlayer(game, code, room);
                }
                else
                {
                    var rom = await Task.Run(() => fresh
                        ? game.PrepareNew(data, game.ReadImage(), "local-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"))
                        : game.Prepare(data, "solo"), work.Token);
                    await Launch(game, rom, null, work.Token);
                }
            }
            catch (OperationCanceledException) { notice = "已取消。"; }
            catch (Exception ex) { notice = (work.IsCancellationRequested ? "已取消。" : "启动失败。 " + ex.Message); }
            finally
            {
                room?.Dispose();
                room = null;
                operation = null;
                if (session == null) ShowHome();
            }
        }

        private async Task FindPlayer(Game game, string code, CodeRoom search)
        {
            var preparation = await Task.Run(() =>
            {
                game.OriginalHash = game.ReadHash(game.RomPath);
                var image = game.ReadImage();
                byte[] save = BattleSaves.Read(game, BattleSaves.Default(data, game, image.Hash), image.Hash);
                return (image, save);
            }, search.Token);
            System.Uri address = signalingServer;
            SetStatus(address == null ? "正在局域网查找同码玩家…" : "正在信令服务器查找同码玩家…");
            using var wifi = address == null ? (WifiManager)ApplicationContext.GetSystemService(WifiService) : null;
            using var multicast = wifi?.CreateMulticastLock("boksol-room");
            multicast?.Acquire();
            try
            {
                var progress = new Progress<RoomRoster>(roster =>
                {
                    rosterSummary = roster.Joined + "/" + roster.Capacity + " · " + roster.Summary();
                    if (operation != null && session == null && roomPlayers != null)
                    {
                        roomPlayers.Text = rosterSummary;
                        roomPlayers.Visibility = ViewStates.Visible;
                    }
                });
                using var match = await search.Find(code, game.Id, preparation.image.Hash, address,
                    playerName, Math.Min(roomSize, game.MaxPlayers), progress);
                try
                {
                    SetStatus("配对成功，正在建立游戏连线…");
                    var rom = await BattleLaunch.Prepare(game, data, preparation.image, preparation.save, match, search.Token);
                    await Launch(game, rom, match, search.Token);
                }
                catch (Exception ex) when (ex is not PeerMatchException && !search.Token.IsCancellationRequested)
                {
                    await match.ReportFailure(ex.Message);
                    throw;
                }
            }
            finally { if (multicast?.IsHeld == true) multicast.Release(); }
        }

        private async Task Launch(Game game, PreparedRom rom, CodeMatch match, CancellationToken token)
        {
            CoreSession live = await CoreSession.Open(game, rom, engines, match, token);
            try
            {
                string message = match == null ? "本地游戏" : await BattleLaunch.Enter(game, rom, live, match, token);
                token.ThrowIfCancellationRequested();
                if (!Active) throw new OperationCanceledException();
                if (live.Completion.IsCompleted)
                {
                    await live.Completion;
                    throw new IOException("游戏核心在启动时结束。");
                }
                session = live;
                if (match != null) message = match.Roster.Name(match.Seat) + " · " + message;
                ShowGame(game, live, message, match != null && match.Remote == null ? match.LocalAddress : null);
            }
            catch
            {
                session = null;
                try { await live.Stop(); }
                catch { /* Preserve the startup error. */ }
                finally { live.Dispose(); }
                throw;
            }
            _ = WatchGame(live);
        }

        private void ShowGame(Game game, CoreSession live, string message, string hostAddress)
        {
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);
            var page = Column();
            page.SetPadding(Dp(8), 0, Dp(8), Dp(4));
            var header = Row();
            header.AddView(Button("退出游戏", ConfirmExit));
            var title = Label(game.Title, 18, Gold);
            title.Gravity = GravityFlags.CenterVertical;
            header.AddView(title, new LinearLayout.LayoutParams(0, -1, 1));
            page.AddView(header);
            page.AddView(Label(message, 13, Muted));
            page.AddView(GameMediaActions(game, live, hostAddress));
            display = new GameDisplay(this, live);
            page.AddView(display, new LinearLayout.LayoutParams(-1, 0, 1));
            page.AddView(display.MakeControls());
            SetContentView(page);
        }

        private async Task WatchGame(CoreSession live)
        {
            try
            {
                await live.Completion;
                if (session == live) notice = "游戏已结束。";
            }
            catch (Exception ex) { if (session == live) notice = "游戏核心已退出。 " + ex.Message; }
            finally
            {
                if (session == live)
                {
                    StopMedia();
                    session = null;
                    live.Dispose();
                    ShowHome();
                }
            }
        }

        private void ConfirmExit()
        {
            new AlertDialog.Builder(this).SetMessage("结束当前游戏？")
                .SetPositiveButton("结束", (_, _) => { if (session != null) _ = session.Stop(); })
                .SetNegativeButton("继续", (_, _) => { }).Show();
        }

        public override bool DispatchKeyEvent(KeyEvent e)
        {
            if (CaptureBinding(e)) return true;
            if (display?.HandleKey(e) == true) return true;
            return base.DispatchKeyEvent(e);
        }

        public override void OnBackPressed()
        {
            if (configuringKeys) { pendingBinding = 0; configuringKeys = false; ShowHome(); }
            else if (session != null) ConfirmExit();
            else if (operation != null) CancelOperation();
            else base.OnBackPressed();
        }

        protected override void OnPause()
        {
            display?.ReleaseInput();
            base.OnPause();
        }

        protected override void OnDestroy()
        {
            CancelOperation();
            StopMedia();
            if (session != null) _ = session.Stop();
            base.OnDestroy();
        }

        private void CancelOperation() { operation?.Cancel(); }

        private void SetStatus(string text)
        {
            notice = text;
            if (Active && homeStatus != null) homeStatus.Text = text;
        }

        internal int Dp(int size) => (int)(Resources.DisplayMetrics.Density * size + 0.5f);

        private LinearLayout Page()
        {
            var root = Column();
            var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
            content.SetPadding(Dp(18), Dp(12), Dp(18), Dp(20));
            var scroll = new ScrollView(this) { FillViewport = true };
            scroll.AddView(content);
            root.AddView(scroll);
            SetContentView(root);
            return content;
        }

        private LinearLayout Column()
        {
            var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
            column.SetBackgroundColor(Background);
            column.SetFitsSystemWindows(true);
            return column;
        }

        private LinearLayout Row() => new LinearLayout(this) { Orientation = Orientation.Horizontal };

        private LinearLayout ActionRow(params (string title, Action action)[] actions)
        {
            var row = Row();
            foreach (var entry in actions)
            {
                var button = Button(entry.title, entry.action);
                button.Enabled = operation == null;
                row.AddView(button, new LinearLayout.LayoutParams(0, -2, 1));
            }
            return row;
        }

        private TextView Label(string text, int size, Color color)
        {
            var label = new TextView(this) { Text = text, TextSize = size };
            label.SetTextColor(color);
            label.SetPadding(Dp(4), Dp(4), Dp(4), Dp(4));
            return label;
        }

        internal Button Button(string title, Action action, bool highlight = false)
        {
            var button = new Button(this) { Text = title, TextSize = 14 };
            button.SetAllCaps(false);
            button.SetTextColor(highlight ? Gold : Color.White);
            button.BackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(Panel);
            button.Click += (_, _) => action();
            return button;
        }
    }
}
