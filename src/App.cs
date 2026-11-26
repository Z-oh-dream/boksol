using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle(Boksol.AppIdentity.Name)]
[assembly: AssemblyProduct(Boksol.AppIdentity.Name)]
[assembly: AssemblyVersion(Boksol.AppIdentity.Version + ".0")]
[assembly: AssemblyFileVersion(Boksol.AppIdentity.Version + ".0")]

namespace Boksol
{
    internal static class AppIdentity
    {
        public const string Name = "我太 boksol";
        public const string Version = "0.5.4";
    }

    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Lobby());
        }
    }

    internal sealed partial class Lobby : Form
    {
        private static readonly Color Background = Color.FromArgb(11, 17, 26);
        private static readonly Color Surface = Color.FromArgb(22, 33, 44);
        private static readonly Color InputColor = Color.FromArgb(10, 18, 26);
        private static readonly Color Outline = Color.FromArgb(57, 80, 91);
        private static readonly Color Ink = Color.FromArgb(237, 245, 249);
        private static readonly Color Quiet = Color.FromArgb(158, 181, 194);
        private static readonly Color Gold = Color.FromArgb(251, 191, 88);
        private static readonly Color Mint = Color.FromArgb(123, 221, 195);
        private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        private readonly string data;
        private readonly Image art;
        private readonly Panel header, footer, body, home;
        private readonly Panel[] pages = new Panel[4];
        private readonly Button[] navButtons = new Button[4];
        private readonly Button previous, next, host, join, cancel, copy, solo, chooseRom, choosePatch, clearPatch, selectSave, record, stopRecord, play, export, remove, share;
        private readonly Button battleSaveButton;
        private readonly Label edition, gameTitle, language, roomStatus, steps, recordingStatus, patchLabel;
        private readonly Label[] romSlots = new Label[Game.All.Length];
        private readonly Label pageTitle, keyHelp, featuredGame, featuredStatus;
        private readonly PictureBox brandIcon;
        private readonly Label brandName;
        private readonly Button back;
        private readonly TextBox invitation, signalingAddress, playerNameEditor;
        private readonly ComboBox roomSizeEditor;
        private Label matchMode, roomPlayers;
        private Uri signalingServer;
        private string playerName = "";
        private readonly GameKeyBindings bindings = new GameKeyBindings();
        private StartupScreen startup;
        private readonly GameViewport viewport;
        private readonly Label gameStatus;
        private readonly Button stopGame, recordGame, shareGame;
        private readonly ListBox movies;
        private readonly PictureBox moviePreview;
        private readonly TextBox movieFilter;
        private readonly Label movieInfo;
        private readonly CheckBox autoRecord;
        private int gameIndex;
        private int pageIndex;
        private int versionQuery;
        private bool busy;
        private CodeRoom room;
        private string selectedHash;
        private BattleSave battleSave;
        private bool pendingClose;
        private CoreSession emulator;
        private bool stoppingGame;
        private Recording currentRecording;
        private Spectating broadcast;
        private string battleHostAddress;
        private string watchCode;
        private Form guide;
        private Game Game { get { return Game.All[gameIndex]; } }

        public Lobby() : this(new OfflineStartupUpdateCheck()) { }

        internal Lobby(IStartupUpdateCheck updateCheck)
        {
            data = Path.Combine(root, "data");
            bindings.Load(KeyBindingsPath);
            using (Stream icon = typeof(Lobby).Assembly.GetManifestResourceStream("solar.ico"))
                Icon = icon == null ? SystemIcons.Application : new Icon(icon);
            using (Stream image = typeof(Lobby).Assembly.GetManifestResourceStream("solar.png"))
            {
                if (image == null) art = BrandArtwork.Create(Gold, Mint);
                else using (var original = Image.FromStream(image)) art = new Bitmap(original);
            }
            Text = AppIdentity.Name;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1190, 706);
            MinimumSize = new Size(1000, 732);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Background;
            ForeColor = Ink;
            Font = new Font("Microsoft YaHei UI", 10);
            KeyPreview = true;

            header = SurfacePanel(Background, DockStyle.Top);
            header.Height = 72;
            header.Width = ClientSize.Width;
            brandIcon = new PictureBox { Image = art, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            Place(header, brandIcon, 27, 11, 50, 50);
            brandName = Caption(AppIdentity.Name, 22, Gold, true);
            Place(header, brandName, 90, 13, 320, 46);
            back = ActionButton("‹  菜单", () => SelectPage(pageIndex == 3 ? 0 : -1));
            Place(header, back, 26, 17, 92, 37);
            pageTitle = Caption("", 17, Ink, true);
            Place(header, pageTitle, 138, 13, 380, 46);
            previous = ActionButton("‹", () => SwitchGame(-1), false, 17);
            next = ActionButton("›", () => SwitchGame(1), false, 17);
            edition = Caption("", 11, Ink, true);
            edition.TextAlign = ContentAlignment.MiddleCenter;
            AtRight(header, previous, 330, 17, 42, 38);
            AtRight(header, edition, 283, 17, 211, 38);
            AtRight(header, next, 67, 17, 42, 38);
            header.Paint += (s, e) => Line(e.Graphics, Outline, 0, header.Height - 1, header.Width, header.Height - 1);

            footer = SurfacePanel(Background, DockStyle.Bottom);
            footer.Height = 42;
            keyHelp = Caption("", 9, Quiet);
            Stretch(footer, keyHelp, 24, 5, 24, 32);
            footer.Paint += (s, e) => Line(e.Graphics, Outline, 0, 0, footer.Width, 0);

            body = new SolarPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 20) };
            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
            home = new SolarPanel { Dock = DockStyle.Fill, BackColor = Background };
            body.Controls.Add(home);
            var mainMenu = SurfacePanel(Background, DockStyle.Left);
            mainMenu.Width = 243;
            var featured = new SolarPanel { Artwork = art, Dock = DockStyle.Fill, BackColor = Surface };
            home.Controls.Add(featured);
            home.Controls.Add(mainMenu);
            var featureFooter = SurfacePanel(Background, DockStyle.Bottom);
            featureFooter.Height = 113;
            featured.Controls.Add(featureFooter);
            featuredGame = Caption("", 25, Ink, true);
            featuredStatus = Caption("", 11, Mint);
            Stretch(featureFooter, featuredGame, 25, 12, 20, 51);
            Stretch(featureFooter, featuredStatus, 27, 67, 20, 26);
            string[] navNames = { "对战", "录像", "设置", "退出" };
            for (int i = 0; i < navNames.Length; i++)
            {
                int item = i;
                navButtons[i] = ActionButton(navNames[i], () => { if (item == 3) Close(); else SelectPage(item); }, false, 24);
                Place(mainMenu, navButtons[i], 4, 60 + i * 86, 204, 69);
                navButtons[i].TextAlign = ContentAlignment.MiddleLeft;
                navButtons[i].Padding = new Padding(21, 0, 0, 0);
            }

            for (int i = 0; i < pages.Length; i++)
            {
                pages[i] = new SolarPanel { Dock = DockStyle.Fill, BackColor = Background };
                body.Controls.Add(pages[i]);
            }

            gameTitle = Caption("", 23, Ink, true);
            language = Caption("", 11, Mint);
            steps = Caption("", 10, Quiet);
            roomStatus = Caption("", 11, Gold, true);
            roomStatus.AccessibleName = "配对状态";
            invitation = TextField();
            invitation.AccessibleName = "联机码";
            invitation.Font = new Font("Consolas", 14, FontStyle.Bold);
            invitation.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && join.Enabled)
                {
                    e.SuppressKeyPress = true;
                    StartRoom();
                }
            };
            copy = ActionButton("复制联机码", CopyInvitation);
            host = ActionButton("生成六位码", GenerateCode);
            join = ActionButton("开始配对", StartRoom, true);
            cancel = ActionButton("取消配对", () => { if (room != null) room.Dispose(); });
            solo = ActionButton("本地启动", () => { if (emulator == null) StartSolo(); else SelectPage(3); });
            cancel.Visible = false;
            share = ActionButton("开放观战", ToggleSpectating);
            battleSaveButton = ActionButton("选择对战存档", ChooseSave);
            battleSaveButton.TextAlign = ContentAlignment.MiddleLeft;
            battleSaveButton.Padding = new Padding(9, 0, 8, 0);
            BuildBattlePage();
            invitation.TextChanged += (s, e) => RefreshControls();

            Panel session = pages[3];
            var sessionBar = SurfacePanel(Surface, DockStyle.Bottom);
            sessionBar.Height = 66;
            viewport = new GameViewport(bindings) { Dock = DockStyle.Fill };
            session.Controls.Add(viewport);
            session.Controls.Add(sessionBar);
            gameStatus = Caption("", 10, Mint);
            Stretch(sessionBar, gameStatus, 15, 9, 528, 48);
            recordGame = ActionButton("录制", () =>
            {
                if (currentRecording == null) StartRecording(); else currentRecording.Stop();
            });
            shareGame = ActionButton("开放观战", ToggleSpectating);
            stopGame = ActionButton("结束游戏", StopGame);
            AtRight(sessionBar, ActionButton("键位", ShowKeyBindings), 505, 15, 70, 36);
            AtRight(sessionBar, ActionButton("中文对照", ShowGuide), 424, 15, 94, 36);
            AtRight(sessionBar, recordGame, 319, 15, 88, 36);
            AtRight(sessionBar, shareGame, 220, 15, 104, 36);
            AtRight(sessionBar, stopGame, 105, 15, 92, 36);

            Panel replay = pages[1];
            var replayList = SurfacePanel(Background, DockStyle.Left);
            replayList.Width = 304;
            var replayDetail = SurfacePanel(Surface, DockStyle.Fill);
            replay.Controls.Add(replayDetail);
            replay.Controls.Add(replayList);
            Place(replayList, Caption("录像库", 19, Ink, true), 16, 14, 254, 44);
            Place(replayList, Caption("按文件名搜索", 10, Quiet), 16, 61, 255, 25);
            movieFilter = TextField();
            movieFilter.AccessibleName = "搜索录像文件名";
            Place(replayList, movieFilter, 16, 92, 266, 32);
            movies = new ListBox { BackColor = InputColor, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false, AccessibleName = "录像列表", Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
            Place(replayList, movies, 16, 133, 266, Math.Max(70, replayList.Height - 151));

            var replayControls = SurfacePanel(Surface, DockStyle.Bottom);
            replayControls.Height = 145;
            moviePreview = new PictureBox { Dock = DockStyle.Fill, BackColor = InputColor, SizeMode = PictureBoxSizeMode.Zoom,
                AccessibleName = "所选录像预览" };
            replayDetail.Padding = new Padding(16, 15, 16, 0);
            replayDetail.Controls.Add(moviePreview);
            replayDetail.Controls.Add(replayControls);
            movieInfo = Caption("", 10, Ink);
            Stretch(replayControls, movieInfo, 0, 6, 8, 29);
            record = ActionButton("开始录制", StartRecording, true);
            stopRecord = ActionButton("结束录制", () => { if (currentRecording != null) currentRecording.Stop(); });
            play = ActionButton("播放", PlayRecording);
            export = ActionButton("导出视频", ExportRecording);
            remove = ActionButton("删除", DeleteRecording);
            Place(replayControls, record, 0, 84, 106, 39);
            Place(replayControls, stopRecord, 116, 84, 106, 39);
            recordingStatus = Caption("", 10, Gold);
            Stretch(replayControls, recordingStatus, 232, 84, 8, 39);
            recordingStatus.Text = "启动游戏后可录制前台画面";
            Place(replayControls, play, 0, 41, 80, 34);
            Place(replayControls, export, 90, 41, 95, 34);
            var openMovies = ActionButton("显示文件", () => ShowFolder(RecordingFolder));
            Place(replayControls, openMovies, 195, 41, 95, 34);
            AtRight(replayControls, remove, 73, 41, 73, 34);
            movies.DoubleClick += (s, e) => PlayRecording();
            movies.SelectedIndexChanged += (s, e) => PreviewRecording();
            movieFilter.TextChanged += (s, e) => RefreshMovies();

            Panel settings = pages[2];
            var settingsPicker = SurfacePanel(Background, DockStyle.Left);
            settingsPicker.Width = 191;
            var settingsDeck = SurfacePanel(Surface, DockStyle.Fill);
            settingsDeck.Padding = new Padding(20, 10, 23, 20);
            settings.Controls.Add(settingsDeck);
            settings.Controls.Add(settingsPicker);
            var gameSettings = SurfacePanel(Surface, DockStyle.Fill);
            var connectionSettings = SurfacePanel(Surface, DockStyle.Fill);
            var controlSettings = SurfacePanel(Surface, DockStyle.Fill);
            var infoSettings = SurfacePanel(Surface, DockStyle.Fill);
            Panel[] sections = { gameSettings, connectionSettings, controlSettings, infoSettings };
            string[] sectionNames = { "游戏与汉化", "联机与录像", "键位", "关于" };
            var sectionButtons = new Button[sections.Length];
            Action<int> showSettings = index =>
            {
                for (int i = 0; i < sections.Length; i++)
                {
                    sections[i].Visible = i == index;
                    sectionButtons[i].ForeColor = i == index ? Gold : Ink;
                }
                sections[index].BringToFront();
            };
            for (int i = 0; i < sections.Length; i++)
            {
                int index = i;
                settingsDeck.Controls.Add(sections[i]);
                sectionButtons[i] = ActionButton(sectionNames[i], () => showSettings(index));
                Place(settingsPicker, sectionButtons[i], 14, 25 + i * 54, 154, 43);
            }
            settings = gameSettings;
            Place(settings, Caption("游戏与汉化", 21, Ink, true), 1, 12, 385, 57);
            Place(settings, Caption("游戏 ROM", 12, Ink, true), 2, 80, 350, 29);
            AddRomSlots(settings);
            chooseRom = ActionButton("导入 ROM", ChooseRoms);
            AtRight(settings, chooseRom, 132, 117, 132, 38);
            Place(settings, Caption("汉化补丁 · 当前作品", 12, Ink, true), 2, 236, 350, 29);
            patchLabel = Caption("", 10, Quiet);
            Stretch(settings, patchLabel, 2, 270, 141, 48);
            choosePatch = ActionButton("添加补丁", ChoosePatch);
            clearPatch = ActionButton("移除补丁", ClearPatch);
            AtRight(settings, choosePatch, 132, 270, 132, 38);
            AtRight(settings, clearPatch, 132, 316, 132, 38);
            Stretch(settings, Caption("支持的日文原版载入内置联机中文。其他汉化版本也可导入，所有玩家需使用同一 ROM 版本。", 10, Quiet), 2, 329, 144, 55);
            var folder = ActionButton("打开存档目录", () => ShowFolder(Path.Combine(data, "game-data")));
            var terms = ActionButton("查看 TPL 术语", ShowGuide);
            Place(settings, folder, 2, 406, 147, 38);
            Place(settings, terms, 159, 406, 146, 38);
            selectSave = ActionButton("选择对战存档", ChooseSave);
            Place(settings, selectSave, 315, 406, 146, 38);
            autoRecord = new CheckBox { Text = "联机启动后自动录制", ForeColor = Ink, BackColor = Surface, AutoSize = true };
            Place(connectionSettings, Caption("联机与录像", 21, Ink, true), 1, 12, 385, 57);
            Place(connectionSettings, Caption("名字", 12, Ink, true), 2, 78, 350, 29);
            playerNameEditor = TextField();
            playerNameEditor.Font = new Font("Microsoft YaHei UI", 11);
            playerNameEditor.MaxLength = RoomRoster.NameLimit;
            Stretch(connectionSettings, playerNameEditor, 2, 114, 104, 36);
            Place(connectionSettings, Caption("信令服务器地址", 12, Ink, true), 2, 162, 350, 29);
            signalingAddress = TextField();
            signalingAddress.MaxLength = 512;
            Stretch(connectionSettings, signalingAddress, 2, 198, 104, 36);
            AtRight(connectionSettings, ActionButton("保存", SaveConnectionSettings), 92, 196, 92, 36);
            KeyEventHandler saveConnection = (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                SaveConnectionSettings();
            };
            signalingAddress.KeyDown += saveConnection;
            playerNameEditor.KeyDown += saveConnection;
            Stretch(connectionSettings, Caption("留空使用局域网配对。远程玩家使用同一地址，以 ws:// 或 wss:// 开头。", 10, Quiet), 2, 235, 8, 41);
            Place(connectionSettings, Caption("远程房间人数", 12, Ink, true), 2, 280, 139, 30);
            roomSizeEditor = new ComboBox { BackColor = InputColor, ForeColor = Ink,
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            roomSizeEditor.Items.AddRange(new object[] { "2 人", "3 人", "4 人" });
            Place(connectionSettings, roomSizeEditor, 151, 280, 94, 35);
            Place(connectionSettings, autoRecord, 2, 340, 340, 25);
            Place(controlSettings, Caption("游戏键位", 21, Ink, true), 1, 12, 385, 57);
            Place(controlSettings, ActionButton("自定义键位", ShowKeyBindings), 2, 88, 152, 43);
            Place(infoSettings, Caption(AppIdentity.Name, 22, Gold, true), 1, 12, 385, 57);
            Stretch(infoSettings, Caption(AppIdentity.Version + "  ·  Windows x64 便携版", 11, Quiet), 2, 74, 8, 32);
            Stretch(infoSettings, Caption("GBA 模拟器  VBA-M 2.2.3\r\nDS 模拟器   melonDS 1.1", 11, Ink), 2, 122, 8, 63);
            var dataButton = ActionButton("打开数据目录", () => ShowFolder(data));
            Place(infoSettings, dataButton, 2, 210, 152, 43);
            showSettings(0);

            LoadSettings();
            RefreshRomSlots();
            autoRecord.CheckedChanged += (s, e) => StoreSettings();
            roomSizeEditor.SelectedIndexChanged += (s, e) => StoreSettings();
            ShowStartup(updateCheck);
            FormClosing += ClosingLobby;
            RefreshControls();
        }

        private static Panel SurfacePanel(Color color, DockStyle dock)
        {
            return new Panel { BackColor = color, Dock = dock };
        }

        private static Label Caption(string value, float size, Color color, bool bold = false)
        {
            return new Label
            {
                Text = value, ForeColor = color, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            };
        }

        private static TextBox TextField()
        {
            return new TextBox { BackColor = InputColor, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 12), MaxLength = 40, ShortcutsEnabled = true };
        }

        private static Button ActionButton(string title, Action action, bool primary = false, float size = 10)
        {
            var button = new SolarButton(primary ? Gold : Outline,
                primary ? Color.FromArgb(255, 213, 137) : Color.FromArgb(42, 60, 69))
            {
                Text = title, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", size, FontStyle.Bold),
                BackColor = primary ? Gold : Surface, ForeColor = primary ? Background : Ink,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = primary ? Gold : Outline;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 213, 137) : Color.FromArgb(42, 53, 74);
            button.Click += (s, e) => action();
            return button;
        }

        private static void Place(Control parent, Control child, int x, int y, int w, int h)
        {
            child.SetBounds(x, y, w, h);
            parent.Controls.Add(child);
        }

        private static void Stretch(Control parent, Control child, int x, int y, int inset, int h)
        {
            child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Place(parent, child, x, y, Math.Max(30, parent.Width - x - inset), h);
        }

        private static void AtRight(Control parent, Control child, int inset, int y, int w, int h)
        {
            child.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Place(parent, child, parent.Width - inset, y, w, h);
        }

        private static void Line(Graphics graphics, Color color, int x, int y, int x2, int y2)
        {
            using (var pen = new Pen(color)) graphics.DrawLine(pen, x, y, x2, y2);
        }

        private void SelectPage(int index)
        {
            pageIndex = index;
            bool onMenu = index < 0;
            home.Visible = onMenu;
            back.Visible = pageTitle.Visible = !onMenu;
            brandIcon.Visible = brandName.Visible = onMenu;
            pageTitle.Text = onMenu ? "" : index == 3 ? AppIdentity.Name + " · " + Game.Title : navButtons[index].Text;
            back.Text = index == 3 ? "‹  对战" : "‹  菜单";
            for (int i = 0; i < pages.Length; i++)
                pages[i].Visible = i == index;
            RefreshKeyHelp();
            if (onMenu)
            {
                home.BringToFront();
                navButtons[0].Focus();
            }
            else
            {
                pages[index].BringToFront();
                if (index == 3 && emulator != null) viewport.Focus();
                else back.Focus();
            }
            if (index == 1) RefreshMovies();
            if (index == 2) RefreshRomSlots();
        }

        private void SwitchGame(int amount)
        {
            if (busy || emulator != null) return;
            gameIndex = (gameIndex + amount + Game.All.Length) % Game.All.Length;
            invitation.Clear();
            ChangeGame();
        }

        private async void ChangeGame()
        {
            Game game = Game;
            int query = ++versionQuery;
            selectedHash = null;
            battleSave = null;
            RefreshSaveCaption();
            edition.Text = game.Title;
            gameTitle.Text = game.Title;
            featuredGame.Text = game.Title;
            language.Text = game.Subtitle + "   ·   " + game.Platform;
            featuredStatus.Text = language.Text;
            roomStatus.Text = "正在检查 ROM…";
            steps.Text = game.Guide;
            patchLabel.Text = game.PatchPath ?? "未应用外部补丁";
            if (guide != null) guide.Close();
            RefreshControls();
            if (!File.Exists(game.RomPath))
            {
                roomStatus.Text = "先在设置中导入此作 ROM。";
                featuredStatus.Text = game.Platform + "  /  未选择 ROM";
                return;
            }
            try
            {
                string path = game.RomPath;
                string hash = await Task.Run(() => game.ReadHash(path));
                if (query != versionQuery || IsDisposed) return;
                string effectiveHash = game.PatchPath == null ? hash : await Task.Run(() => game.ReadImage().Hash);
                if (query != versionQuery || IsDisposed) return;
                game.OriginalHash = hash;
                selectedHash = effectiveHash;
                string editionName = hash == game.KnownHash ? game.KnownLanguage : "自选版本";
                if (OfficialText.Supports(game, effectiveHash))
                {
                    editionName = "原版日文 · 内置联机中文";
                    if (game.PatchPath == null) patchLabel.Text = "已启用内置联机中文";
                }
                if (game.PatchPath != null) editionName += " · 已选补丁";
                language.Text = game.Platform + "   /   " + editionName;
                featuredStatus.Text = language.Text;
                roomStatus.Text = "已就绪。输入或生成联机码。";
                RefreshSaveChoice();
            }
            catch (Exception ex) { if (query == versionQuery && !IsDisposed) roomStatus.Text = ex.Message; }
            if (!IsDisposed) RefreshControls();
        }

        private void RefreshControls()
        {
            bool idle = !busy && emulator == null;
            previous.Enabled = next.Enabled = chooseRom.Enabled = idle;
            selectSave.Enabled = battleSaveButton.Enabled = idle && selectedHash != null;
            choosePatch.Enabled = idle && selectedHash != null;
            clearPatch.Enabled = idle && Game.PatchPath != null;
            host.Enabled = idle;
            join.Enabled = idle && selectedHash != null;
            solo.Enabled = (idle && selectedHash != null) || (emulator != null && !stoppingGame);
            solo.Text = emulator == null ? "本地启动" : "返回游戏";
            cancel.Visible = room != null;
            copy.Enabled = invitation.Text.Length > 0;
            copy.Text = watchCode == null ? "复制联机码" : "复制观战码";
            invitation.ReadOnly = !idle;
            roomSizeEditor.Enabled = idle && signalingServer != null && Game.MaxPlayers > 2;
            record.Enabled = emulator != null && currentRecording == null;
            stopRecord.Enabled = currentRecording != null;
            play.Enabled = export.Enabled = remove.Enabled = movies.SelectedItem != null;
            shareGame.Enabled = share.Enabled = emulator != null && battleHostAddress != null;
            shareGame.Text = share.Text = broadcast == null ? "开放观战" : "结束观战";
            stopGame.Enabled = emulator != null && !stoppingGame;
            recordGame.Enabled = emulator != null && !stoppingGame;
            recordGame.Text = currentRecording == null ? "录制" : "结束录制";
        }

        private void GenerateCode()
        {
            invitation.Text = CodeRoom.FormatCode(CodeRoom.CreateCode());
            roomStatus.Text = "把联机码发给其他玩家，然后点击“开始配对”。";
        }

        private async void StartRoom()
        {
            if (busy || emulator != null) return;
            string code;
            if (!CodeRoom.TryCode(invitation.Text, out code))
            {
                roomStatus.Text = "输入六位联机码，或使用 4–16 位字母、数字及连字符的自定义码。";
                return;
            }
            Game game = Game;
            if (battleSave == null)
            {
                ChooseSave();
                if (busy || emulator != null || battleSave == null) return;
            }
            BattleSave chosen = battleSave;
            Uri address = signalingServer;
            string name = playerName;
            int seats = Math.Min(game.MaxPlayers, roomSizeEditor.SelectedIndex + 2);
            busy = true;
            room = new CodeRoom();
            roomPlayers.Text = "";
            roomStatus.Text = "正在准备 ROM 与存档";
            RefreshControls();
            try
            {
                CoreSession.Check(game, root);
                var image = await Task.Run(() => game.ReadImage());
                if (!String.Equals(image.Hash, selectedHash, StringComparison.Ordinal))
                    throw new InvalidDataException("ROM 已变化，请重新选择游戏和对战存档。");
                byte[] save = await Task.Run(() => BattleSaves.Read(game, chosen, image.Hash));
                room.Token.ThrowIfCancellationRequested();
                roomStatus.Text = address == null ? "正在局域网查找同码玩家" : "正在信令服务器查找同码玩家";
                var progress = new Progress<RoomRoster>(roster =>
                {
                    if (busy && !IsDisposed)
                        roomPlayers.Text = roster.Joined + "/" + roster.Capacity + " · " + roster.Summary();
                });
                using (var match = await room.Find(code, game.Id, image.Hash, address, name, seats, progress))
                {
                    try
                    {
                        roomStatus.Text = "游戏版本一致，正在准备游戏";
                        var rom = await BattleLaunch.Prepare(game, data, image, save, match, room.Token);
                        room.Token.ThrowIfCancellationRequested();
                        await Launch(game, rom, match, room.Token);
                    }
                    catch (Exception ex) when (!(ex is PeerMatchException) && !room.Token.IsCancellationRequested)
                    {
                        await match.ReportFailure(ex.Message);
                        throw;
                    }
                }
            }
            catch (OperationCanceledException) { if (!IsDisposed) roomStatus.Text = "已取消配对"; }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    roomStatus.Text = room.Token.IsCancellationRequested ? "已取消配对" : "配对失败。 " + ex.Message;
            }
            finally
            {
                if (room != null) room.Dispose();
                room = null;
                busy = false;
                if (!IsDisposed)
                {
                    RefreshControls();
                    if (pendingClose) Close();
                }
            }
        }

        private async void StartSolo(string newProfile = null, BattleSave practice = null)
        {
            if (busy || emulator != null) return;
            Game game = Game;
            busy = true;
            roomStatus.Text = "正在准备 ROM 与存档";
            RefreshControls();
            try
            {
                CoreSession.Check(game, root);
                var rom = await Task.Run(() =>
                {
                    if (practice != null)
                    {
                        var image = game.ReadImage();
                        if (image.Hash != selectedHash) throw new InvalidDataException("ROM 已变化，请重新选择存档。");
                        return game.PreparePractice(data, image, BattleSaves.Read(game, practice, image.Hash));
                    }
                    return newProfile == null ? game.Prepare(data, "solo") : game.PrepareNew(data, game.ReadImage(), newProfile);
                });
                if (!pendingClose)
                {
                    await Launch(game, rom, null, CancellationToken.None);
                    if (newProfile != null) gameStatus.Text = "请在原作里新建并保存，结束游戏后会出现在对战存档列表。";
                    if (practice != null) gameStatus.Text = "已载入练习档 · 在游戏标题菜单选择已有档继续";
                }
            }
            catch (Exception ex) { if (!IsDisposed) roomStatus.Text = "启动失败。 " + ex.Message; }
            finally
            {
                busy = false;
                if (!IsDisposed) { RefreshControls(); if (pendingClose) Close(); }
            }
        }

        private async Task Launch(Game game, PreparedRom rom, CodeMatch match, CancellationToken stop)
        {
            if (match != null) roomStatus.Text = "已配对，正在建立游戏连线";
            CoreSession session = await CoreSession.Open(game, rom, root, match, stop);
            string entry = null;
            try
            {
                stop.ThrowIfCancellationRequested();
                if (match != null)
                {
                    if (rom.HasBattleStart) roomStatus.Text = "房间玩家已连接，正在准备原作对战";
                    entry = await BattleLaunch.Enter(game, rom, session, match, stop);
                }
                stop.ThrowIfCancellationRequested();
                if (pendingClose) throw new OperationCanceledException();
                if (session.Completion.IsCompleted)
                {
                    await session.Completion;
                    throw new IOException("游戏核心在启动时结束。");
                }
            }
            catch
            {
                try { await session.Stop(); }
                catch { /* Preserve the startup failure. */ }
                finally { session.Dispose(); }
                throw;
            }
            emulator = session;
            battleHostAddress = match != null && match.IsHost && match.Remote == null ? match.LocalAddress : null;
            stoppingGame = false;
            SelectPage(3);
            viewport.Mount(session);
            roomStatus.Text = match == null ? "本地游戏已启动" : "游戏连线已建立";
            gameStatus.Text = match == null ? "本地游戏" : match.Roster.Name(match.Seat) + " · " + entry;
            RefreshControls();
            ObserveGame(session);
            if (match != null && autoRecord.Checked) StartRecording();
        }

        private async void ObserveGame(CoreSession session)
        {
            string status = "游戏已结束";
            try { await session.Completion; }
            catch (Exception ex) { status = "游戏异常结束。 " + ex.Message; }
            session.Dispose();
            if (emulator != session || IsDisposed) return;
            emulator = null;
            viewport.Forget();
            stoppingGame = false;
            battleHostAddress = null;
            if (broadcast != null) broadcast.Dispose();
            steps.Text = Game.Guide;
            roomStatus.Text = status;
            try { RefreshSaveChoice(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { roomStatus.Text = ex.Message; }
            if (pageIndex == 3) SelectPage(0);
            RefreshControls();
            if (pendingClose) Close();
        }

        private void StopGame()
        {
            if (emulator == null || stoppingGame) return;
            stoppingGame = true;
            gameStatus.Text = "正在结束游戏";
            RefreshControls();
            _ = emulator.Stop();
        }

        private void CopyInvitation()
        {
            if (invitation.Text.Length == 0) return;
            try
            {
                Clipboard.SetText(invitation.Text);
                if (!busy) roomStatus.Text = watchCode == null ? "联机码已复制" : "观战邀请已复制";
            }
            catch { roomStatus.Text = "剪贴板被占用。可手动选中并复制邀请。"; }
        }

        private static bool ParseInvitation(string input, out string address, out string code)
        {
            string[] parts = input.Trim().Split('/');
            address = null;
            code = null;
            IPAddress ip;
            if (parts.Length != 2 || parts[0].Split('.').Length != 4 ||
                !IPAddress.TryParse(parts[0], out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            string token = parts[1].ToUpperInvariant();
            if (!Spectating.IsValidCode(token)) return false;
            address = ip.ToString();
            code = token;
            return true;
        }

        private Form Dialog(string title, Size size, Form dialog = null)
        {
            if (dialog == null) dialog = new Form();
            dialog.Text = title;
            dialog.ClientSize = size;
            dialog.Icon = Icon;
            dialog.BackColor = Surface;
            dialog.ForeColor = Ink;
            dialog.Font = Font;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MinimizeBox = dialog.MaximizeBox = dialog.ShowInTaskbar = false;
            return dialog;
        }

        private async void ToggleSpectating()
        {
            if (broadcast != null) { broadcast.Dispose(); return; }
            CoreSession session = emulator;
            if (session == null || battleHostAddress == null) return;
            try
            {
                watchCode = Spectating.CreateCode();
                broadcast = new Spectating(session, watchCode);
                invitation.Text = battleHostAddress + "/" + watchCode;
                roomStatus.Text = "观战已开放。可复制观战邀请。";
                gameStatus.Text = "观战已开放。可返回对战页复制观战邀请。";
                RefreshControls();
                await broadcast.Completion;
                if (emulator != null) roomStatus.Text = "观战已结束";
            }
            catch (Exception ex) { roomStatus.Text = "观战开放失败。 " + ex.Message; }
            finally
            {
                if (broadcast != null) broadcast.Dispose();
                broadcast = null;
                watchCode = null;
                if (!IsDisposed)
                {
                    invitation.Clear();
                    RefreshControls();
                    if (pendingClose) Close();
                }
            }
        }

        private void OpenSpectator()
        {
            using (var dialog = Dialog("观战", new Size(640, 522)))
            {
                Place(dialog, Caption("观战邀请", 11, Ink, true), 20, 15, 350, 27);
                var input = TextField();
                input.AccessibleName = "观战邀请";
                if (invitation.Text.Contains("/")) input.Text = invitation.Text;
                Place(dialog, input, 20, 52, 461, 29);
                var state = Caption("输入开房方提供的观战邀请。", 10, Quiet);
                Place(dialog, state, 20, 86, 600, 33);
                var picture = new PictureBox { BackColor = InputColor, SizeMode = PictureBoxSizeMode.Zoom };
                Place(dialog, picture, 20, 124, 600, 378);
                CancellationTokenSource watching = null;
                Button view = null;
                view = ActionButton("播放", async () =>
                {
                    if (watching != null) { watching.Cancel(); return; }
                    string address, code;
                    if (!ParseInvitation(input.Text, out address, out code))
                    {
                        state.Text = "邀请格式为 主机IPv4地址/8位观战码。";
                        return;
                    }
                    using (var request = new CancellationTokenSource())
                    {
                        watching = request;
                        view.Text = "停止";
                        input.ReadOnly = true;
                        state.Text = "正在连接";
                        try
                        {
                            await Spectating.Watch(address, code, frame =>
                            {
                                if (dialog.IsDisposed || request.IsCancellationRequested) return;
                                if (frame == null)
                                {
                                    if (picture.Image == null) state.Text = "已连接，等待主机的游戏画面。";
                                    return;
                                }
                                Image last = picture.Image;
                                picture.Image = GameCapture.Decode(frame);
                                if (last != null) last.Dispose();
                                state.Text = "正在观战";
                            }, request.Token);
                        }
                        catch (Exception ex)
                        {
                            if (!dialog.IsDisposed) state.Text = request.IsCancellationRequested ? "观战已停止" : ex.Message;
                        }
                        finally
                        {
                            watching = null;
                            if (!dialog.IsDisposed) { input.ReadOnly = false; view.Text = "播放"; }
                        }
                    }
                }, true);
                Place(dialog, view, 492, 44, 128, 38);
                dialog.AcceptButton = view;
                dialog.FormClosing += (s, e) => { if (watching != null) watching.Cancel(); };
                dialog.ShowDialog(this);
                if (picture.Image != null) picture.Image.Dispose();
            }
        }

        private void ChoosePatch()
        {
            if (busy || emulator != null) return;
            using (var dialog = new OpenFileDialog { Filter = "IPS 或 BPS 补丁|*.ips;*.bps", Title = "选择汉化补丁" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Game.PatchPath = dialog.FileName;
                Game.BattleSaveId = null;
            }
            StoreSettings();
            ChangeGame();
        }

        private void ClearPatch()
        {
            Game.PatchPath = null;
            Game.BattleSaveId = null;
            StoreSettings();
            ChangeGame();
        }

        private void ChooseSave()
        {
            if (busy || emulator != null || selectedHash == null) return;
            bool create = false;
            BattleSave practice = null;
            using (var dialog = Dialog("对战存档 · " + Game.Title, new Size(568, 332)))
            {
                var state = Caption("", 10, Quiet);
                Place(dialog, state, 20, 12, 522, 39);
                var list = new ListBox
                {
                    BackColor = InputColor, ForeColor = Ink, AccessibleName = "可用对战存档",
                    BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false
                };
                Place(dialog, list, 20, 59, 523, 151);
                if (Game.IsDs)
                    Place(dialog, Caption("内置档使用第 1 槽。个人档的槽位在原作内选择。", 10, Quiet), 20, 213, 522, 27);
                Action select = () =>
                {
                    var item = list.SelectedItem as BattleSave;
                    if (item == null) return;
                    SetBattleSave(item.Id);
                    dialog.Close();
                };
                var confirm = ActionButton("使用所选", select, true);
                var practiceButton = ActionButton("用此档练习", () =>
                {
                    practice = list.SelectedItem as BattleSave;
                    if (practice != null) dialog.Close();
                });
                list.SelectedIndexChanged += (s, e) => confirm.Enabled = practiceButton.Enabled = list.SelectedItem != null;
                list.DoubleClick += (s, e) => select();
                Action reload = () =>
                {
                    list.Items.Clear();
                    try
                    {
                        BattleSave[] saves = BattleSaves.List(data, Game, selectedHash);
                        list.Items.AddRange(saves);
                        list.SelectedItem = saves.FirstOrDefault(s => battleSave != null && battleSave.Id == s.Id) ?? saves.FirstOrDefault();
                        state.Text = saves.Length > 0 ? "选择用于本场对战的存档。" : "还没有存档。可新建游戏档或导入已有文件。";
                    }
                    catch (Exception ex) { state.Text = "无法读取存档列表。 " + ex.Message; }
                    confirm.Enabled = practiceButton.Enabled = list.SelectedItem != null;
                };
                var import = ActionButton("导入存档", () =>
                {
                    using (var file = new OpenFileDialog
                    {
                        Filter = Game.IsDs ? "DS 游戏存档|*.sav" : "GBA 游戏存档|*.sav;*.sa1",
                        Title = "导入 " + Game.Title + " 的游戏存档"
                    })
                    {
                        if (file.ShowDialog(dialog) != DialogResult.OK) return;
                        try
                        {
                            var save = BattleSaves.Import(data, Game, selectedHash, file.FileName);
                            reload();
                            list.SelectedItem = list.Items.Cast<BattleSave>().FirstOrDefault(s => s.Id == save.Id);
                        }
                        catch (Exception ex) { state.Text = "导入失败。 " + ex.Message; }
                    }
                });
                var newSave = ActionButton("新建存档", () =>
                {
                    create = true;
                    dialog.Close();
                });
                Place(dialog, confirm, 20, 254, 120, 40);
                Place(dialog, practiceButton, 153, 254, 120, 40);
                Place(dialog, import, 286, 254, 120, 40);
                Place(dialog, newSave, 419, 254, 120, 40);
                dialog.AcceptButton = confirm;
                reload();
                dialog.ShowDialog(this);
            }
            if (practice != null) StartSolo(practice: practice);
            if (!create) return;
            string profile = "local-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            StartSolo(profile);
        }

        private void SetBattleSave(string id)
        {
            Game.BattleSaveId = id;
            StoreSettings();
            RefreshSaveChoice();
        }

        private void RefreshSaveChoice()
        {
            battleSave = BattleSaves.Default(data, Game, selectedHash);
            RefreshSaveCaption();
        }

        private void RefreshSaveCaption()
        {
            selectSave.Text = battleSave == null ? "选择对战存档" : "已选对战存档…";
            battleSaveButton.Text = battleSave == null ? "选择、新建或导入存档" : battleSave.Name +
                (Game.IsDs && battleSave.IsBuiltIn ? " · 第 1 槽" : "");
        }

        private void LoadSettings()
        {
            string file = Path.Combine(data, "rom-paths.txt");
            string[] lines = new string[0];
            try { if (File.Exists(file)) lines = File.ReadAllLines(file, Encoding.UTF8); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            autoRecord.Checked = lines.Contains("autorecord=1");
            Func<string, string> setting = key => lines.FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.Ordinal))
                ?.Substring(key.Length + 1);
            string address = setting(RemoteRoom.SettingsKey);
            if (address != null && RemoteRoom.TryAddress(address, out signalingServer)) signalingAddress.Text = address;
            if (RoomRoster.TryDecodeName(setting(RoomRoster.NameKey) ?? "", out playerName))
                playerNameEditor.Text = playerName;
            roomSizeEditor.SelectedIndex = Int32.TryParse(setting(RoomRoster.SeatsKey), out int seats) &&
                seats >= 2 && seats <= 4 ? seats - 2 : 0;
            UpdateMatchMode();
            string[] defaults = { "续我们的太阳.gba", "新我们的太阳.gba", "我们的太阳DS.nds" };
            for (int i = 0; i < Game.All.Length; i++)
            {
                Game game = Game.All[i];
                string adjacent = Path.Combine(root, defaults[i]);
                game.RomPath = game.MatchesRom(adjacent) ? adjacent : null;
                game.PatchPath = game.SavePath = game.BattleSaveId = game.OriginalHash = null;
                string line = lines.FirstOrDefault(l => l.StartsWith(game.Id + "|", StringComparison.Ordinal));
                if (line == null) continue;
                string[] fields = line.Split('|');
                if (fields.Length < 3 || fields.Length > 6) continue;
                if (!game.MatchesRom(fields[1])) continue;
                game.RomPath = fields[1];
                if (File.Exists(fields[2])) game.PatchPath = fields[2];
                if (fields.Length >= 4 && File.Exists(fields[3])) game.SavePath = fields[3];
                if (fields.Length >= 5 && fields[4].Length > 0) game.BattleSaveId = fields[4];
            }
        }

        private void SaveConnectionSettings()
        {
            string value = signalingAddress.Text.Trim();
            if (!RemoteRoom.TryAddress(value, out var address))
            {
                MessageBox.Show(this, "请输入以 ws:// 或 wss:// 开头的有效信令服务器地址。", "信令服务器地址");
                return;
            }
            signalingServer = address;
            signalingAddress.Text = value;
            playerName = RoomRoster.CleanName(playerNameEditor.Text);
            playerNameEditor.Text = playerName;
            StoreSettings();
            UpdateMatchMode();
            RefreshControls();
        }

        private void UpdateMatchMode()
        {
            matchMode.Text = signalingServer == null ? "局域网配对" : "远程配对";
        }

        private void StoreSettings()
        {
            try
            {
                Directory.CreateDirectory(data);
                string entries = String.Join("\n", Game.All.Select(g => g.Id + "|" + g.RomPath + "|" + g.PatchPath + "|" +
                    g.SavePath + "|" + g.BattleSaveId)) +
                    "\nautorecord=" + (autoRecord.Checked ? "1" : "0");
                if (signalingServer != null) entries += "\n" + RemoteRoom.SettingsKey + "=" + signalingServer.AbsoluteUri;
                if (playerName.Length != 0) entries += "\n" + RoomRoster.NameKey + "=" + RoomRoster.EncodeName(playerName);
                entries += "\n" + RoomRoster.SeatsKey + "=" + (roomSizeEditor.SelectedIndex + 2);
                Game.AtomicWrite(Path.Combine(data, "rom-paths.txt"), Encoding.UTF8.GetBytes(entries));
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法保存设置"); }
        }

        private string RecordingFolder { get { return Path.Combine(data, "recordings"); } }

        private async void StartRecording()
        {
            CoreSession session = emulator;
            if (session == null || currentRecording != null) return;
            try
            {
                Directory.CreateDirectory(RecordingFolder);
                string filename = Game.Id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".avi";
                currentRecording = new Recording(session, Path.Combine(RecordingFolder, filename));
                recordingStatus.Text = "正在录制游戏画面。";
                RefreshControls();
                int count = await currentRecording.Completion;
                recordingStatus.Text = count > 0 ? "录像已保存" : "没有录到游戏画面。";
            }
            catch (Exception ex) { recordingStatus.Text = "录制失败。 " + ex.Message; }
            finally
            {
                currentRecording = null;
                if (!IsDisposed)
                {
                    RefreshMovies();
                    RefreshControls();
                    if (pendingClose) Close();
                }
            }
        }

        private void RefreshMovies()
        {
            movies.Items.Clear();
            if (Directory.Exists(RecordingFolder))
                movies.Items.AddRange(Directory.GetFiles(RecordingFolder, "*.avi").OrderByDescending(File.GetLastWriteTime)
                    .Select(Path.GetFileName).Where(name => name.IndexOf(movieFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToArray());
            if (movies.Items.Count > 0) movies.SelectedIndex = 0;
            else PreviewRecording();
        }

        private string SelectedMovie
        {
            get { return movies.SelectedItem == null ? null : Path.Combine(RecordingFolder, movies.SelectedItem.ToString()); }
        }

        private void PreviewRecording()
        {
            string path = SelectedMovie;
            play.Enabled = export.Enabled = remove.Enabled = path != null;
            if (moviePreview.Image != null) moviePreview.Image.Dispose();
            moviePreview.Image = null;
            movieInfo.Text = path == null ? "没有符合条件的录像" : Path.GetFileName(path);
            if (path == null) return;
            try { moviePreview.Image = Recording.Preview(path); }
            catch (Exception) { movieInfo.Text = "无法读取录像预览。"; }
        }

        private void ExportRecording()
        {
            string path = SelectedMovie;
            if (path == null) return;
            using (var dialog = new SaveFileDialog { FileName = Path.GetFileName(path), Filter = "AVI 视频|*.avi", DefaultExt = "avi" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (String.Equals(path, dialog.FileName, StringComparison.OrdinalIgnoreCase)) return;
                try { File.Copy(path, dialog.FileName, true); recordingStatus.Text = "录像已导出"; }
                catch (Exception ex) { recordingStatus.Text = "导出失败。 " + ex.Message; }
            }
        }

        private void DeleteRecording()
        {
            string path = SelectedMovie;
            if (path == null || MessageBox.Show(this, "将所选录像移到回收站？", "删除录像",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                recordingStatus.Text = "录像已移到回收站";
                RefreshMovies();
            }
            catch (Exception ex) { recordingStatus.Text = "无法删除录像。 " + ex.Message; }
        }

        private void PlayRecording()
        {
            if (movies.SelectedItem == null) return;
            try { Process.Start(new ProcessStartInfo(Path.Combine(RecordingFolder, movies.SelectedItem.ToString())) { UseShellExecute = true }); }
            catch (Exception ex) { recordingStatus.Text = "无法打开录像。 " + ex.Message; }
        }

        private void ShowFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开文件夹"); }
        }

        private void ShowGuide()
        {
            if (guide != null) { guide.Activate(); return; }
            guide = new Form {
                Text = Game.Title + "  ·  中文对照", Icon = Icon, BackColor = Background, ForeColor = Ink,
                ClientSize = new Size(475, 450), MinimumSize = new Size(400, 385), TopMost = true,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.SizableToolWindow
            };
            var content = new TextBox {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Surface, ForeColor = Ink, BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 11)
            };
            string ds = "标题菜单\r\nNEW GAME　开始新游戏\r\nLOAD　读取存档\r\nOPTION　设置\r\nWIRELESS　无线通信\r\n\r\n" +
                "对战菜单\r\n通信対戦　通信对战\r\nDSワイヤレス対戦　DS 无线对战\r\nDSダウンロード対戦　DS 下载对战\r\nロード　读取存档\r\nデータがありません　没有存档\r\n\r\n" +
                "规则与配置\r\nルール選択　规则选择\r\n時間　时限\r\nラウンド　局数\r\nステージ　场地\r\nキャラクター選択　角色选择\r\n武器　武器\r\nアイテム　道具\r\n決定　确认\r\n\r\n" +
                DsTerms.Reference;
            string gba = "TPL 术语与补充译名\r\n" +
                "Django　强戈（EXE4 / EXE5 / EXE6）\r\n" +
                "Solar Boy Django　太阳少年强戈（EXE4）\r\n" +
                "Otenko　天鼓、天鼓大人（EXE4 / EXE5 / EXE6）\r\n" +
                "原作武器 Gun Del Sol　太阳铳（EXE4 / EXE5 / EXE6）\r\n" +
                "Crossover Battle 2　跨界对战2（EXE6）\r\n" +
                "Sabata　萨巴塔";
            content.Text = Game.Guide + "\r\n\r\n" + (Game.IsDs ? ds : gba);
            guide.Padding = new Padding(16);
            guide.Controls.Add(content);
            guide.FormClosed += (s, e) => guide = null;
            guide.Show(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (startup != null) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Escape && pageIndex >= 0) { SelectPage(pageIndex == 3 ? 0 : -1); return true; }
            if (pageIndex == 3) return base.ProcessCmdKey(ref msg, keyData);
            if (ActiveControl == movies && keyData == Keys.Enter) { PlayRecording(); return true; }
            if (ActiveControl == movies && keyData == Keys.Delete) { DeleteRecording(); return true; }
            if (pageIndex == 1 && keyData == Keys.E && ReplayExportShortcut && ActiveControl != movieFilter)
            {
                ExportRecording();
                return true;
            }
            if (ActiveControl is TextBox || ActiveControl is ComboBox || ActiveControl is ListBox)
                return base.ProcessCmdKey(ref msg, keyData);
            return HandleMenuKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
        }

        private void ClosingLobby(object sender, FormClosingEventArgs e)
        {
            if ((busy || currentRecording != null || broadcast != null || emulator != null) && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                pendingClose = true;
                if (room != null) room.Dispose();
                if (currentRecording != null) currentRecording.Stop();
                if (broadcast != null) broadcast.Dispose();
                if (emulator != null && !stoppingGame) StopGame();
            }
        }
    }
}
