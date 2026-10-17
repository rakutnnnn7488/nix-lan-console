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
        private readonly Panel Chart;
        private readonly Panel CharacterCanvas;
        private readonly Panel Desktop;
        private readonly Panel Rail;
        private readonly Panel RightRail;
        private readonly Panel Taskbar;
        private readonly Button StartButton;
        private readonly Label TaskbarStatus;
        private readonly Panel CharacterCard;
        private readonly Panel StatsCard;
        private readonly Panel RecordsCard;
        private readonly Timer RefreshTimer;
        private readonly Timer AnimationTimer;
        private readonly List<double[]> Samples = new List<double[]>();
        private readonly List<RoleClip> AmeClips = new List<RoleClip>();
        private readonly List<RoleClip> KangelClips = new List<RoleClip>();
        private readonly Dictionary<string, Image> FrameCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly Random Randomizer = new Random();
        private Image CharacterBackground;
        private Image SideBackground;
        private Image DesktopBackground;
        private readonly Dictionary<string, Image> DesktopIcons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Control> ShortcutControls = new List<Control>();
        private Image WindowFrame;
        private Image WindowInactiveFrame;
        private Image JineBackground;
        private Panel ActiveCard;
        private PrivateFontCollection PixelFonts;
        private Font PixelText;
        private Font PixelSmallText;
        private Font PixelTinyText;
        private Image StartBackground;
        private Image StartPressedBackground;
        private RoleClip CurrentClip;
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
            var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = new Padding(0), Padding = new Padding(0) };
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            metrics.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            for (int i = 0; i < 4; i++) metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 18));
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
            HostState = new Label { Text = "主机离线", Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.FromArgb(82, 79, 153), Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) }; metrics.Controls.Add(HostState, 0, 0);
            string[] metricNames = { "CPU", "内存", "GPU", "温度" };
            for (int i = 0; i < metricNames.Length; i++)
            {
                int rowIndex = i;
                var row = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 3) };
                MetricIcons[i] = new PictureBox { Location = new Point(0, 2), Size = new Size(42, 36), SizeMode = PictureBoxSizeMode.Zoom };
                MetricValues[i] = new Label { Text = "—", Location = new Point(51, 18), Size = new Size(155, 25), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(59, 50, 145), Font = new Font("Consolas", 13F, FontStyle.Bold) };
                var name = new Label { Text = metricNames[i], Location = new Point(51, 0), Size = new Size(160, 19), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(97, 165, 192), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };
                MetricNames[i] = name;
                var track = new Panel { Height = 22, BackColor = Color.FromArgb(214, 182, 218) };
                MetricFills[i] = new Panel { Dock = DockStyle.Left, Width = 0, BackColor = i == 1 ? Color.FromArgb(111, 178, 212) : Color.FromArgb(118, 102, 192) }; track.Controls.Add(MetricFills[i]);
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
                    MetricValues[rowIndex].Location = new Point(compact ? 39 : 51, compact ? 11 : 18);
                    MetricValues[rowIndex].Height = compact ? 13 : 24;
                    MetricValues[rowIndex].Width = Math.Max(52, track.Left - MetricValues[rowIndex].Left);
                    if (PixelSmallText != null)
                    {
                        MetricValues[rowIndex].Font = compact ? PixelSmallText : PixelText;
                        name.Font = compact ? PixelTinyText : PixelSmallText;
                    }
                };
                metrics.Controls.Add(row, 0, i + 1);
            }

            Chart = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(255, 248, 255), Margin = new Padding(0, 4, 0, 0) };
            Chart.Paint += PaintChart;
            metrics.Controls.Add(Chart, 0, 5); StatsCard.Controls.Add(metrics); Desktop.Controls.Add(StatsCard);

            RecordsCard = Card("游戏记录", 615);
            var jine = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(228, 155, 233), BackgroundImageLayout = ImageLayout.Stretch };
            var dayBadge = new Label { Text = "主机游戏日志  ♡  最近 7 天", TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(206, 210, 239), ForeColor = Color.FromArgb(63, 46, 144), Height = 35, Dock = DockStyle.Top };
            GameList = new Label { Text = "等待主机游戏记录…", AutoEllipsis = true, ForeColor = Color.FromArgb(70, 59, 143), BackColor = Color.FromArgb(218, 237, 255), Font = new Font("Microsoft YaHei UI", 10F), Padding = new Padding(14), Cursor = Cursors.Hand };
            GameList.Click += delegate { using (var dialog = new NativeRecordsDialog()) dialog.ShowDialog(this); };
            jine.Controls.Add(GameList); jine.Controls.Add(dayBadge);
            jine.Resize += delegate { GameList.Bounds = new Rectangle(48, 92, Math.Max(80, jine.Width - 96), Math.Min(250, Math.Max(75, jine.Height - 118))); };
            RecordsCard.Controls.Add(jine); Desktop.Controls.Add(RecordsCard); center.Controls.Add(Desktop); body.Controls.Add(center); Controls.Add(body);
            FocusCard(RecordsCard);
            Rail.SendToBack(); RightRail.SendToBack();
            Taskbar = new BufferedPanel { Height = 42, BackColor = Color.FromArgb(247, 224, 247), Padding = new Padding(4, 4, 4, 4) };
            Taskbar.Paint += delegate(object sender, PaintEventArgs e) { using (var pen = new Pen(Color.FromArgb(89, 62, 156), 2)) e.Graphics.DrawLine(pen, 0, 1, Taskbar.Width, 1); };
            StartButton = new Button { Text = "开始", Dock = DockStyle.Left, Width = 136, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(38, 31, 94), BackColor = Color.FromArgb(246, 222, 249), Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), BackgroundImageLayout = ImageLayout.Stretch };
            StartButton.FlatAppearance.BorderSize = 0;
            StartButton.Click += delegate { OpenSettings(); };
            StartButton.MouseDown += delegate { if (StartPressedBackground != null) StartButton.BackgroundImage = StartPressedBackground; };
            StartButton.MouseUp += delegate { StartButton.BackgroundImage = StartBackground; };
            Taskbar.Controls.Add(StartButton);
            foreach (string tabName in new[] { "▣  任务管理器", "▣  Webcam", "▣  JINE / 游戏记录" })
            {
                var tab = new Button { Text = tabName, Dock = DockStyle.Left, Width = 176, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(238, 211, 242), ForeColor = Color.FromArgb(65, 48, 145), Font = new Font("Consolas", 9F, FontStyle.Bold) };
                tab.FlatAppearance.BorderColor = Color.FromArgb(116, 91, 173);
                string selected = tabName;
                tab.Click += delegate { if (selected.Contains("JINE")) FocusCard(RecordsCard); else if (selected.Contains("任务")) FocusCard(StatsCard); else FocusCard(CharacterCard); };
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
                if (CurrentClip != null && (DateTime.UtcNow - ClipStarted).TotalSeconds >= CurrentClip.Duration) SelectRoleClip(IsKangel, IsGameRunning);
                Image frame = CurrentFrame();
                if (!Object.ReferenceEquals(frame, DisplayedFrame)) { DisplayedFrame = frame; CharacterCanvas.Invalidate(); }
            };
            AnimationTimer.Start();
            FormClosed += delegate
            {
                RefreshTimer.Stop(); AnimationTimer.Stop();
                if (CharacterBackground != null) CharacterBackground.Dispose();
                if (SideBackground != null) SideBackground.Dispose();
                if (DesktopBackground != null) DesktopBackground.Dispose();
                foreach (var image in DesktopIcons.Values) image.Dispose();
                if (WindowFrame != null) WindowFrame.Dispose();
                if (WindowInactiveFrame != null) WindowInactiveFrame.Dispose();
                if (JineBackground != null) JineBackground.Dispose();
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
                e.Graphics.DrawImage(SideBackground, Rail.ClientRectangle, new Rectangle(Point.Empty, SideBackground.Size), GraphicsUnit.Pixel);
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
            e.Graphics.DrawImage(SideBackground, new Rectangle(0, 0, RightRail.Width, RightRail.Height), new Rectangle(Point.Empty, SideBackground.Size), GraphicsUnit.Pixel);
            e.Graphics.DrawImage(SideBackground, new Rectangle(2, 0, RightRail.Width, RightRail.Height), new Rectangle(Point.Empty, SideBackground.Size), GraphicsUnit.Pixel);
            e.Graphics.ResetTransform();
        }

        private Panel Card(string title, int width)
        {
            var card = new BufferedPanel { Name = title, Width = width, BackColor = Color.FromArgb(255, 248, 255), Padding = new Padding(10, 42, 10, 12), Margin = new Padding(8) };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                Image frame = card == ActiveCard || WindowInactiveFrame == null ? WindowFrame : WindowInactiveFrame;
                if (frame != null) DrawNineSlice(e.Graphics, frame, card.ClientRectangle);
                else using (var pen = new Pen(Color.FromArgb(119, 96, 177), 2)) e.Graphics.DrawRectangle(pen, 1, 1, card.ClientSize.Width - 3, card.ClientSize.Height - 3);
                using (var font = PixelText == null ? new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) : new Font(PixelText.FontFamily, 17, FontStyle.Regular, GraphicsUnit.Pixel)) using (var brush = new SolidBrush(Color.FromArgb(70, 51, 150))) e.Graphics.DrawString("■ " + title, font, brush, 18, 11);
                using (var pen = new Pen(Color.FromArgb(67, 46, 155), 2))
                {
                    for (int button = 0; button < 3; button++) e.Graphics.DrawRectangle(pen, card.Width - 76 + button * 22, 12, 17, 17);
                }
                using (var font = new Font("Consolas", 9F, FontStyle.Bold)) using (var brush = new SolidBrush(Color.FromArgb(67, 46, 155)))
                {
                    e.Graphics.DrawString("_", font, brush, card.Width - 73, 10);
                    e.Graphics.DrawString("□", font, brush, card.Width - 51, 10);
                    e.Graphics.DrawString("×", font, brush, card.Width - 29, 10);
                }
            };
            bool moving = false; Point origin = Point.Empty;
            card.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || e.Y >= 40) return;
                if (e.X >= card.Width - 80)
                {
                    int command = (e.X - (card.Width - 80)) / 22;
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
                card.Left = Math.Max(0, Math.Min(Desktop.Width - card.Width, card.Left + e.X - origin.X));
                card.Top = Math.Max(0, Math.Min(Desktop.Height - card.Height, card.Top + e.Y - origin.Y));
            };
            card.MouseUp += delegate { if (moving) { moving = false; card.Capture = false; RememberPosition(card); SaveLayout(); } };
            return card;
        }

        private static void DrawNineSlice(Graphics graphics, Image image, Rectangle target)
        {
            if (target.Width < 24 || target.Height < 70) return;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            int left = 69, right = 12, top = 42, bottom = 22;
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
            StatsCard.Bounds = ScaleRect(1038, 22, 392, 353, scale);
            RecordsCard.Bounds = ScaleRect(815, 447, 615, 575, scale);
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
            e.Graphics.Clear(Color.FromArgb(250, 235, 250));
            if (DesktopBackground != null)
            {
                using (var brush = new TextureBrush(DesktopBackground, System.Drawing.Drawing2D.WrapMode.Tile))
                {
                    brush.TranslateTransform(26, 24);
                    e.Graphics.FillRectangle(brush, Desktop.ClientRectangle);
                }
            }
            else using (var brush = new SolidBrush(Color.FromArgb(220, 230, 245)))
                for (int y = 12; y < Desktop.Height; y += 32)
                    for (int x = 12 + (y / 32 % 2) * 16; x < Desktop.Width; x += 32)
                        e.Graphics.FillRectangle(brush, x, y, 7, 7);
        }
        private static Rectangle ScaleRect(int x, int y, int width, int height, double scale)
        {
            return new Rectangle((int)(x * scale), (int)(y * scale), (int)(width * scale), (int)(height * scale));
        }

        private void RefreshData()
        {
            DashboardState state = DisplayServer.Snapshot();
            HostState.Text = state.online ? "● 主机在线  /  " + state.host : "○ 主机离线";
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
            if (kangel != IsKangel || playing != IsGameRunning || CurrentClip == null) SelectRoleClip(kangel, playing);
            GameList.Text = RecentSummary(state.sessions);
        }

        private void LoadRoleAssets()
        {
            try
            {
                string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game-assets");
                string manifest = Path.Combine(assets, "native-role", "角色动画.json");
                if (File.Exists(Path.Combine(assets, "zpix.ttf")))
                {
                    PixelFonts = new PrivateFontCollection();
                    PixelFonts.AddFontFile(Path.Combine(assets, "zpix.ttf"));
                    PixelText = new Font(PixelFonts.Families[0], 16, FontStyle.Regular, GraphicsUnit.Pixel);
                    PixelSmallText = new Font(PixelFonts.Families[0], 11, FontStyle.Regular, GraphicsUnit.Pixel);
                    PixelTinyText = new Font(PixelFonts.Families[0], 10, FontStyle.Regular, GraphicsUnit.Pixel);
                    HostState.Font = PixelText; GameList.Font = PixelText; TaskbarStatus.Font = PixelText;
                    foreach (var value in MetricValues) value.Font = value.Parent.Height < 40 ? PixelSmallText : PixelText;
                    foreach (var name in MetricNames) name.Font = name.Parent.Height < 40 ? PixelTinyText : PixelSmallText;
                    foreach (Control control in Taskbar.Controls) if (control is Button) control.Font = PixelText;
                }
                if (File.Exists(Path.Combine(assets, "bg_stream_native.png"))) CharacterBackground = LoadDetachedImage(Path.Combine(assets, "bg_stream_native.png"));
                if (File.Exists(Path.Combine(assets, "sidebar_noon.png"))) SideBackground = LoadDetachedImage(Path.Combine(assets, "sidebar_noon.png"));
                if (File.Exists(Path.Combine(assets, "desktop_wallpaper.png"))) DesktopBackground = LoadDetachedImage(Path.Combine(assets, "desktop_wallpaper.png"));
                foreach (string name in new[] { "icon_desktop_youtube.png", "icon_desktop_asobu.png", "icon_desktop_neru.png", "icon_desktop_okusuri.png", "icon_desktop_internet.png", "icon_desktop_odekake.png" })
                    if (File.Exists(Path.Combine(assets, name))) DesktopIcons[name] = LoadDetachedImage(Path.Combine(assets, name));
                string[] metricIconNames = { "icon_status_follower.png", "icon_status_stress.png", "icon_status_love.png", "icon_status_yami.png" };
                for (int i = 0; i < metricIconNames.Length; i++)
                {
                    string path = Path.Combine(assets, metricIconNames[i]);
                    if (File.Exists(path)) { Image icon = LoadDetachedImage(path); MetricIconImages.Add(icon); MetricIcons[i].Image = icon; }
                }
                Desktop.Invalidate(); Rail.Invalidate(); RightRail.Invalidate();
                if (File.Exists(Path.Combine(assets, "window_active.png"))) { WindowFrame = LoadDetachedImage(Path.Combine(assets, "window_active.png")); CharacterCard.Invalidate(); StatsCard.Invalidate(); RecordsCard.Invalidate(); }
                if (File.Exists(Path.Combine(assets, "window_inactive.png"))) WindowInactiveFrame = LoadDetachedImage(Path.Combine(assets, "window_inactive.png"));
                if (File.Exists(Path.Combine(assets, "jine_bg.png"))) { JineBackground = LoadDetachedImage(Path.Combine(assets, "jine_bg.png")); RecordsCard.Controls[0].BackgroundImage = JineBackground; }
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
                        string path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relative));
                        if (!path.StartsWith(Path.GetFullPath(assets).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                        Image image;
                        if (!FrameCache.TryGetValue(path, out image)) { image = LoadDetachedImage(path); FrameCache[path] = image; }
                        clip.Frames.Add(new RoleFrame { Time = Convert.ToDouble(frameData["time"]), Image = image });
                    }
                    if (clip.Frames.Count > 0) (kangel ? KangelClips : AmeClips).Add(clip);
                }
            }
            catch (Exception ex) { RoleAssetError = ex.ToString(); }
            CharacterCanvas.Invalidate();
        }

        private static Image LoadDetachedImage(string path)
        {
            using (var stream = new MemoryStream(File.ReadAllBytes(path))) using (var source = Image.FromStream(stream)) return new Bitmap(source);
        }

        private void SelectRoleClip(bool kangel, bool gameRunning)
        {
            IsKangel = kangel;
            IsGameRunning = gameRunning;
            var list = kangel && !gameRunning ? KangelClips.Where(x => x.Name != "stream_cho_game").ToList() : kangel ? KangelClips : AmeClips;
            CurrentClip = list.Count == 0 ? null : list[Randomizer.Next(list.Count)];
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
            if (CurrentClip == null || CurrentClip.Frames.Count == 0) return null;
            double elapsed = (DateTime.UtcNow - ClipStarted).TotalSeconds % CurrentClip.Duration;
            RoleFrame selected = CurrentClip.Frames[0];
            foreach (var frame in CurrentClip.Frames) { if (frame.Time > elapsed) break; selected = frame; }
            return selected.Image;
        }

        private sealed class RoleClip { public string Name; public double Duration; public readonly List<RoleFrame> Frames = new List<RoleFrame>(); }
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
            return String.Join("\r\n\r\n", totals.OrderByDescending(x => x.Value).Take(12).Select(x => "▣  " + x.Key + "\r\n     " + FormatDuration(x.Value)));
        }

        private void PaintChart(object sender, PaintEventArgs e)
        {
            var rect = Chart.ClientRectangle; if (rect.Width < 50 || rect.Height < 40) return;
            Color[] colors = { Color.FromArgb(74, 57, 171), Color.FromArgb(91, 174, 206), Color.FromArgb(202, 113, 179) };
            using (var font = new Font("Microsoft YaHei UI", 8F))
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
