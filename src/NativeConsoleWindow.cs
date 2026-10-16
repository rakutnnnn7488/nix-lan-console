using System;
using System.Drawing;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
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
        private readonly Panel[] MetricFills = new Panel[4];
        private readonly Label GameList;
        private readonly Panel Chart;
        private readonly Panel CharacterCanvas;
        private readonly Panel Desktop;
        private readonly Panel Rail;
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
        private Image WindowFrame;
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

        public NativeConsoleWindow()
        {
            Text = "副机控制台";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            BackColor = Color.FromArgb(244, 224, 248);
            Font = new Font("Microsoft YaHei UI", 10F);
            DoubleBuffered = true;

            var top = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.FromArgb(225, 203, 239) };
            var title = new Label { Text = "▣  NIX LAN CONSOLE  /  DESKTOP", Dock = DockStyle.Left, Width = 420, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), ForeColor = Color.FromArgb(59, 50, 145), Font = new Font("Consolas", 10F, FontStyle.Bold) };
            title.MouseDown += BeginDrag; title.MouseMove += ContinueDrag; title.MouseUp += EndDrag;
            var minimize = WindowButton("—"); minimize.Dock = DockStyle.Right; minimize.Click += delegate { Hide(); };
            var close = WindowButton("×"); close.Dock = DockStyle.Right; close.Click += delegate { Environment.Exit(0); };
            top.Controls.Add(close); top.Controls.Add(minimize); top.Controls.Add(title);
            Controls.Add(top);

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 237, 250) };
            Rail = new Panel { Dock = DockStyle.Left, Width = 242, BackColor = Color.FromArgb(235, 220, 245), Padding = new Padding(12), BackgroundImageLayout = ImageLayout.Stretch };
            var brand = new Label { Text = "AMENET\nDESKTOP", Dock = DockStyle.Top, Height = 78, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(69, 49, 150), Font = new Font("Consolas", 17F, FontStyle.Bold) };
            Rail.Controls.Add(brand);
            foreach (string name in new[] { "角色 / Webcam", "主机状态", "游戏记录", "设置" })
            {
                var button = new Button { Text = "▣  " + name, Dock = DockStyle.Top, Height = 47, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(62, 49, 137), BackColor = Color.FromArgb(242, 231, 248), Margin = new Padding(4) };
                button.FlatAppearance.BorderColor = Color.FromArgb(150, 128, 187); Rail.Controls.Add(button); button.BringToFront();
                string action = name;
                button.Click += delegate
                {
                    if (action == "游戏记录") { using (var dialog = new NativeRecordsDialog()) dialog.ShowDialog(this); }
                    else if (action == "设置") OpenSettings();
                    else if (action == "角色 / Webcam") CharacterCanvas.Focus();
                    else HostState.Focus();
                };
            }
            body.Controls.Add(Rail);

            Desktop = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 235, 250) };
            Desktop.Paint += PaintDesktop;
            CharacterCard = Card("WEBCAM  /  CHARACTER", 716);
            CharacterState = new Label { Text = "糖糖 · 日常待机 ♡", Dock = DockStyle.Bottom, Height = 44, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(64, 50, 147), Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) };
            CharacterCanvas = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(252, 244, 251) };
            CharacterCanvas.Paint += PaintCharacter;
            CharacterCard.Controls.Add(CharacterCanvas); CharacterCard.Controls.Add(CharacterState); Desktop.Controls.Add(CharacterCard);

            StatsCard = Card("HOST STATUS", 392);
            HostState = new Label { Text = "主机离线", Dock = DockStyle.Top, Height = 35, ForeColor = Color.FromArgb(82, 79, 153), Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold) }; StatsCard.Controls.Add(HostState);
            string[] metricNames = { "CPU", "MEMORY", "GPU", "TEMPERATURE" };
            for (int i = metricNames.Length - 1; i >= 0; i--)
            {
                var row = new Panel { Dock = DockStyle.Top, Height = 57, Padding = new Padding(0, 3, 0, 3) };
                MetricValues[i] = new Label { Text = "—", Dock = DockStyle.Right, Width = 94, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(59, 50, 145), Font = new Font("Consolas", 12F, FontStyle.Bold) };
                var name = new Label { Text = metricNames[i], Dock = DockStyle.Top, Height = 20, ForeColor = Color.FromArgb(100, 86, 159), Font = new Font("Consolas", 9F, FontStyle.Bold) };
                var track = new Panel { Dock = DockStyle.Bottom, Height = 17, BackColor = Color.FromArgb(222, 207, 234) };
                MetricFills[i] = new Panel { Dock = DockStyle.Left, Width = 0, BackColor = i == 1 ? Color.FromArgb(111, 178, 212) : Color.FromArgb(118, 102, 192) }; track.Controls.Add(MetricFills[i]);
                row.Controls.Add(track); row.Controls.Add(name); row.Controls.Add(MetricValues[i]); StatsCard.Controls.Add(row);
            }

            Chart = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Color.FromArgb(255, 248, 255), Margin = new Padding(0, 8, 0, 8) };
            Chart.Paint += PaintChart;
            StatsCard.Controls.Add(Chart); Desktop.Controls.Add(StatsCard);

            RecordsCard = Card("GAME RECORDS  /  LAST 7 DAYS", 615);
            GameList = new Label { Text = "等待主机游戏记录…", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(70, 59, 143), Font = new Font("Microsoft YaHei UI", 10F), Padding = new Padding(4, 10, 4, 4) };
            RecordsCard.Controls.Add(GameList); Desktop.Controls.Add(RecordsCard); body.Controls.Add(Desktop); Controls.Add(body);
            top.SendToBack(); Rail.SendToBack();
            Taskbar = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = Color.FromArgb(247, 224, 247), Padding = new Padding(4, 4, 4, 4) };
            Taskbar.Paint += delegate(object sender, PaintEventArgs e) { using (var pen = new Pen(Color.FromArgb(89, 62, 156), 2)) e.Graphics.DrawLine(pen, 0, 1, Taskbar.Width, 1); };
            StartButton = new Button { Text = "开始", Dock = DockStyle.Left, Width = 136, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(38, 31, 94), BackColor = Color.FromArgb(246, 222, 249), Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), BackgroundImageLayout = ImageLayout.Stretch };
            StartButton.FlatAppearance.BorderSize = 0;
            StartButton.Click += delegate { OpenSettings(); };
            StartButton.MouseDown += delegate { if (StartPressedBackground != null) StartButton.BackgroundImage = StartPressedBackground; };
            StartButton.MouseUp += delegate { StartButton.BackgroundImage = StartBackground; };
            Taskbar.Controls.Add(StartButton);
            foreach (string tabName in new[] { "▣  WEBCAM", "▣  HOST STATUS", "▣  GAME RECORDS" })
            {
                var tab = new Button { Text = tabName, Dock = DockStyle.Left, Width = 176, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(238, 211, 242), ForeColor = Color.FromArgb(65, 48, 145), Font = new Font("Consolas", 9F, FontStyle.Bold) };
                tab.FlatAppearance.BorderColor = Color.FromArgb(116, 91, 173);
                string selected = tabName;
                tab.Click += delegate { if (selected.Contains("GAME")) { using (var dialog = new NativeRecordsDialog()) dialog.ShowDialog(this); } else if (selected.Contains("HOST")) StatsCard.BringToFront(); else CharacterCard.BringToFront(); };
                Taskbar.Controls.Add(tab); tab.BringToFront();
            }
            TaskbarStatus = new Label { Text = "○ 主机离线", Dock = DockStyle.Right, Width = 230, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(73, 55, 147), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };
            Taskbar.Controls.Add(TaskbarStatus);
            Controls.Add(Taskbar); Taskbar.SendToBack();
            Desktop.Resize += delegate { LayoutDesktop(); };
            Resize += delegate { Rail.Width = Math.Max(180, (int)(ClientSize.Width * 242.0 / 1920)); Taskbar.Height = Math.Max(40, (int)(ClientSize.Height * 40.0 / 1080)); StartButton.Width = Math.Max(136, (int)(ClientSize.Width * 136.0 / 1920)); LayoutDesktop(); };
            Rail.Width = Math.Max(180, (int)(ClientSize.Width * 242.0 / 1920));
            Taskbar.Height = Math.Max(40, (int)(ClientSize.Height * 40.0 / 1080));
            StartButton.Width = Math.Max(136, (int)(ClientSize.Width * 136.0 / 1920));
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
                if (WindowFrame != null) WindowFrame.Dispose();
                if (StartBackground != null) StartBackground.Dispose();
                if (StartPressedBackground != null) StartPressedBackground.Dispose();
                foreach (var image in FrameCache.Values) image.Dispose();
            };
            RefreshData();
        }

        private static Button WindowButton(string text)
        {
            return new Button { Text = text, Width = 42, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(248, 238, 250), ForeColor = Color.FromArgb(65, 51, 142), Font = new Font("Consolas", 13F, FontStyle.Bold), Margin = new Padding(3) };
        }

        private void OpenSettings() { using (var dialog = new NativeSettingsDialog()) dialog.ShowDialog(this); }

        private Panel Card(string title, int width)
        {
            var card = new Panel { Width = width, BackColor = Color.FromArgb(255, 248, 255), Padding = new Padding(12, 42, 12, 12), Margin = new Padding(8) };
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (WindowFrame != null) DrawNineSlice(e.Graphics, WindowFrame, card.ClientRectangle);
                else using (var pen = new Pen(Color.FromArgb(119, 96, 177), 2)) e.Graphics.DrawRectangle(pen, 1, 1, card.ClientSize.Width - 3, card.ClientSize.Height - 3);
                using (var font = new Font("Consolas", 10F, FontStyle.Bold)) using (var brush = new SolidBrush(Color.FromArgb(70, 51, 150))) e.Graphics.DrawString("■ " + title, font, brush, 18, 11);
            };
            bool moving = false; Point origin = Point.Empty;
            card.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && e.Y < 40) { moving = true; origin = e.Location; card.BringToFront(); } };
            card.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!moving) return;
                card.Left = Math.Max(0, Math.Min(Desktop.Width - card.Width, card.Left + e.X - origin.X));
                card.Top = Math.Max(0, Math.Min(Desktop.Height - card.Height, card.Top + e.Y - origin.Y));
            };
            card.MouseUp += delegate { moving = false; };
            return card;
        }

        private static void DrawNineSlice(Graphics graphics, Image image, Rectangle target)
        {
            if (target.Width < 24 || target.Height < 70) return;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            int left = 8, right = 8, top = 42, bottom = 20;
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
            double scale = Math.Min(Desktop.Width / 1678.0, Desktop.Height / 1042.0);
            CharacterCard.Bounds = ScaleRect(174, 22, 716, 508, scale);
            StatsCard.Bounds = ScaleRect(1038, 0, 392, 353, scale);
            RecordsCard.Bounds = ScaleRect(815, 410, 615, 575, scale);
            Chart.Height = Math.Max(50, (int)(70 * scale));
        }
        private void PaintDesktop(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(250, 235, 250));
            using (var brush = new SolidBrush(Color.FromArgb(220, 230, 245)))
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
            TaskbarStatus.Text = state.online ? "● 主机在线" : "○ 主机离线";
            MetricValues[0].Text = state.online ? Math.Round(state.cpu) + "%" : "—";
            MetricValues[1].Text = state.online && state.memoryTotal > 0 ? Math.Round(state.memoryUsed / state.memoryTotal * 100) + "%" : "—";
            MetricValues[2].Text = state.online && state.gpu >= 0 ? Math.Round(state.gpu) + "%" : "—";
            MetricValues[3].Text = state.online ? "CPU " + Temp(state.cpuTemp) + " / GPU " + Temp(state.gpuTemp) : "—";
            double[] values = { state.cpu, state.online && state.memoryTotal > 0 ? state.memoryUsed / state.memoryTotal * 100 : -1, state.gpu, -1 };
            for (int i = 0; i < 3; i++) MetricFills[i].Width = (int)(MetricFills[i].Parent.ClientSize.Width * Math.Max(0, Math.Min(100, values[i])) / 100.0);
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
                if (File.Exists(Path.Combine(assets, "bg_stream_native.png"))) CharacterBackground = LoadDetachedImage(Path.Combine(assets, "bg_stream_native.png"));
                if (File.Exists(Path.Combine(assets, "sidebar_noon.png"))) { SideBackground = LoadDetachedImage(Path.Combine(assets, "sidebar_noon.png")); Rail.BackgroundImage = SideBackground; }
                if (File.Exists(Path.Combine(assets, "window_active.png"))) { WindowFrame = LoadDetachedImage(Path.Combine(assets, "window_active.png")); CharacterCard.Invalidate(); StatsCard.Invalidate(); RecordsCard.Invalidate(); }
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
            if (CharacterBackground != null) e.Graphics.DrawImage(CharacterBackground, bounds);
            Image image = CurrentFrame();
            if (image != null && bounds.Width > 0 && bounds.Height > 0)
            {
                double scale = Math.Min((bounds.Width * .88) / image.Width, (bounds.Height * .96) / image.Height);
                int width = Math.Max(1, (int)(image.Width * scale)), height = Math.Max(1, (int)(image.Height * scale));
                e.Graphics.DrawImage(image, (bounds.Width - width) / 2, bounds.Height - height, width, height);
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
            using (var grid = new Pen(Color.FromArgb(230, 218, 239)))
                for (int i = 1; i <= 3; i++) e.Graphics.DrawLine(grid, 8, i * (rect.Height - 12) / 4, rect.Width - 8, i * (rect.Height - 12) / 4);
            Color[] colors = { Color.FromArgb(74, 57, 171), Color.FromArgb(91, 174, 206), Color.FromArgb(202, 113, 179) };
            for (int series = 0; series < 3; series++)
            {
                using (var pen = new Pen(colors[series], 2))
                {
                    Point? previous = null;
                    for (int i = 0; i < Samples.Count; i++)
                    {
                        double value = Samples[i][series]; if (value < 0) { previous = null; continue; }
                        int x = 8 + i * (rect.Width - 16) / 59;
                        int y = rect.Height - 6 - (int)(Math.Max(0, Math.Min(100, value)) * (rect.Height - 20) / 100);
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
