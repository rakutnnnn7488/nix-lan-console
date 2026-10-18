using System;
using System.Drawing;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NixLanConsole
{
    // Native WinForms replacement for the Edge app window. This is the new UI host;
    // data and process monitoring remain in DisplayServer while screens are migrated.
    internal sealed class NativeConsoleWindow : Form
    {
        private readonly Label HostState;
        private readonly Label CharacterState;
        private readonly Label[] MetricValues = new Label[4];
        private readonly Label[] MetricNames = new Label[4];
        private readonly Panel[] MetricFills = new Panel[4];
        private readonly PictureBox[] MetricIcons = new PictureBox[4];
        private readonly List<Image> MetricIconImages = new List<Image>();
        private readonly Label GameList;
        private readonly Label RecordDayBadge;
        private readonly Panel RecapSparkline;
        private GameSession LatestRecap;
        private readonly PictureBox RecordAvatar;
        private readonly PictureBox RecordTail;
        private readonly Label RecordsOpenHint;
        private readonly Panel Chart;
        private readonly Panel CharacterCanvas;
        private readonly Panel Desktop;
        private readonly Panel Rail;
        private readonly Panel RightRail;
        private readonly Panel Taskbar;
        private readonly Button StartButton;
        private readonly Button LiveTab;
        private readonly Label TaskbarStatus;
        private readonly Panel CharacterCard;
        private readonly Panel StatsCard;
        private readonly Panel RecordsCard;
        private readonly Panel LiveCard;
        private readonly Panel LiveCanvas;
        private readonly Panel TransitionOverlay;
        private readonly Timer RefreshTimer;
        private readonly Timer AnimationTimer;
        private readonly List<double[]> Samples = new List<double[]>();
        private readonly List<RoleClip> AmeClips = new List<RoleClip>();
        private readonly List<RoleClip> KangelClips = new List<RoleClip>();
        private readonly Dictionary<RoleScene, List<RoleClip>> SceneClips = new Dictionary<RoleScene, List<RoleClip>>();
        private readonly Dictionary<string, Image> FrameCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly Random Randomizer = new Random();
        private Image CharacterBackground;
        private Image SideBackground;
        private Image DesktopBackground;
        private Image DesktopFullBackground;
        private Bitmap DesktopPaintCache;
        private Image TaskbarBackground;
        private readonly Dictionary<string, Image> DesktopIcons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Control> ShortcutControls = new List<Control>();
        private Image WindowFrame;
        private Image WindowInactiveFrame;
        private Image CardMinimizeImage;
        private Image CardMaximizeImage;
        private Image CardCloseImage;
        private Image JineBackground;
        private Image LiveTemplate;
        private DateTime TransformationStarted;
        private bool Transforming;
        private bool PreviousGameRunning;
        private string LiveGameName = "";
        private Panel ActiveCard;
        private PrivateFontCollection PixelFonts;
        private PrivateFontCollection InterfaceFonts;
        private Font PixelText;
        private Font PixelSmallText;
        private Font PixelTinyText;
        private Font ManagerText;
        private Font ManagerSmallText;
        private Font ManagerTinyText;
        private Font MetricNumberText;
        private Font MetricCompactText;
        private Font MetricTemperatureText;
        private Font LiveText;
        private Image StartBackground;
        private Image StartPressedBackground;
        private RoleClip CurrentClip;
        private RoleScene CurrentScene;
        private Image DisplayedFrame;
        private DateTime ClipStarted;
        private bool IsKangel;
        private bool IsGameRunning;
        private string RoleAssetError;
        private bool Dragging;
        private Point DragOrigin;
        private readonly Dictionary<string, double[]> SavedPositions = new Dictionary<string, double[]>();
        private static string LayoutFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole", "ui-layout.json"); } }

        public NativeConsoleWindow()
        {
            Text = "副机控制台";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            BackColor = Color.FromArgb(244, 224, 248);
            Font = new Font("Microsoft YaHei UI", 10F);
            DoubleBuffered = true;

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 237, 250) };
            Rail = new BufferedPanel { Dock = DockStyle.Left, Width = 242, BackColor = Color.FromArgb(235, 220, 245) };
            Rail.Paint += PaintLeftRail;
            RightRail = new BufferedPanel { Dock = DockStyle.Right, Width = 242, BackColor = Color.FromArgb(235, 220, 245) };
            RightRail.Paint += PaintRightRail;
            var close = WindowButton("×");
            var closeMenu = new ContextMenuStrip();
            closeMenu.Items.Add("关闭界面（保留后台监控）", null, delegate { Hide(); });
            closeMenu.Items.Add("退出整个程序", null, delegate { Environment.Exit(0); });
            close.Click += delegate { closeMenu.Show(close, new Point(0, close.Height)); };
            var minimize = WindowButton("—"); minimize.Click += delegate { Hide(); };
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            minimize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Location = new Point(RightRail.Width - 34, 2);
            minimize.Location = new Point(RightRail.Width - 70, 2);
            RightRail.Controls.Add(close); RightRail.Controls.Add(minimize);
            body.Controls.Add(Rail); body.Controls.Add(RightRail);

            var center = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 235, 250) };

            Desktop = new BufferedPanel { BackColor = Color.FromArgb(250, 235, 250) };
            Desktop.Paint += PaintDesktop;
            var desktopMenu = new ContextMenuStrip();
            desktopMenu.Items.Add("恢复默认窗口位置", null, delegate { SavedPositions.Clear(); SaveLayout(); LayoutDesktop(); });
            Desktop.ContextMenuStrip = desktopMenu;
            AddDesktopIcon("直播", "icon_desktop_youtube.png", 50, 55, delegate { FocusCard(CharacterCard); });
            AddDesktopIcon("陪玩", "icon_desktop_asobu.png", 50, 175, delegate { using (var dialog = new NativeRecordsDialog()) dialog.ShowDialog(this); });
            AddDesktopIcon("睡觉", "icon_desktop_neru.png", 50, 295, delegate { Hide(); });
            AddDesktopIcon("吃药", "icon_desktop_okusuri.png", 50, 415, delegate { FocusCard(StatsCard); });
            AddDesktopIcon("因特网", "icon_desktop_internet.png", 50, 535, delegate { OpenSettings(); });
            AddDesktopIcon("出门", "icon_desktop_odekake.png", 50, 655, delegate { Hide(); });

            CharacterCard = Card("Webcam", 716);
            CharacterState = new Label { Text = "糖糖 · 日常待机 ♡" };
            CharacterCanvas = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(252, 244, 251) };
            CharacterCanvas.Paint += PaintCharacter;
            CharacterCard.Controls.Add(CharacterCanvas); Desktop.Controls.Add(CharacterCard);

            StatsCard = Card("任务管理器", 392);
            var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = new Padding(0), Padding = new Padding(9, 6, 9, 7), BackColor = Color.FromArgb(250, 242, 251) };
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            metrics.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            for (int i = 0; i < 4; i++) metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 16));
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
            HostState = new Label { Text = "主机离线", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.FromArgb(82, 79, 153), BackColor = Color.FromArgb(224, 211, 242), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) }; metrics.Controls.Add(HostState, 0, 0);
            string[] metricNames = { "CPU", "内存", "GPU", "温度" };
            for (int i = 0; i < metricNames.Length; i++)
            {
                int rowIndex = i;
                var row = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3), BackColor = Color.FromArgb(250, 242, 251) };
                MetricIcons[i] = new PictureBox { Location = new Point(0, 2), Size = new Size(42, 36), SizeMode = PictureBoxSizeMode.Zoom };
                MetricValues[i] = new Label { Text = "—", Location = new Point(51, 18), Size = new Size(155, 25), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(59, 50, 145), Font = new Font("Consolas", 13F, FontStyle.Bold) };
                var name = new Label { Text = metricNames[i], Location = new Point(51, 0), Size = new Size(160, 19), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(97, 165, 192), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };
                MetricNames[i] = name;
                var track = new Panel { Height = 22, BackColor = Color.FromArgb(218, 191, 230), BorderStyle = BorderStyle.FixedSingle };
                MetricFills[i] = new Panel { Dock = DockStyle.Left, Width = 0, BackColor = Color.FromArgb(105, 179, 211) }; track.Controls.Add(MetricFills[i]);
                row.Controls.Add(MetricIcons[i]); row.Controls.Add(name); row.Controls.Add(MetricValues[i]); row.Controls.Add(track);
                row.Resize += delegate
                {
                    bool compact = row.Height < 40;
                    int trackWidth = Math.Min(125, Math.Max(65, row.Width / 3));
                    track.Height = compact ? 12 : 22;
                    track.Bounds = new Rectangle(row.Width - trackWidth - 1, Math.Max(2, (row.Height - track.Height) / 2), trackWidth, track.Height);
                    MetricIcons[rowIndex].Bounds = new Rectangle(0, 0, compact ? 34 : 42, compact ? 28 : 36);
                    name.Location = new Point(compact ? 39 : 51, 0);
                    name.Width = Math.Max(46, track.Left - name.Left);
                    name.Height = compact ? 12 : 19;
                    MetricValues[rowIndex].Location = new Point(compact ? 39 : 51, compact ? 10 : 17);
                    MetricValues[rowIndex].Height = compact ? 18 : 31;
                    MetricValues[rowIndex].Width = Math.Max(52, track.Left - MetricValues[rowIndex].Left);
                    if (MetricNumberText != null) MetricValues[rowIndex].Font = compact ? MetricCompactText : rowIndex == 3 ? MetricTemperatureText : MetricNumberText;
                    if (ManagerSmallText != null) name.Font = compact ? ManagerTinyText : ManagerSmallText;
                    else if (PixelText != null) name.Font = compact ? PixelSmallText : PixelText;
                };
                metrics.Controls.Add(row, 0, i + 1);
            }

            Chart = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 232, 250), Margin = new Padding(0, 4, 0, 0) };
            Chart.Paint += PaintChart;
            metrics.Controls.Add(Chart, 0, 5); StatsCard.Controls.Add(metrics); Desktop.Controls.Add(StatsCard);

            RecordsCard = Card("游戏记录", 615);
            var jine = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(228, 155, 233), BackgroundImageLayout = ImageLayout.Stretch };
            RecordDayBadge = new Label { Text = "第 1 天  /  主机游戏日志  /  最近 7 天", TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(206, 210, 239), ForeColor = Color.FromArgb(63, 46, 144), Height = 35, Dock = DockStyle.Top };
            RecordAvatar = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, BackColor = Color.Transparent };
            RecordTail = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, BackColor = Color.Transparent };
            GameList = new Label { Text = "等待主机游戏记录…", AutoEllipsis = true, ForeColor = Color.FromArgb(70, 59, 143), BackColor = Color.FromArgb(218, 237, 255), Font = new Font("Microsoft YaHei UI", 10F), Padding = new Padding(12), Cursor = Cursors.Hand };
            GameList.Click += delegate { ShowLatestGameRecap(); };
            RecapSparkline = new BufferedPanel { BackColor = Color.FromArgb(235, 242, 255), Cursor = Cursors.Hand, Visible = false };
            RecapSparkline.Paint += PaintRecapSparkline;
            RecapSparkline.Click += delegate { ShowLatestGameRecap(); };
            RecordsOpenHint = new Label { Text = "查看完整游戏记录  ▶", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(59, 55, 142), BackColor = Color.FromArgb(210, 239, 81), Cursor = Cursors.Hand };
            RecordsOpenHint.Click += delegate { using (var dialog = new NativeRecordsDialog()) dialog.ShowDialog(this); };
            jine.Controls.Add(GameList); jine.Controls.Add(RecapSparkline); jine.Controls.Add(RecordTail); jine.Controls.Add(RecordAvatar); jine.Controls.Add(RecordsOpenHint); jine.Controls.Add(RecordDayBadge);
            jine.Resize += delegate { LayoutRecordMessages(jine); };
            RecordsCard.Controls.Add(jine); Desktop.Controls.Add(RecordsCard);
            LiveCard = Card("直播", 1336);
            LiveCanvas = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(255, 248, 255) };
            LiveCanvas.Paint += PaintLive;
            LiveCard.Controls.Add(LiveCanvas); LiveCard.Visible = false; Desktop.Controls.Add(LiveCard);
            TransitionOverlay = new BufferedPanel { BackColor = Color.FromArgb(250, 235, 250), Visible = false };
            TransitionOverlay.Paint += PaintTransformation;
            Desktop.Controls.Add(TransitionOverlay);
            center.Controls.Add(Desktop); body.Controls.Add(center); Controls.Add(body);
            FocusCard(RecordsCard);
            Rail.SendToBack(); RightRail.SendToBack();
            Taskbar = new BufferedPanel { Height = 40, BackColor = Color.FromArgb(247, 224, 247), Padding = new Padding(4, 5, 4, 5) };
            Taskbar.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (TaskbarBackground != null)
                {
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    e.Graphics.DrawImage(TaskbarBackground, Taskbar.ClientRectangle, new Rectangle(Point.Empty, TaskbarBackground.Size), GraphicsUnit.Pixel);
                }
                else using (var pen = new Pen(Color.FromArgb(89, 62, 156), 2)) e.Graphics.DrawLine(pen, 0, 1, Taskbar.Width, 1);
            };
            StartButton = new Button { Text = "开始", Dock = DockStyle.Left, Width = 136, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(38, 31, 94), BackColor = Color.FromArgb(246, 222, 249), Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), BackgroundImageLayout = ImageLayout.Stretch };
            StartButton.FlatAppearance.BorderSize = 0;
            StartButton.Click += delegate { OpenSettings(); };
            StartButton.MouseDown += delegate { if (StartPressedBackground != null) StartButton.BackgroundImage = StartPressedBackground; };
            StartButton.MouseUp += delegate { StartButton.BackgroundImage = StartBackground; };
            Taskbar.Controls.Add(StartButton);
            foreach (string tabName in new[] { "▣  任务管理器", "▣  Webcam", "▣  JINE", "▣  直播" })
            {
                var tab = new Button { Text = tabName, Dock = DockStyle.Left, Width = 160, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(238, 211, 242), ForeColor = Color.FromArgb(65, 48, 145), Font = new Font("Consolas", 9F, FontStyle.Bold) };
                tab.FlatAppearance.BorderColor = Color.FromArgb(116, 91, 173);
                string selected = tabName;
                tab.Click += delegate { if (selected.Contains("直播")) { if (LiveCard.Visible) FocusCard(LiveCard); } else if (selected.Contains("JINE")) FocusCard(RecordsCard); else if (selected.Contains("任务")) FocusCard(StatsCard); else FocusCard(CharacterCard); };
                if (selected.Contains("直播")) { LiveTab = tab; tab.Visible = false; }
                Taskbar.Controls.Add(tab); tab.BringToFront();
            }
            TaskbarStatus = new Label { Text = "○ 主机离线", Dock = DockStyle.Right, Width = 230, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(73, 55, 147), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };
            Taskbar.Controls.Add(TaskbarStatus);
            center.Controls.Add(Taskbar); Taskbar.BringToFront();
            center.Resize += delegate
            {
                Taskbar.Bounds = new Rectangle(0, center.Height - Taskbar.Height, center.Width, Taskbar.Height);
                Desktop.Bounds = new Rectangle(0, 0, center.Width, Math.Max(0, center.Height - Taskbar.Height));
                Taskbar.BringToFront();
            };
            Desktop.Resize += delegate { LayoutDesktop(); };
            Resize += delegate
            {
                Rail.Width = RightRail.Width = Math.Max(90, (int)(ClientSize.Width * 242.0 / 1920));
                Taskbar.Height = Math.Max(30, (int)(ClientSize.Height * 40.0 / 1080));
                StartButton.Width = Math.Max(90, (int)(ClientSize.Width * 136.0 / 1920));
                Taskbar.Bounds = new Rectangle(0, center.Height - Taskbar.Height, center.Width, Taskbar.Height);
                Desktop.Bounds = new Rectangle(0, 0, center.Width, Math.Max(0, center.Height - Taskbar.Height));
                LayoutDesktop();
            };
            Rail.Width = RightRail.Width = Math.Max(90, (int)(ClientSize.Width * 242.0 / 1920));
            Taskbar.Height = Math.Max(30, (int)(ClientSize.Height * 40.0 / 1080));
            StartButton.Width = Math.Max(90, (int)(ClientSize.Width * 136.0 / 1920));
            Taskbar.Bounds = new Rectangle(0, center.Height - Taskbar.Height, center.Width, Taskbar.Height);
            Desktop.Bounds = new Rectangle(0, 0, center.Width, Math.Max(0, center.Height - Taskbar.Height));
            LoadLayout();
            LayoutDesktop();

            RefreshTimer = new Timer { Interval = 1000 };
            RefreshTimer.Tick += delegate { RefreshData(); };
            RefreshTimer.Start();
            LoadRoleAssets();
            AnimationTimer = new Timer { Interval = 83 };
            AnimationTimer.Tick += delegate
            {
                if (CurrentClip != null && (DateTime.UtcNow - ClipStarted).TotalSeconds >= CurrentClip.Duration) SelectRoleClip(CurrentScene);
                Image frame = CurrentFrame();
                if (!Object.ReferenceEquals(frame, DisplayedFrame)) { DisplayedFrame = frame; CharacterCanvas.Invalidate(); }
                if (Transforming)
                {
                    if ((DateTime.UtcNow - TransformationStarted).TotalSeconds >= 3.8) FinishTransformation();
                    else TransitionOverlay.Invalidate();
                }
                else if (LiveCard.Visible) LiveCanvas.Invalidate();
            };
            AnimationTimer.Start();
            FormClosed += delegate
            {
                RefreshTimer.Stop(); AnimationTimer.Stop();
                if (CharacterBackground != null) CharacterBackground.Dispose();
                if (SideBackground != null) SideBackground.Dispose();
                if (DesktopBackground != null) DesktopBackground.Dispose();
                if (DesktopFullBackground != null) DesktopFullBackground.Dispose();
                if (DesktopPaintCache != null) DesktopPaintCache.Dispose();
                if (TaskbarBackground != null) TaskbarBackground.Dispose();
                foreach (var image in DesktopIcons.Values) image.Dispose();
                if (WindowFrame != null) WindowFrame.Dispose();
                if (WindowInactiveFrame != null) WindowInactiveFrame.Dispose();
                if (CardMinimizeImage != null) CardMinimizeImage.Dispose();
                if (CardMaximizeImage != null) CardMaximizeImage.Dispose();
                if (CardCloseImage != null) CardCloseImage.Dispose();
                if (JineBackground != null) JineBackground.Dispose();
                if (LiveTemplate != null) LiveTemplate.Dispose();
                if (RecordAvatar.Image != null) RecordAvatar.Image.Dispose();
                if (RecordTail.Image != null) RecordTail.Image.Dispose();
                if (ManagerText != null) ManagerText.Dispose();
                if (ManagerSmallText != null) ManagerSmallText.Dispose();
                if (ManagerTinyText != null) ManagerTinyText.Dispose();
                if (MetricNumberText != null) MetricNumberText.Dispose();
                if (MetricCompactText != null) MetricCompactText.Dispose();
                if (MetricTemperatureText != null) MetricTemperatureText.Dispose();
                if (LiveText != null) LiveText.Dispose();
                if (InterfaceFonts != null) InterfaceFonts.Dispose();
                if (PixelText != null) PixelText.Dispose();
                if (PixelSmallText != null) PixelSmallText.Dispose();
                if (PixelTinyText != null) PixelTinyText.Dispose();
                if (PixelFonts != null) PixelFonts.Dispose();
                if (StartBackground != null) StartBackground.Dispose();
                if (StartPressedBackground != null) StartPressedBackground.Dispose();
                foreach (var image in FrameCache.Values) image.Dispose();
                foreach (var image in MetricIconImages) image.Dispose();
            };
            RefreshData();
        }

        private static Button WindowButton(string text)
        {
            return new Button { Text = text, Width = 32, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(248, 238, 250), ForeColor = Color.FromArgb(65, 51, 142), Font = new Font("Consolas", 12F, FontStyle.Bold), Margin = new Padding(2) };
        }

        private void OpenSettings() { using (var dialog = new NativeSettingsDialog()) dialog.ShowDialog(this); }

        private void FocusCard(Panel card)
        {
            ActiveCard = card;
            card.Visible = true;
            card.BringToFront();
            foreach (var window in new[] { CharacterCard, StatsCard, RecordsCard }) if (window != null) window.Invalidate();
        }

        private void AddDesktopIcon(string label, string assetName, int x, int y, Action action)
        {
            var icon = new BufferedPanel { Tag = assetName, Size = new Size(88, 98), Location = new Point(x, y), BackColor = Color.Transparent, Cursor = Cursors.Hand };
            icon.Paint += delegate(object sender, PaintEventArgs e)
            {
                Image image;
                if (DesktopIcons.TryGetValue(assetName, out image))
                {
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    int side = Math.Min(64, (int)(icon.Width * 0.73));
                    e.Graphics.DrawImage(image, new Rectangle((icon.Width - side) / 2, 0, side, side), new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
                }
                using (var font = PixelText == null ? new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) : new Font(PixelText.FontFamily, 16, FontStyle.Regular, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(e.Graphics, label, font, new Rectangle(0, Math.Min(64, (int)(icon.Width * 0.73)) + 4, icon.Width, 26), Color.FromArgb(63, 47, 144), TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            };
            icon.Click += delegate { action(); };
            Desktop.Controls.Add(icon);
            ShortcutControls.Add(icon);
        }

        private void PaintLeftRail(object sender, PaintEventArgs e)
        {
            if (SideBackground != null)
            {
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                e.Graphics.DrawImage(SideBackground, new Rectangle(2, 0, Rail.Width, Rail.Height), new Rectangle(Point.Empty, SideBackground.Size), GraphicsUnit.Pixel);
            }
        }

        private void PaintRightRail(object sender, PaintEventArgs e)
        {
            if (SideBackground == null) return;
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            e.Graphics.TranslateTransform(RightRail.Width, 0);
            e.Graphics.ScaleTransform(-1, 1);
            e.Graphics.DrawImage(SideBackground, new Rectangle(2, 0, RightRail.Width, RightRail.Height), new Rectangle(Point.Empty, SideBackground.Size), GraphicsUnit.Pixel);
            e.Graphics.ResetTransform();
        }

        private Panel Card(string title, int width)
        {
            var card = new BufferedPanel { Name = title, Width = width, BackColor = Color.FromArgb(255, 248, 255), Padding = new Padding(10, 42, 10, 12), Margin = new Padding(8) };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                Image frame = card == ActiveCard || WindowInactiveFrame == null ? WindowFrame : WindowInactiveFrame;
                if (frame != null) DrawNineSlice(e.Graphics, frame, card.ClientRectangle, card == ActiveCard || WindowInactiveFrame == null);
                else using (var pen = new Pen(Color.FromArgb(119, 96, 177), 2)) e.Graphics.DrawRectangle(pen, 1, 1, card.ClientSize.Width - 3, card.ClientSize.Height - 3);
                using (var font = PixelText == null ? new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) : new Font(PixelText.FontFamily, 17, FontStyle.Regular, GraphicsUnit.Pixel)) using (var brush = new SolidBrush(Color.FromArgb(70, 51, 150))) e.Graphics.DrawString("■ " + title, font, brush, 18, 11);
                Image[] buttonImages = { CardMinimizeImage, CardMaximizeImage, CardCloseImage };
                using (var pen = new Pen(Color.FromArgb(67, 46, 155), 2))
                using (var font = new Font("Consolas", 9F, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.FromArgb(67, 46, 155)))
                {
                    string[] labels = { "_", "□", "×" };
                    for (int button = 0; button < 3; button++)
                    {
                        var bounds = CardButtonBounds(card, button);
                        if (buttonImages[button] != null)
                        {
                            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                            e.Graphics.DrawImage(buttonImages[button], bounds, new Rectangle(Point.Empty, buttonImages[button].Size), GraphicsUnit.Pixel);
                        }
                        else { e.Graphics.DrawRectangle(pen, bounds); e.Graphics.DrawString(labels[button], font, brush, bounds.X + 3, bounds.Y - 1); }
                    }
                }
            };
            bool moving = false; Point origin = Point.Empty;
            card.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || e.Y >= 40) return;
                for (int command = 0; command < 3; command++)
                {
                    if (!CardButtonBounds(card, command).Contains(e.Location)) continue;
                    if (command == 0 || command == 2) card.Visible = false;
                    else if (command == 1)
                    {
                        if (card.Tag is Rectangle) { card.Bounds = (Rectangle)card.Tag; card.Tag = null; }
                        else { card.Tag = card.Bounds; card.Bounds = new Rectangle(0, 0, Desktop.Width, Desktop.Height); }
                    }
                    return;
                }
                moving = true; origin = e.Location; card.Capture = true; FocusCard(card);
            };
            card.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!moving) return;
                Point next = new Point(Math.Max(0, Math.Min(Desktop.Width - card.Width, card.Left + e.X - origin.X)),
                    Math.Max(0, Math.Min(Desktop.Height - card.Height, card.Top + e.Y - origin.Y)));
                if (next != card.Location) card.Location = next;
            };
            card.MouseUp += delegate { if (moving) { moving = false; card.Capture = false; RememberPosition(card); SaveLayout(); } };
            return card;
        }

        // WindowBase.prefab: header x=8, width=window-20; button centers
        // are -64, -40.7 and -16 from the header's right anchor.
        private static Rectangle CardButtonBounds(Control card, int index)
        {
            int[] leftFromRight = { 86, 63, 38 };
            return new Rectangle(card.Width - leftFromRight[index], 11, 20, 20);
        }

        private static void DrawNineSlice(Graphics graphics, Image image, Rectangle target, bool active)
        {
            if (target.Width < 24 || target.Height < 70) return;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            // Unity Sprite.border: x=left, y=bottom, z=right, w=top.
            int left = active ? 69 : 70, right = active ? 12 : 14, top = 42, bottom = 22;
            int[] sx = { 0, left, image.Width - right }, sy = { 0, top, image.Height - bottom };
            int[] sw = { left, image.Width - left - right, right }, sh = { top, image.Height - top - bottom, bottom };
            int[] dx = { target.Left, target.Left + left, target.Right - right }, dy = { target.Top, target.Top + top, target.Bottom - bottom };
            int[] dw = { left, target.Width - left - right, right }, dh = { top, target.Height - top - bottom, bottom };
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                graphics.DrawImage(image, new Rectangle(dx[x], dy[y], dw[x], dh[y]), new Rectangle(sx[x], sy[y], sw[x], sh[y]), GraphicsUnit.Pixel);
        }

        private void LayoutDesktop()
        {
            if (Desktop == null || Desktop.Width < 100 || Desktop.Height < 100) return;
            double scale = Math.Min(Desktop.Width / 1436.0, Desktop.Height / 1038.0);
            for (int i = 0; i < ShortcutControls.Count; i++) ShortcutControls[i].Bounds = ScaleRect(50, 55 + i * 120, 88, 98, scale);
            CharacterCard.Bounds = ScaleRect(174, 62, 716, 508, scale);
            StatsCard.Bounds = ScaleRect(1038, 22, 392, 405, scale);
            RecordsCard.Bounds = ScaleRect(815, 447, 615, 575, scale);
            LiveCard.Bounds = ScaleRect(50, 50, 1336, 940, scale);
            TransitionOverlay.Bounds = Desktop.ClientRectangle;
            foreach (var card in new[] { CharacterCard, StatsCard, RecordsCard })
            {
                double[] position;
                if (SavedPositions.TryGetValue(card.Name, out position)) card.Location = new Point((int)(position[0] * Math.Max(0, Desktop.Width - card.Width)), (int)(position[1] * Math.Max(0, Desktop.Height - card.Height)));
            }
        }
        private void RememberPosition(Panel card)
        {
            SavedPositions[card.Name] = new[] { Math.Max(0, Math.Min(1, card.Left / (double)Math.Max(1, Desktop.Width - card.Width))), Math.Max(0, Math.Min(1, card.Top / (double)Math.Max(1, Desktop.Height - card.Height))) };
        }
        private void LoadLayout()
        {
            try
            {
                if (!File.Exists(LayoutFile)) return;
                var saved = new JavaScriptSerializer().Deserialize<Dictionary<string, double[]>>(File.ReadAllText(LayoutFile, Encoding.UTF8));
                if (saved == null) return;
                foreach (var item in saved) if (item.Value != null && item.Value.Length == 2 && item.Value.All(v => !Double.IsNaN(v) && !Double.IsInfinity(v) && v >= 0 && v <= 1)) SavedPositions[item.Key] = item.Value;
            }
            catch (Exception ex) { StartupDiagnostics.Write("layout load: " + ex.Message); }
        }
        private void SaveLayout()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LayoutFile));
                string pending = LayoutFile + ".pending";
                File.WriteAllText(pending, new JavaScriptSerializer().Serialize(SavedPositions), Encoding.UTF8);
                if (File.Exists(LayoutFile)) File.Replace(pending, LayoutFile, null); else File.Move(pending, LayoutFile);
            }
            catch (Exception ex) { StartupDiagnostics.Write("layout save: " + ex.Message); }
        }
        private void PaintDesktop(object sender, PaintEventArgs e)
        {
            if (DesktopPaintCache == null || DesktopPaintCache.Size != Desktop.ClientSize) RebuildDesktopPaintCache();
            if (DesktopPaintCache == null) return;
            Rectangle clip = Rectangle.Intersect(e.ClipRectangle, Desktop.ClientRectangle);
            if (!clip.IsEmpty) e.Graphics.DrawImage(DesktopPaintCache, clip, clip, GraphicsUnit.Pixel);
        }
        private void RebuildDesktopPaintCache()
        {
            if (DesktopPaintCache != null) { DesktopPaintCache.Dispose(); DesktopPaintCache = null; }
            if (Desktop.Width < 1 || Desktop.Height < 1) return;
            DesktopPaintCache = new Bitmap(Desktop.Width, Desktop.Height);
            using (Graphics graphics = Graphics.FromImage(DesktopPaintCache))
            {
                graphics.Clear(Color.FromArgb(250, 235, 250));
            if (DesktopFullBackground != null)
            {
                // FHDbg contains the full 1920px scene. This panel represents the
                // 1436px center between the two 242px side rails.
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                var source = new Rectangle(242, 0, 1436, 1033);
                graphics.DrawImage(DesktopFullBackground, Desktop.ClientRectangle, source, GraphicsUnit.Pixel);
            }
            else if (DesktopBackground != null)
            {
                using (var brush = new TextureBrush(DesktopBackground, System.Drawing.Drawing2D.WrapMode.Tile))
                {
                    brush.TranslateTransform(26, 24);
                    graphics.FillRectangle(brush, Desktop.ClientRectangle);
                }
            }
            else using (var brush = new SolidBrush(Color.FromArgb(220, 230, 245)))
                for (int y = 12; y < Desktop.Height; y += 32)
                    for (int x = 12 + (y / 32 % 2) * 16; x < Desktop.Width; x += 32)
                        graphics.FillRectangle(brush, x, y, 7, 7);
            }
        }
        private static Rectangle ScaleRect(int x, int y, int width, int height, double scale)
        {
            return new Rectangle((int)(x * scale), (int)(y * scale), (int)(width * scale), (int)(height * scale));
        }

        private void RefreshData()
        {
            DashboardState state = DisplayServer.Snapshot();
            HostState.Text = state.online ? "主机在线  /  " + state.host : "主机离线";
            TaskbarStatus.Text = (state.online ? "● 主机在线" : "○ 主机离线") + "  ·  " + (state.role == "kangel" ? "超天酱" : "糖糖");
            MetricValues[0].Text = state.online && state.cpu >= 0 ? Math.Round(state.cpu) + "%" : "—";
            MetricValues[1].Text = state.online && state.memoryTotal > 0 ? Math.Round(state.memoryUsed / state.memoryTotal * 100) + "%" : "—";
            MetricValues[2].Text = state.online && state.gpu >= 0 ? Math.Round(state.gpu) + "%" : "—";
            MetricValues[3].Text = state.online ? Temp(state.cpuTemp) + " / " + Temp(state.gpuTemp) : "—";
            double[] values = { state.cpu, state.online && state.memoryTotal > 0 ? state.memoryUsed / state.memoryTotal * 100 : -1, state.gpu, -1 };
            for (int i = 0; i < 3; i++) MetricFills[i].Width = (int)(MetricFills[i].Parent.ClientSize.Width * Math.Max(0, Math.Min(100, values[i])) / 100.0);
            MetricFills[3].Width = (int)(MetricFills[3].Parent.ClientSize.Width * Math.Max(0, Math.Min(100, Math.Max(state.cpuTemp, state.gpuTemp))) / 100.0);
            Samples.Add(new[] { values[0], values[1], values[2] }); if (Samples.Count > 60) Samples.RemoveAt(0); Chart.Invalidate();
            CharacterState.Text = state.role == "kangel" || state.role == "transform" ? "超天酱 · " + (state.games != null && state.games.Length > 0 ? "游戏运行中 ♡" : "待机 ♡") : "糖糖 · 日常待机 ♡";
            bool kangel = state.role == "kangel" || state.role == "transform";
            bool playing = state.games != null && state.games.Length > 0;
            bool wasKangel = IsKangel;
            RoleScene nextScene = playing && kangel ? RoleScene.LiveGaming : kangel ? RoleScene.AfterGame : state.online ? RoleScene.Daily : RoleScene.HostOffline;
            if (nextScene != CurrentScene || CurrentClip == null) SelectRoleClip(nextScene);
            LiveGameName = playing ? state.games[0] : "";
            if (playing && !PreviousGameRunning)
            {
                if (!wasKangel && kangel) StartTransformation();
                else { LiveCard.Visible = true; LiveTab.Visible = true; FocusCard(LiveCard); }
            }
            else if (!playing && PreviousGameRunning)
            {
                Transforming = false; TransitionOverlay.Visible = false; LiveCard.Visible = false; LiveTab.Visible = false;
            }
            if (!playing && !kangel) { Transforming = false; TransitionOverlay.Visible = false; LiveCard.Visible = false; LiveTab.Visible = false; }
            PreviousGameRunning = playing;
            if (LiveCard.Visible) LiveCanvas.Invalidate();
            var latestFinished = (state.sessions ?? new GameSession[0]).Where(s => s != null && s.end > s.start && !String.Equals(s.game, "Wallpaper Engine", StringComparison.OrdinalIgnoreCase)).OrderByDescending(s => s.end).FirstOrDefault();
            LatestRecap = latestFinished;
            RecapSparkline.Visible = latestFinished != null && latestFinished.samples != null && latestFinished.samples.Length > 1;
            if (RecapSparkline.Visible) RecapSparkline.Invalidate();
            GameList.Text = latestFinished == null ? RecentSummary(state.sessions) :
                "本次游戏结束啦 ♡\r\n" + latestFinished.game + "  ·  " + FormatDuration(latestFinished.end - latestFinished.start) +
                "\r\n点击查看游玩回顾与硬件曲线 ▶";
            LayoutRecordMessages(GameList.Parent);
        }

        private void ShowLatestGameRecap()
        {
            var latest = (DisplayServer.Snapshot().sessions ?? new GameSession[0])
                .Where(s => s != null && s.end > s.start && !String.Equals(s.game, "Wallpaper Engine", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.end).FirstOrDefault();
            if (latest == null) { using (var dialog = new NativeRecordsDialog()) dialog.ShowDialog(this); return; }
            using (var dialog = new NativeSessionRecapDialog(latest)) dialog.ShowDialog(this);
        }

        private void LayoutRecordMessages(Control surface)
        {
            if (surface == null || surface.Width < 120) return;
            bool compact = surface.Width < 450;
            int margin = Math.Max(18, (int)(surface.Width * 0.075));
            int avatarSize = compact ? 36 : 50;
            int bubbleX = margin + avatarSize + 9;
            int bubbleWidth = Math.Min(surface.Width - bubbleX - margin, compact ? 310 : 390);
            int lines = GameList.Text.Split(new[] { '\n' }).Length;
            int bubbleHeight = Math.Min(Math.Max(54, lines * (compact ? 16 : 22) + 23), Math.Max(54, surface.Height - 160));
            int bubbleY = Math.Max(62, (int)(surface.Height * 0.19));
            GameList.Bounds = new Rectangle(bubbleX, bubbleY, bubbleWidth, bubbleHeight);
            RecapSparkline.Bounds = new Rectangle(bubbleX, GameList.Bottom + 3, bubbleWidth, 86);
            RecordAvatar.Bounds = new Rectangle(margin, bubbleY + 2, avatarSize, avatarSize);
            RecordTail.Bounds = new Rectangle(bubbleX - 8, bubbleY + 11, 8, 24);
            RecordsOpenHint.Bounds = new Rectangle(Math.Max(margin, surface.Width - margin - Math.Min(220, surface.Width / 2)), Math.Min(surface.Height - 52, (RecapSparkline.Visible ? RecapSparkline.Bottom : GameList.Bottom) + 18), Math.Min(220, surface.Width / 2), compact ? 30 : 38);
        }

        private void PaintRecapSparkline(object sender, PaintEventArgs e)
        {
            if (LatestRecap == null || LatestRecap.samples == null) return;
            var samples = LatestRecap.samples.Where(s => s != null).OrderBy(s => s.utc).ToArray();
            if (samples.Length < 2) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var label = new SolidBrush(Color.FromArgb(70, 59, 143)))
                e.Graphics.DrawString("CPU     GPU     温度", PixelTinyText ?? Font, label, 9, 5);
            Color[] colors = { Color.FromArgb(76, 58, 170), Color.FromArgb(87, 168, 211), Color.FromArgb(207, 109, 176) };
            long span = Math.Max(1, LatestRecap.end - LatestRecap.start);
            for (int metric = 0; metric < 3; metric++)
            {
                Func<GameMetricSample, double> value = metric == 0 ? (Func<GameMetricSample, double>)(s => s.cpu) : metric == 1 ? (Func<GameMetricSample, double>)(s => s.gpu) : (Func<GameMetricSample, double>)(s => Math.Max(s.cpuTemp, s.gpuTemp));
                using (var pen = new Pen(colors[metric], 2))
                {
                    Point? previous = null;
                    foreach (var sample in samples)
                    {
                        double v = value(sample);
                        if (v < 0) { previous = null; continue; }
                        int x = 10 + (int)((RecapSparkline.Width - 20) * Math.Max(0, Math.Min(1, (sample.utc - LatestRecap.start) / (double)span)));
                        int y = 77 - (int)(Math.Max(0, Math.Min(100, v)) * 48 / 100);
                        var point = new Point(x, y);
                        if (previous.HasValue) e.Graphics.DrawLine(pen, previous.Value, point);
                        previous = point;
                    }
                }
            }
        }

        private void LoadRoleAssets()
        {
            try
            {
                string assets = Path.Combine(BuildInfo.InstallDir, "game-assets");
                string manifest = Path.Combine(assets, "native-role", "角色动画.json");
                if (File.Exists(Path.Combine(assets, "zpix.ttf")))
                {
                    PixelFonts = new PrivateFontCollection();
                    PixelFonts.AddFontFile(Path.Combine(assets, "zpix.ttf"));
                    PixelText = new Font(PixelFonts.Families[0], 16, FontStyle.Regular, GraphicsUnit.Pixel);
                    PixelSmallText = new Font(PixelFonts.Families[0], 11, FontStyle.Regular, GraphicsUnit.Pixel);
                    PixelTinyText = new Font(PixelFonts.Families[0], 10, FontStyle.Regular, GraphicsUnit.Pixel);
                    MetricNumberText = new Font(PixelFonts.Families[0], 28, FontStyle.Regular, GraphicsUnit.Pixel);
                    MetricCompactText = new Font(PixelFonts.Families[0], 15, FontStyle.Regular, GraphicsUnit.Pixel);
                    MetricTemperatureText = new Font(PixelFonts.Families[0], 21, FontStyle.Regular, GraphicsUnit.Pixel);
                    HostState.Font = PixelText; GameList.Font = PixelText; RecordDayBadge.Font = PixelText;
                    RecordsOpenHint.Font = PixelText; TaskbarStatus.Font = PixelText;
                    for (int i = 0; i < MetricValues.Length; i++) MetricValues[i].Font = MetricValues[i].Parent.Height < 40 ? MetricCompactText : i == 3 ? MetricTemperatureText : MetricNumberText;
                    foreach (var name in MetricNames) name.Font = PixelText;
                    foreach (Control control in Taskbar.Controls) if (control is Button) control.Font = PixelText;
                }
                string managerFontPath = Path.Combine(assets, "PixelMplus10-Regular.ttf");
                string liveFontPath = Path.Combine(assets, "DinkieBitmap-9px.ttf");
                try
                {
                    InterfaceFonts = new PrivateFontCollection();
                    if (File.Exists(managerFontPath))
                    {
                        InterfaceFonts.AddFontFile(managerFontPath);
                        var family = InterfaceFonts.Families.First(f => f.Name.StartsWith("PixelMplus10", StringComparison.OrdinalIgnoreCase));
                        ManagerText = new Font(family, 18, FontStyle.Regular, GraphicsUnit.Pixel);
                        ManagerSmallText = new Font(family, 16, FontStyle.Regular, GraphicsUnit.Pixel);
                        ManagerTinyText = new Font(family, 12, FontStyle.Regular, GraphicsUnit.Pixel);
                        // PixelMplus10 is used for metric labels. Zpix renders the
                        // Chinese host status consistently with the rest of the UI.
                        foreach (var name in MetricNames) name.Font = name.Parent.Height < 40 ? ManagerTinyText : ManagerSmallText;
                    }
                    if (File.Exists(liveFontPath))
                    {
                        InterfaceFonts.AddFontFile(liveFontPath);
                        var family = InterfaceFonts.Families.First(f => f.Name.StartsWith("DinkieBitmap 9px", StringComparison.OrdinalIgnoreCase));
                        LiveText = new Font(family, 18, FontStyle.Regular, GraphicsUnit.Pixel);
                    }
                }
                catch (Exception ex) { StartupDiagnostics.Write("optional interface font: " + ex.Message); }
                if (File.Exists(Path.Combine(assets, "bg_stream_native.png"))) CharacterBackground = LoadDetachedImage(Path.Combine(assets, "bg_stream_native.png"));
                if (File.Exists(Path.Combine(assets, "sidebar_noon.png"))) SideBackground = LoadDetachedImage(Path.Combine(assets, "sidebar_noon.png"));
                if (File.Exists(Path.Combine(assets, "desktop_wallpaper.png"))) DesktopBackground = LoadDetachedImage(Path.Combine(assets, "desktop_wallpaper.png"));
                if (File.Exists(Path.Combine(assets, "desktop_full_original.png"))) DesktopFullBackground = LoadDetachedImage(Path.Combine(assets, "desktop_full_original.png"));
                RebuildDesktopPaintCache();
                if (File.Exists(Path.Combine(assets, "footer_original.png"))) TaskbarBackground = LoadDetachedImage(Path.Combine(assets, "footer_original.png"));
                foreach (string name in new[] { "icon_desktop_youtube.png", "icon_desktop_asobu.png", "icon_desktop_neru.png", "icon_desktop_okusuri.png", "icon_desktop_internet.png", "icon_desktop_odekake.png" })
                    if (File.Exists(Path.Combine(assets, name))) DesktopIcons[name] = LoadDetachedImage(Path.Combine(assets, name));
                string[] metricIconNames = { "icon_status_follower.png", "icon_status_stress.png", "icon_status_love.png", "icon_status_yami.png" };
                for (int i = 0; i < metricIconNames.Length; i++)
                {
                    string path = Path.Combine(assets, metricIconNames[i]);
                    if (File.Exists(path)) { Image icon = LoadDetachedImage(path); MetricIconImages.Add(icon); MetricIcons[i].Image = icon; }
                }
                Desktop.Invalidate(); Taskbar.Invalidate(); Rail.Invalidate(); RightRail.Invalidate();
                if (File.Exists(Path.Combine(assets, "window_active.png"))) { WindowFrame = LoadDetachedImage(Path.Combine(assets, "window_active.png")); CharacterCard.Invalidate(); StatsCard.Invalidate(); RecordsCard.Invalidate(); }
                if (File.Exists(Path.Combine(assets, "window_inactive.png"))) WindowInactiveFrame = LoadDetachedImage(Path.Combine(assets, "window_inactive.png"));
                if (File.Exists(Path.Combine(assets, "button_minimize.png"))) CardMinimizeImage = LoadDetachedImage(Path.Combine(assets, "button_minimize.png"));
                if (File.Exists(Path.Combine(assets, "button_maximize.png"))) CardMaximizeImage = LoadDetachedImage(Path.Combine(assets, "button_maximize.png"));
                if (File.Exists(Path.Combine(assets, "button_close.png"))) CardCloseImage = LoadDetachedImage(Path.Combine(assets, "button_close.png"));
                CharacterCard.Invalidate(); StatsCard.Invalidate(); RecordsCard.Invalidate();
                if (File.Exists(Path.Combine(assets, "jine_bg.png"))) { JineBackground = LoadDetachedImage(Path.Combine(assets, "jine_bg.png")); RecordsCard.Controls[0].BackgroundImage = JineBackground; }
                if (File.Exists(Path.Combine(assets, "haisin.png"))) LiveTemplate = LoadDetachedImage(Path.Combine(assets, "haisin.png"));
                if (File.Exists(Path.Combine(assets, "icon_jine_ame.png"))) RecordAvatar.Image = LoadDetachedImage(Path.Combine(assets, "icon_jine_ame.png"));
                if (File.Exists(Path.Combine(assets, "tail.png"))) RecordTail.Image = LoadDetachedImage(Path.Combine(assets, "tail.png"));
                if (File.Exists(Path.Combine(assets, "button_start.png"))) { StartBackground = LoadDetachedImage(Path.Combine(assets, "button_start.png")); StartButton.BackgroundImage = StartBackground; }
                if (File.Exists(Path.Combine(assets, "start_pressed.png"))) StartPressedBackground = LoadDetachedImage(Path.Combine(assets, "start_pressed.png"));
                if (!File.Exists(manifest)) return;
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest, Encoding.UTF8));
                foreach (var entry in data)
                {
                    bool kangel = entry.Key.StartsWith("stream_cho_", StringComparison.OrdinalIgnoreCase);
                    bool ame = entry.Key.StartsWith("stream_ame_idle_", StringComparison.OrdinalIgnoreCase);
                    if (!kangel && !ame) continue;
                    var clipData = entry.Value as Dictionary<string, object>; if (clipData == null) continue;
                    double duration = Convert.ToDouble(clipData["duration"]);
                    var frameList = clipData["frames"] as IEnumerable; if (frameList == null) continue;
                    var clip = new RoleClip { Name = entry.Key, Duration = duration };
                    foreach (object value in frameList)
                    {
                        var frameData = value as Dictionary<string, object>; if (frameData == null) continue;
                        string relative = Convert.ToString(frameData["src"]).Replace('/', Path.DirectorySeparatorChar);
                        string path = Path.GetFullPath(Path.Combine(BuildInfo.InstallDir, relative));
                        if (!path.StartsWith(Path.GetFullPath(assets).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                        Image image;
                        if (!FrameCache.TryGetValue(path, out image)) { image = LoadDetachedImage(path); FrameCache[path] = image; }
                        clip.Frames.Add(new RoleFrame { Time = Convert.ToDouble(frameData["time"]), Image = image });
                    }
                    if (clip.Frames.Count > 0) (kangel ? KangelClips : AmeClips).Add(clip);
                }
                SceneClips[RoleScene.Daily] = AmeClips.Where(x => x.Name.Contains("_idle_normal_") || x.Name.Contains("_idle_happy_")).ToList();
                SceneClips[RoleScene.HostOffline] = AmeClips.Where(x => x.Name.Contains("_idle_anxiety_") || x.Name.Contains("_idle_iraira_")).ToList();
                SceneClips[RoleScene.AfterGame] = KangelClips.Where(x => x.Name == "stream_cho_akaruku" || x.Name == "stream_cho_kawaiku" || x.Name == "stream_cho_gaoo").ToList();
                SceneClips[RoleScene.LiveGaming] = KangelClips.Where(x => x.Name == "stream_cho_game" || x.Name == "stream_cho_game_fever" || x.Name == "stream_cho_akaruku").ToList();
            }
            catch (Exception ex) { RoleAssetError = ex.ToString(); }
            CharacterCanvas.Invalidate();
        }

        private static Image LoadDetachedImage(string path)
        {
            using (var stream = new MemoryStream(File.ReadAllBytes(path))) using (var source = Image.FromStream(stream)) return new Bitmap(source);
        }

        private void StartTransformation()
        {
            Transforming = true;
            TransformationStarted = DateTime.UtcNow;
            LiveCard.Visible = false;
            TransitionOverlay.Visible = true;
            TransitionOverlay.BringToFront();
            TransitionOverlay.Invalidate();
        }

        private void FinishTransformation()
        {
            Transforming = false;
            TransitionOverlay.Visible = false;
            if (!PreviousGameRunning) return;
            LiveCard.Visible = true;
            LiveTab.Visible = true;
            FocusCard(LiveCard);
            LiveCanvas.Invalidate();
        }

        private void PaintTransformation(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(245, 223, 247));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            double elapsed = Math.Max(0, (DateTime.UtcNow - TransformationStarted).TotalSeconds);
            int width = Math.Min(Desktop.Width - 40, (int)(696 * Math.Min(2.0, Desktop.Width / 760.0)));
            int height = (int)(width * 454.0 / 696.0);
            var viewport = new Rectangle((Desktop.Width - width) / 2, Math.Max(20, (Desktop.Height - height) / 2 - 25), width, height);
            if (CharacterBackground != null) g.DrawImage(CharacterBackground, viewport);
            RoleClip clip = elapsed < 1.25
                ? AmeClips.FirstOrDefault(x => x.Name == "stream_ame_idle_normal_a")
                : KangelClips.FirstOrDefault(x => x.Name == "stream_cho_kawaiku");
            if (clip == null) clip = elapsed < 1.25 ? AmeClips.FirstOrDefault() : KangelClips.FirstOrDefault();
            Image frame = FrameAt(clip, elapsed < 1.25 ? elapsed : elapsed - 1.25);
            if (frame != null) g.DrawImage(frame, viewport);
            if (elapsed >= 1.16 && elapsed < 1.43)
                using (var flash = new SolidBrush(Color.FromArgb((int)(235 * (1 - Math.Abs(elapsed - 1.295) / 0.135)), Color.White))) g.FillRectangle(flash, Desktop.ClientRectangle);
            using (var pen = new Pen(Color.FromArgb(78, 56, 166), 3)) g.DrawRectangle(pen, viewport);
        }

        private void PaintLive(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var area = LiveCanvas.ClientRectangle;
            if (area.Width < 100 || area.Height < 100) return;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            if (LiveTemplate != null) g.DrawImage(LiveTemplate, area);
            else g.Clear(Color.FromArgb(255, 248, 255));
            double sx = area.Width / 1382.0, sy = area.Height / 890.0;
            Func<int, int, int, int, Rectangle> rect = (x, y, w, h) => new Rectangle((int)(x * sx), (int)(y * sy), Math.Max(1, (int)(w * sx)), Math.Max(1, (int)(h * sy)));
            var video = rect(60, 90, 696, 454);
            if (CharacterBackground != null) g.DrawImage(CharacterBackground, video);
            Image frame = CurrentFrame();
            if (frame != null) g.DrawImage(frame, video);
            using (var font = PixelText == null ? new Font("Microsoft YaHei UI", 12F) : new Font(PixelText.FontFamily, 18, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Color.FromArgb(69, 53, 153)))
            {
                g.DrawString("超天酱来啦！", font, ink, (int)(65 * sx), (int)(588 * sy));
                g.DrawString("主机正在运行：" + LiveGameName, LiveText ?? font, ink, (int)(65 * sx), (int)(633 * sy));
                g.DrawString("正在选择留言", LiveText ?? font, ink, (int)(900 * sx), (int)(104 * sy));
                string[] lines = { "正在记录游戏时长", "主机游戏：" + LiveGameName, "主机状态已连接", "超天酱陪你玩游戏！", "切回桌面可查看统计", "今天也一起加油！" };
                for (int i = 0; i < lines.Length; i++)
                {
                    using (var marker = new SolidBrush(Color.FromArgb(70, 106, 222)))
                        g.FillRectangle(marker, rect(790, 154 + i * 46, 13, 13));
                    g.DrawString(lines[i], font, ink, (int)(812 * sx), (int)((148 + i * 46) * sy));
                }
                g.DrawString("待选留言 ▶", font, ink, (int)(60 * sx), (int)(787 * sy));
            }
            using (var outline = new Pen(Color.FromArgb(214, 191, 226)))
            {
                g.DrawRectangle(outline, rect(448, 755, 356, 86));
                g.DrawRectangle(outline, rect(824, 755, 356, 86));
            }
        }

        private static Image FrameAt(RoleClip clip, double elapsed)
        {
            if (clip == null || clip.Frames.Count == 0 || clip.Duration <= 0) return null;
            double position = elapsed % clip.Duration;
            RoleFrame selected = clip.Frames[0];
            foreach (var frame in clip.Frames) { if (frame.Time > position) break; selected = frame; }
            return selected.Image;
        }

        private void SelectRoleClip(RoleScene scene)
        {
            CurrentScene = scene;
            IsKangel = scene == RoleScene.AfterGame || scene == RoleScene.LiveGaming;
            IsGameRunning = scene == RoleScene.LiveGaming;
            List<RoleClip> list;
            if (!SceneClips.TryGetValue(scene, out list) || list.Count == 0) list = IsKangel ? KangelClips : AmeClips;
            if (list.Count == 0) CurrentClip = null;
            else if (list.Count == 1) CurrentClip = list[0];
            else
            {
                var candidates = list.Where(x => x != CurrentClip).ToList();
                CurrentClip = candidates[Randomizer.Next(candidates.Count)];
            }
            ClipStarted = DateTime.UtcNow;
            DisplayedFrame = CurrentFrame();
            CharacterCanvas.Invalidate();
        }

        private void PaintCharacter(object sender, PaintEventArgs e)
        {
            var bounds = CharacterCanvas.ClientRectangle;
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            if (CharacterBackground != null) e.Graphics.DrawImage(CharacterBackground, bounds, new Rectangle(Point.Empty, CharacterBackground.Size), GraphicsUnit.Pixel);
            Image image = CurrentFrame();
            if (image != null && bounds.Width > 0 && bounds.Height > 0)
            {
                e.Graphics.DrawImage(image, bounds, new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
            }
            else
            {
                using (var font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold)) using (var brush = new SolidBrush(Color.FromArgb(90, 69, 146)))
                    e.Graphics.DrawString(RoleAssetError == null ? "尚未安装角色动画素材" : "角色动画读取失败：" + RoleAssetError.Split('\n')[0], font, brush, 28, 28);
            }
        }

        private Image CurrentFrame()
        {
            return FrameAt(CurrentClip, (DateTime.UtcNow - ClipStarted).TotalSeconds);
        }

        private sealed class RoleClip { public string Name; public double Duration; public readonly List<RoleFrame> Frames = new List<RoleFrame>(); }
        private enum RoleScene { Daily, HostOffline, AfterGame, LiveGaming }
        private sealed class RoleFrame { public double Time; public Image Image; }

        private sealed class BufferedPanel : Panel
        {
            public BufferedPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                UpdateStyles();
            }
        }

        private static string RecentSummary(GameSession[] sessions)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long from = now - TimeSpan.FromDays(7).Ticks / TimeSpan.TicksPerMillisecond;
            var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in sessions ?? new GameSession[0])
            {
                if (session == null || String.IsNullOrWhiteSpace(session.game)) continue;
                long start = Math.Max(from, session.start), end = Math.Min(now, session.end == 0 ? now : session.end);
                if (end <= start) continue;
                if (!totals.ContainsKey(session.game)) totals[session.game] = 0;
                totals[session.game] += end - start;
            }
            if (totals.Count == 0) return "最近 7 天没有游戏记录。";
            return String.Join("\r\n", totals.OrderByDescending(x => x.Value).Take(3).Select(x => "▣  " + x.Key + "  " + FormatDuration(x.Value)));
        }

        private void PaintChart(object sender, PaintEventArgs e)
        {
            var rect = Chart.ClientRectangle; if (rect.Width < 50 || rect.Height < 40) return;
            Color[] colors = { Color.FromArgb(74, 57, 171), Color.FromArgb(91, 174, 206), Color.FromArgb(202, 113, 179) };
            using (var font = PixelText == null ? new Font("Microsoft YaHei UI", 8F) : new Font(PixelText.FontFamily, 11, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                string[] labels = { "CPU", "内存", "GPU" };
                for (int i = 0; i < 3; i++) using (var brush = new SolidBrush(colors[i])) e.Graphics.DrawString(labels[i], font, brush, 8 + i * 52, 0);
                using (var brush = new SolidBrush(Color.FromArgb(100, 86, 159))) e.Graphics.DrawString("最近 60 秒", font, brush, Math.Max(164, rect.Width - 78), 0);
            }
            using (var grid = new Pen(Color.FromArgb(230, 218, 239)))
                for (int i = 1; i <= 3; i++) e.Graphics.DrawLine(grid, 8, 18 + i * (rect.Height - 24) / 4, rect.Width - 8, 18 + i * (rect.Height - 24) / 4);
            for (int series = 0; series < 3; series++)
            {
                using (var pen = new Pen(colors[series], 2))
                {
                    Point? previous = null;
                    for (int i = 0; i < Samples.Count; i++)
                    {
                        double value = Samples[i][series]; if (value < 0) { previous = null; continue; }
                        int x = 8 + i * (rect.Width - 16) / 59;
                        int y = rect.Height - 6 - (int)(Math.Max(0, Math.Min(100, value)) * (rect.Height - 24) / 100);
                        var point = new Point(x, y); if (previous.HasValue) e.Graphics.DrawLine(pen, previous.Value, point); previous = point;
                    }
                }
            }
        }

        private static string FormatDuration(long ms) { long mins = ms / 60000; return (mins / 60) + " 小时 " + (mins % 60) + " 分"; }

        private static string Temp(double value) { return value >= 0 ? Math.Round(value) + "°C" : "—"; }
        private void BeginDrag(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Dragging = true; DragOrigin = e.Location; } }
        private void ContinueDrag(object sender, MouseEventArgs e) { if (Dragging) { Location = new Point(Left + e.X - DragOrigin.X, Top + e.Y - DragOrigin.Y); } }
        private void EndDrag(object sender, MouseEventArgs e) { Dragging = false; }
    }
}

