using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NixLanConsole
{
    internal class NativeDialog : Form
    {
        protected static readonly Color Ink = Color.FromArgb(62, 48, 140);
        protected NativeDialog(string title, int width, int height)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(width, height);
            MinimumSize = new Size(Math.Min(width, 700), Math.Min(height, 500));
            BackColor = Color.FromArgb(250, 240, 252);
            ForeColor = Ink;
            Font = new Font("Microsoft YaHei UI", 10F);
        }
        protected static Button MakeButton(string text)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 34), BackColor = Color.FromArgb(229, 207, 240), ForeColor = Ink, FlatStyle = FlatStyle.Flat };
            button.FlatAppearance.BorderColor = Color.FromArgb(102, 80, 163);
            return button;
        }
        protected static DataGridView MakeGrid()
        {
            return new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.FromArgb(255, 249, 255), BorderStyle = BorderStyle.FixedSingle };
        }
    }

    internal sealed class NativeRecordsDialog : NativeDialog
    {
        private readonly PrivateFontCollection PixelFonts = new PrivateFontCollection();
        private Font PixelFont;
        private readonly ComboBox Range;
        private readonly DateTimePicker From;
        private readonly DateTimePicker To;
        private readonly DataGridView Totals;
        private readonly DataGridView Sessions;
        private readonly Label Summary;
        private DashboardState State;

        public NativeRecordsDialog() : base("游戏运行记录", 940, 680)
        {
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(4);
            BackColor = Color.FromArgb(64, 48, 150);
            string fontPath = System.IO.Path.Combine(BuildInfo.InstallDir, "game-assets", "zpix.ttf");
            if (System.IO.File.Exists(fontPath))
            {
                try { PixelFonts.AddFontFile(fontPath); PixelFont = new Font(PixelFonts.Families[0], 16, FontStyle.Regular, GraphicsUnit.Pixel); Font = PixelFont; }
                catch { PixelFont = null; }
            }
            var title = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(226, 186, 233) };
            var titleText = new Label { Text = "■ 游戏记录  /  GAME LOG", Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink };
            var close = MakeButton("×"); close.AutoSize = false; close.Size = new Size(28, 26); close.Dock = DockStyle.Right;
            close.Click += delegate { Close(); };
            title.Controls.Add(titleText); title.Controls.Add(close);
            Point dragPoint = Point.Empty;
            titleText.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) dragPoint = e.Location; };
            titleText.MouseMove += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Location = new Point(Left + e.X - dragPoint.X, Top + e.Y - dragPoint.Y); };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 57, Padding = new Padding(12, 9, 0, 0), BackColor = Color.FromArgb(248, 235, 250) };
            top.Controls.Add(new Label { Text = "统计时间：", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
            Range = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
            Range.Items.AddRange(new object[] { "今天", "最近 7 天", "最近 30 天", "全部", "自定义" }); Range.SelectedIndex = 1;
            Range.SelectedIndexChanged += delegate { ToggleRange(); RefreshRecords(); };
            top.Controls.Add(Range);
            From = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120, Visible = false, Value = DateTime.Today.AddDays(-7) };
            To = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120, Visible = false, Value = DateTime.Today };
            From.ValueChanged += delegate { RefreshRecords(); }; To.ValueChanged += delegate { RefreshRecords(); };
            top.Controls.Add(From); top.Controls.Add(To);
            var reload = MakeButton("刷新"); reload.Click += delegate { RefreshRecords(); }; top.Controls.Add(reload);
            Controls.Add(top);

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 7), Appearance = TabAppearance.FlatButtons };
            Totals = MakeGrid(); Totals.Columns.Add("game", "游戏"); Totals.Columns.Add("duration", "时长");
            Sessions = MakeGrid(); Sessions.Columns.Add("game", "游戏"); Sessions.Columns.Add("start", "开始"); Sessions.Columns.Add("end", "结束"); Sessions.Columns.Add("duration", "时长");
            StyleGrid(Totals); StyleGrid(Sessions);
            var totalsTab = new TabPage("游戏汇总") { BackColor = Color.FromArgb(248, 235, 250) }; totalsTab.Controls.Add(Totals);
            var sessionsTab = new TabPage("逐次记录") { BackColor = Color.FromArgb(248, 235, 250) }; sessionsTab.Controls.Add(Sessions);
            tabs.TabPages.Add(totalsTab); tabs.TabPages.Add(sessionsTab); Controls.Add(tabs);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(12, 6, 0, 0), FlowDirection = FlowDirection.LeftToRight, BackColor = Color.FromArgb(248, 235, 250) };
            Summary = new Label { Text = "", Width = 360, Height = 55, TextAlign = ContentAlignment.MiddleLeft };
            var edit = MakeButton("修改选中记录"); edit.Click += delegate { EditSelected(); };
            var recap = MakeButton("查看本次回顾"); recap.Click += delegate { ShowSelectedRecap(); };
            var remove = MakeButton("删除选中记录"); remove.Click += delegate { DeleteSelected(); };
            bottom.Controls.Add(Summary); bottom.Controls.Add(recap); bottom.Controls.Add(edit); bottom.Controls.Add(remove); Controls.Add(bottom);
            var content = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 235, 250) };
            title.Dock = top.Dock = tabs.Dock = bottom.Dock = DockStyle.None;
            content.Controls.Add(title); content.Controls.Add(top); content.Controls.Add(tabs); content.Controls.Add(bottom);
            Action arrange = delegate
            {
                int width = content.ClientSize.Width, height = content.ClientSize.Height;
                title.Bounds = new Rectangle(0, 0, width, 34);
                top.Bounds = new Rectangle(0, 34, width, 57);
                bottom.Bounds = new Rectangle(0, Math.Max(91, height - 84), width, 84);
                tabs.Bounds = new Rectangle(0, 91, width, Math.Max(0, height - 175));
            };
            content.Resize += delegate { arrange(); };
            Controls.Add(content); arrange();
            FormClosed += delegate { if (PixelFont != null) PixelFont.Dispose(); PixelFonts.Dispose(); };
            RefreshRecords();
        }

        private void StyleGrid(DataGridView grid)
        {
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Color.FromArgb(248, 235, 250);
            grid.GridColor = Color.FromArgb(190, 153, 216);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 36;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(208, 191, 235);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.BackColor = Color.FromArgb(251, 242, 251);
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(165, 205, 234);
            grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(238, 221, 246);
            grid.RowTemplate.Height = 32;
        }

        private void ToggleRange() { bool custom = Range.SelectedIndex == 4; From.Visible = custom; To.Visible = custom; }
        private void TimeBounds(out long start, out long end)
        {
            DateTime now = DateTime.Now;
            DateTime first = Range.SelectedIndex == 0 ? now.Date : Range.SelectedIndex == 1 ? now.AddDays(-7) : Range.SelectedIndex == 2 ? now.AddDays(-30) : Range.SelectedIndex == 4 ? From.Value.Date : DateTime.MinValue;
            DateTime last = Range.SelectedIndex == 4 ? To.Value.Date.AddDays(1) : now;
            start = first == DateTime.MinValue ? 0 : new DateTimeOffset(first).ToUnixTimeMilliseconds();
            end = Math.Min(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), new DateTimeOffset(last).ToUnixTimeMilliseconds());
        }
        private void RefreshRecords()
        {
            State = DisplayServer.Snapshot();
            long begin, finish; TimeBounds(out begin, out finish);
            var excluded = new HashSet<string>(State.excludedGames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Totals.Rows.Clear(); Sessions.Rows.Clear();
            foreach (var session in State.sessions ?? new GameSession[0])
            {
                if (session == null || String.IsNullOrWhiteSpace(session.game) || excluded.Contains(session.game)) continue;
                long from = Math.Max(begin, session.start), to = Math.Min(finish, session.end == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : session.end);
                if (to <= from) continue;
                if (!totals.ContainsKey(session.game)) totals[session.game] = 0;
                totals[session.game] += to - from;
                int row = Sessions.Rows.Add(session.game, TimeText(session.start), session.end == 0 ? "运行中" : TimeText(session.end), Duration(to - from));
                Sessions.Rows[row].Tag = session;
            }
            foreach (var item in totals.OrderByDescending(x => x.Value)) Totals.Rows.Add(item.Key, Duration(item.Value));
            Summary.Text = "游戏 " + totals.Count + " 个  ·  合计 " + Duration(totals.Values.Sum());
        }
        private GameSession SelectedSession() { return Sessions.SelectedRows.Count == 0 ? null : Sessions.SelectedRows[0].Tag as GameSession; }
        private void ShowSelectedRecap()
        {
            var session = SelectedSession();
            if (session == null || session.end <= session.start) { MessageBox.Show(this, "请在逐次记录中选择已结束的游戏。", "游戏回顾"); return; }
            using (var dialog = new NativeSessionRecapDialog(session)) dialog.ShowDialog(this);
        }
        private void EditSelected()
        {
            var session = SelectedSession(); if (session == null) { MessageBox.Show(this, "请切换到逐次记录并选中一条。", "游戏记录"); return; }
            if (session.end == 0) { MessageBox.Show(this, "游戏运行中，请退出后再修改记录。", "游戏记录"); return; }
            using (var dialog = new NativeSessionEditDialog(session)) if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                try { DisplayServer.EditNativeSession(session.id, dialog.GameName, new DateTimeOffset(dialog.EndTime).ToUnixTimeMilliseconds(), false); RefreshRecords(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void DeleteSelected()
        {
            var session = SelectedSession(); if (session == null) { MessageBox.Show(this, "请切换到逐次记录并选中一条。", "游戏记录"); return; }
            if (MessageBox.Show(this, "删除这条游戏记录？删除前会自动备份记录文件。", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { DisplayServer.EditNativeSession(session.id, null, 0, true); RefreshRecords(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private static string TimeText(long value) { return DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm"); }
        private static string Duration(long value) { long minutes = Math.Max(0, value / 60000); return (minutes / 60) + " 小时 " + (minutes % 60) + " 分"; }
    }

    internal sealed class NativeSessionRecapDialog : NativeDialog
    {
        private readonly GameSession Session;
        private readonly Panel Curves;
        private readonly string[] Names = { "CPU 使用率", "GPU 使用率", "CPU 温度", "GPU 温度" };
        private readonly Color[] Colors = { Color.FromArgb(76, 58, 170), Color.FromArgb(87, 168, 211), Color.FromArgb(207, 109, 176), Color.FromArgb(112, 180, 154) };

        internal NativeSessionRecapDialog(GameSession session) : base("本次游戏回顾", 820, 620)
        {
            Session = session;
            var title = new Label { Dock = DockStyle.Top, Height = 62, Text = "♡  " + session.game + "  ·  游玩回顾", Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold), ForeColor = Ink, Padding = new Padding(20, 12, 0, 0) };
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, session.end - session.start));
            var summary = new Label { Dock = DockStyle.Top, Height = 64, Text =
                DateTimeOffset.FromUnixTimeMilliseconds(session.start).LocalDateTime.ToString("yyyy-MM-dd HH:mm") + "  →  " +
                DateTimeOffset.FromUnixTimeMilliseconds(session.end).LocalDateTime.ToString("HH:mm") +
                "    游玩 " + (int)duration.TotalHours + " 小时 " + duration.Minutes + " 分钟", Padding = new Padding(24, 10, 0, 0), ForeColor = Ink };
            Curves = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(255, 249, 255), Padding = new Padding(16) };
            Curves.Paint += PaintCurves;
            var close = MakeButton("关闭"); close.Dock = DockStyle.Bottom; close.Height = 42; close.Click += delegate { Close(); };
            var history = MakeButton("查看历史游戏记录"); history.Dock = DockStyle.Bottom; history.Height = 42;
            history.Click += delegate { using (var dialog = new NativeRecordsDialog()) dialog.ShowDialog(this); };
            Controls.Add(Curves); Controls.Add(close); Controls.Add(history); Controls.Add(summary); Controls.Add(title);
        }

        private void PaintCurves(object sender, PaintEventArgs e)
        {
            var samples = (Session.samples ?? new GameMetricSample[0]).Where(s => s != null).OrderBy(s => s.utc).ToArray();
            if (samples.Length == 0)
            {
                using (var brush = new SolidBrush(Ink)) e.Graphics.DrawString("这条记录没有硬件采样。更新前的历史游戏仅显示时长。", Font, brush, 24, 28);
                return;
            }
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int left = 145, right = Curves.Width - 28, top = 12;
            int rowHeight = Math.Max(78, (Curves.Height - 35) / 4);
            for (int metric = 0; metric < 4; metric++)
            {
                int y0 = top + metric * rowHeight, bottom = y0 + rowHeight - 24;
                if (bottom <= y0 || right <= left) continue;
                Func<GameMetricSample, double> value = metric == 0 ? (Func<GameMetricSample, double>)(s => s.cpu) : metric == 1 ? (Func<GameMetricSample, double>)(s => s.gpu) : metric == 2 ? (Func<GameMetricSample, double>)(s => s.cpuTemp) : (Func<GameMetricSample, double>)(s => s.gpuTemp);
                var valid = samples.Where(s => value(s) >= 0).ToArray();
                double maximum = valid.Length == 0 ? 100 : Math.Max(metric < 2 ? 100 : 90, valid.Max(value) + 5);
                string numbers = valid.Length == 0 ? "暂无读数" : "均值 " + Math.Round(valid.Average(value)) + (metric < 2 ? "%" : "°C") + "  /  峰值 " + Math.Round(valid.Max(value)) + (metric < 2 ? "%" : "°C");
                using (var ink = new SolidBrush(Ink))
                {
                    e.Graphics.DrawString(Names[metric], Font, ink, 18, y0 + 7);
                    e.Graphics.DrawString(numbers, Font, ink, 18, y0 + 31);
                }
                using (var grid = new Pen(Color.FromArgb(223, 207, 234)))
                {
                    e.Graphics.DrawLine(grid, left, bottom, right, bottom);
                    e.Graphics.DrawLine(grid, left, y0 + 5, right, y0 + 5);
                }
                if (valid.Length < 2) continue;
                long span = Math.Max(1, Session.end - Session.start);
                using (var pen = new Pen(Colors[metric], 2.5F))
                {
                    Point? previous = null;
                    foreach (var sample in samples)
                    {
                        double v = value(sample);
                        if (v < 0) { previous = null; continue; }
                        int x = left + (int)((right - left) * Math.Max(0, Math.Min(1, (sample.utc - Session.start) / (double)span)));
                        int y = bottom - (int)((bottom - y0 - 5) * Math.Max(0, Math.Min(1, v / maximum)));
                        var point = new Point(x, y);
                        if (previous.HasValue) e.Graphics.DrawLine(pen, previous.Value, point);
                        previous = point;
                    }
                }
            }
        }
    }

    internal sealed class NativeSessionEditDialog : NativeDialog
    {
        private readonly TextBox Game;
        private readonly DateTimePicker End;
        public string GameName { get { return Game.Text.Trim(); } }
        public DateTime EndTime { get { return End.Value; } }
        public NativeSessionEditDialog(GameSession session) : base("修改游戏记录", 470, 230)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(18) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = "游戏名称", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            Game = new TextBox { Text = session.game, Dock = DockStyle.Fill }; layout.Controls.Add(Game, 1, 0);
            layout.Controls.Add(new Label { Text = "结束时间", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            End = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", Dock = DockStyle.Fill, Value = DateTimeOffset.FromUnixTimeMilliseconds(session.end).LocalDateTime }; layout.Controls.Add(End, 1, 1);
            var save = MakeButton("保存"); save.DialogResult = DialogResult.OK; layout.Controls.Add(save, 1, 2); AcceptButton = save;
            Controls.Add(layout);
        }
    }

    internal sealed class NativeGameReviewDialog : NativeDialog
    {
        private readonly DataGridView Items;
        private readonly TextBox CorrectedTitle;
        private readonly CheckBox ShowReviewed;

        internal NativeGameReviewDialog() : base("游戏识别纠错箱", 1040, 640)
        {
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(4);
            BackColor = Color.FromArgb(64, 48, 150);
            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 235, 250) };
            var header = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.FromArgb(226, 186, 233) };
            var heading = new Label { Dock = DockStyle.Fill, Text = "■ 游戏识别纠错箱  /  GAME REVIEW", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
            var close = MakeButton("×"); close.AutoSize = false; close.Size = new Size(32, 30); close.Dock = DockStyle.Right; close.Click += delegate { Close(); };
            header.Controls.Add(heading); header.Controls.Add(close);
            Point origin = Point.Empty;
            heading.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) origin = e.Location; };
            heading.MouseMove += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Location = new Point(Left + e.X - origin.X, Top + e.Y - origin.Y); };
            var hint = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(14, 10, 0, 0), Text = "这里列出主机识别过的游戏进程。确认一次后不再待审；纠正或忽略会在下次主机采样生效。" };
            Items = MakeGrid(); Items.Columns.Add("game", "识别游戏"); Items.Columns.Add("process", "进程"); Items.Columns.Add("source", "依据"); Items.Columns.Add("path", "程序路径"); Items.Columns.Add("seen", "最近发现"); Items.Columns.Add("status", "状态");
            Items.Columns["path"].FillWeight = 220; Items.Columns["game"].FillWeight = 130;
            Items.SelectionChanged += delegate { if (Items.CurrentRow != null && Items.CurrentRow.Tag is GameDetection) CorrectedTitle.Text = ((GameDetection)Items.CurrentRow.Tag).game; };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 120, Padding = new Padding(12, 6, 0, 0), BackColor = Color.FromArgb(248, 235, 250) };
            ShowReviewed = new CheckBox { Text = "显示已确认", Width = 125, Height = 32 }; ShowReviewed.CheckedChanged += delegate { RefreshItems(); };
            var refresh = MakeButton("刷新"); refresh.Click += delegate { RefreshItems(); };
            actions.Controls.Add(ShowReviewed); actions.Controls.Add(refresh);
            actions.SetFlowBreak(refresh, true);
            actions.Controls.Add(new Label { Text = "正确游戏名：", Width = 100, Height = 34, TextAlign = ContentAlignment.MiddleLeft });
            CorrectedTitle = new TextBox { Width = 240 }; actions.Controls.Add(CorrectedTitle);
            var confirm = MakeButton("确认识别"); confirm.Click += delegate { Apply(false, false); };
            var correct = MakeButton("改成此名称"); correct.Click += delegate { Apply(false, true); };
            var ignore = MakeButton("忽略此进程"); ignore.Click += delegate { Apply(true, false); };
            var reset = MakeButton("恢复自动识别"); reset.Click += delegate { ResetSelected(); };
            actions.Controls.Add(confirm); actions.Controls.Add(correct); actions.Controls.Add(ignore); actions.Controls.Add(reset);
            body.Controls.Add(Items); body.Controls.Add(actions); body.Controls.Add(hint); body.Controls.Add(header); Controls.Add(body);
            RefreshItems();
        }

        private void RefreshItems()
        {
            var state = DisplayServer.Snapshot();
            var reviewed = new HashSet<string>(state.reviewedGameKeys ?? new string[0], StringComparer.OrdinalIgnoreCase);
            Items.Rows.Clear();
            foreach (GameDetection item in state.reviewItems ?? new GameDetection[0])
            {
                if (item == null || (!ShowReviewed.Checked && reviewed.Contains(item.key))) continue;
                int index = Items.Rows.Add(item.game, item.process, item.source, item.key, item.seen > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(item.seen).LocalDateTime.ToString("MM-dd HH:mm") : "—", reviewed.Contains(item.key) ? "已确认" : "待确认");
                Items.Rows[index].Tag = item;
            }
        }

        private void Apply(bool ignore, bool rename)
        {
            var item = Items.CurrentRow == null ? null : Items.CurrentRow.Tag as GameDetection;
            if (item == null) { MessageBox.Show(this, "请先选择一条进程记录。", "游戏识别"); return; }
            string title = rename ? CorrectedTitle.Text.Trim() : item.game;
            try { DisplayServer.ReviewDetection(item.key, title, ignore); RefreshItems(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void ResetSelected()
        {
            var item = Items.CurrentRow == null ? null : Items.CurrentRow.Tag as GameDetection;
            if (item == null) { MessageBox.Show(this, "请先选择一条进程记录。", "游戏识别"); return; }
            try { DisplayServer.ResetGameReview(item.key); RefreshItems(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    internal sealed class NativeSettingsDialog : NativeDialog
    {
        private readonly CheckedListBox Games;
        private readonly CheckBox AutoUpdate;
        private readonly CheckBox HostAutoUpdate;
        private readonly TextBox HostAddress;
        private readonly Label UpdateMessage;
        private readonly ProgressBar UpdateProgress;
        private readonly Button InstallButton;
        private readonly Label DiagnosisText;
        private readonly System.Windows.Forms.Timer RefreshTimer;
        private DashboardState State;

        public NativeSettingsDialog() : base("控制台设置", 780, 630)
        {
            State = DisplayServer.Snapshot();
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 8) };
            var gamesTab = new TabPage("游戏记录");
            var gameTop = new Label { Text = "勾选要记录的游戏。Wallpaper Engine 始终排除。", Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 10, 0, 0) };
            Games = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
            var excluded = new HashSet<string>(State.excludedGames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (string name in (State.knownGames ?? new string[0]).Concat(new[] { "Wallpaper Engine" }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) Games.Items.Add(name, !excluded.Contains(name));
            Games.ItemCheck += delegate(object sender, ItemCheckEventArgs e) { if (String.Equals(Convert.ToString(Games.Items[e.Index]), "Wallpaper Engine", StringComparison.OrdinalIgnoreCase)) e.NewValue = CheckState.Unchecked; };
            var gameActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(8, 7, 0, 0) };
            var save = MakeButton("保存游戏设置"); save.Click += delegate { SaveGames(); };
            var purge = MakeButton("清除选中游戏历史"); purge.Click += delegate { PurgeSelected(); };
            var review = MakeButton("识别纠错箱"); review.Click += delegate { using (var dialog = new NativeGameReviewDialog()) dialog.ShowDialog(this); };
            gameActions.Controls.Add(save); gameActions.Controls.Add(purge); gameActions.Controls.Add(review);
            gamesTab.Controls.Add(Games); gamesTab.Controls.Add(gameTop); gamesTab.Controls.Add(gameActions);

            var versionTab = new TabPage("软件版本");
            var versionLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18) };
            versionLayout.Controls.Add(new Label { Text = "副机版本：" + State.version + "\r\n主机版本：" + (State.online ? State.hostVersion : "主机离线"), Width = 620, Height = 65, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) });
            AutoUpdate = new CheckBox { Text = "副机自动更新", Checked = State.autoUpdate, Width = 600, Height = 38 };
            HostAutoUpdate = new CheckBox { Text = "允许主机自动更新（远程；主机设置页也可关闭）", Checked = State.hostAutoUpdate, Width = 600, Height = 38 };
            versionLayout.Controls.Add(AutoUpdate); versionLayout.Controls.Add(HostAutoUpdate);
            versionLayout.Controls.Add(new Label { Text = "主机 IPv4 地址（当前主机：" + (State.hostAddress ?? "192.168.1.7") + "）", Width = 600, Height = 25, Padding = new Padding(0, 5, 0, 0) });
            HostAddress = new TextBox { Text = State.hostAddress ?? "192.168.1.7", Width = 300, Height = 28 };
            versionLayout.Controls.Add(HostAddress);
            var versionActions = new FlowLayoutPanel { Width = 610, Height = 50 };
            var saveVersion = MakeButton("保存设置"); saveVersion.Click += delegate { SaveGames(); };
            var check = MakeButton("检查 GitHub 更新"); check.Click += delegate { ThreadPool.QueueUserWorkItem(delegate { try { DisplayServer.CheckNativeRelease(); } catch { } }); };
            InstallButton = MakeButton("安装更新"); InstallButton.Enabled = false;
            InstallButton.Click += delegate { try { DisplayServer.InstallNativeUpdate(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法安装", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
            versionActions.Controls.Add(saveVersion); versionActions.Controls.Add(check); versionActions.Controls.Add(InstallButton); versionLayout.Controls.Add(versionActions);
            UpdateProgress = new ProgressBar { Width = 600, Height = 24, Style = ProgressBarStyle.Continuous }; versionLayout.Controls.Add(UpdateProgress);
            UpdateMessage = new Label { Text = "", Width = 620, Height = 80 }; versionLayout.Controls.Add(UpdateMessage);
            versionTab.Controls.Add(versionLayout);

            var foliaTab = new TabPage("Folia");
            var foliaLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
            foliaLayout.Controls.Add(new Label
            {
                Text = "主机播放音乐时，副机会自动在控制台窗口显示 Folia 歌词动效。也可以点击桌面「Folia」图标手动打开。",
                Width = 690, Height = 68, AutoSize = false
            });
            foliaLayout.Controls.Add(new Label
            {
                Text = "主机需运行 Folia，并在 Folia 设置中开启 OBS 浏览器源。副机不需要安装 Folia，也不用填写播放器路径。",
                Width = 690, Height = 58, AutoSize = false
            });
            foliaLayout.Controls.Add(new Label
            {
                Text = "原版歌词动效通过现有主副机连接传到副机，在游戏风格卡片内显示。暂停时保留画面；主机退出 Folia 后自动关闭卡片。",
                Width = 690, Height = 48, AutoSize = false
            });
            foliaTab.Controls.Add(foliaLayout);

            var diagnosisTab = new TabPage("连接诊断");
            var diagnosisLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
            DiagnosisText = new Label { Width = 690, Height = 390, Font = new Font("Microsoft YaHei UI", 11F), ForeColor = Ink };
            diagnosisLayout.Controls.Add(DiagnosisText);
            var copyDiagnosis = MakeButton("复制诊断信息");
            copyDiagnosis.Click += delegate { try { Clipboard.SetText(DiagnosisText.Text); MessageBox.Show(this, "诊断信息已复制，可直接发给我排查。", "连接诊断"); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "复制失败"); } };
            diagnosisLayout.Controls.Add(copyDiagnosis); diagnosisTab.Controls.Add(diagnosisLayout);

            tabs.TabPages.Add(gamesTab); tabs.TabPages.Add(versionTab); tabs.TabPages.Add(foliaTab); tabs.TabPages.Add(diagnosisTab); Controls.Add(tabs);
            RefreshTimer = new System.Windows.Forms.Timer { Interval = 500 }; RefreshTimer.Tick += delegate { RefreshProgress(); if (tabs.SelectedTab == diagnosisTab) RefreshDiagnostics(); }; RefreshTimer.Start();
            FormClosed += delegate { RefreshTimer.Stop(); RefreshTimer.Dispose(); };
            RefreshProgress();
            RefreshDiagnostics();
        }
        private void RefreshDiagnostics()
        {
            var state = DisplayServer.Snapshot();
            Func<long, string> stamp = value => value <= 0 ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            DiagnosisText.Text = "连接状态：" + (state.online ? "主机在线" : "主机离线") + "\r\n" +
                "主机地址：" + (state.hostAddress ?? "—") + ":46702\r\n" +
                "解析地址：" + (String.IsNullOrWhiteSpace(state.resolvedAddress) ? "—" : state.resolvedAddress) + "\r\n" +
                "往返耗时：" + (state.connectionRttMs < 0 ? "—" : state.connectionRttMs + " ms") + "\r\n" +
                "最近状态包：" + (state.packetAgeMs < 0 ? "—" : state.packetAgeMs + " ms 前") + "\r\n" +
                "最近尝试：" + stamp(state.connectionAttemptUtc) + "\r\n" +
                "最近成功：" + stamp(state.connectionSuccessUtc) + "\r\n" +
                "最近错误：" + (String.IsNullOrWhiteSpace(state.connectionError) ? "无" : state.connectionError) + "\r\n\r\n" +
                "主机版本：" + (state.hostVersion ?? "—") + "\r\n" +
                "Folia 进程：" + (state.foliaRunning ? "运行中" : "未运行") + "\r\n" +
                "Folia 浏览器源：" + (state.foliaConnected ? "已连接" : "未连接") + "\r\n" +
                "播放状态：" + (state.foliaPlaying ? "播放中" : "未播放") + "\r\n" +
                "当前歌曲：" + (String.IsNullOrWhiteSpace(state.foliaSong) ? "—" : state.foliaSong) + "\r\n\r\n" +
                "建议：" + (!state.online ? "检查主机程序、地址与 TCP 46702 防火墙规则。" : state.foliaRunning && !state.foliaConnected ? "在主机 Folia 中开启 OBS 浏览器源。" : "连接正常，可在异常发生时复制此页信息。 ");
        }
        private void SaveGames()
        {
            var excluded = new List<string>();
            foreach (object item in Games.Items) if (!Games.GetItemChecked(Games.Items.IndexOf(item))) excluded.Add(Convert.ToString(item));
            try { DisplayServer.SaveNativeSettings(AutoUpdate.Checked, HostAutoUpdate.Checked, excluded, HostAddress.Text); State = DisplayServer.Snapshot(); MessageBox.Show(this, "设置已保存。", "控制台设置"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void PurgeSelected()
        {
            if (Games.SelectedItem == null) { MessageBox.Show(this, "请先选择一个游戏。", "游戏记录"); return; }
            string game = Convert.ToString(Games.SelectedItem);
            if (MessageBox.Show(this, "清除“" + game + "”的全部历史记录？程序会先备份记录文件。", "确认清除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { DisplayServer.PurgeNativeGame(game); MessageBox.Show(this, "已清除记录。", "游戏记录"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "清除失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void RefreshProgress()
        {
            UpdateStatus progress = DisplayServer.GetUpdateProgress();
            UpdateMessage.Text = progress.message ?? "";
            bool running = progress.state == "checking" || progress.state == "downloading" || progress.state == "verifying" || progress.state == "preparing" || progress.state == "installing";
            UpdateProgress.Style = running && progress.percent < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (UpdateProgress.Style == ProgressBarStyle.Continuous) UpdateProgress.Value = Math.Max(0, Math.Min(100, progress.percent));
            InstallButton.Enabled = progress.state == "available";
        }
    }

    internal sealed class NativeHostSettingsDialog : NativeDialog
    {
        private readonly CheckBox AutoUpdate;
        private readonly CheckBox StartWithWindows;
        private readonly CheckedListBox Games;
        private readonly Label Status;
        private readonly Label UpdateMessage;
        private readonly ProgressBar UpdateProgress;
        private readonly Button InstallButton;
        private readonly System.Windows.Forms.Timer RefreshTimer;
        private HostSettingsState State;

        public NativeHostSettingsDialog() : base("主机设置", 780, 640)
        {
            StartPosition = FormStartPosition.CenterScreen;
            State = HostSensor.GetSettingsState();

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 8) };

            var general = new TabPage("常规") { BackColor = Color.FromArgb(248, 235, 250) };
            var generalLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18), AutoScroll = true };
            generalLayout.Controls.Add(new Label { Text = "主机名：" + State.machine + "    版本：" + State.version + "    TCP 端口：46702", Width = 700, Height = 30, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold) });
            AutoUpdate = new CheckBox { Text = "允许主机自动更新（游戏运行时暂缓）", Checked = State.autoUpdate, Width = 680, Height = 34 };
            StartWithWindows = new CheckBox { Text = "登录 Windows 时自动启动主机监控", Checked = State.startWithWindows, Width = 680, Height = 34 };
            generalLayout.Controls.Add(AutoUpdate);
            generalLayout.Controls.Add(StartWithWindows);
            generalLayout.Controls.Add(new Label { Text = "副机的设置页也可远程关闭自动更新；两边都开启时才会自动检查。", Width = 680, Height = 26, ForeColor = Color.FromArgb(90, 80, 130) });
            var generalButtons = new FlowLayoutPanel { Width = 700, Height = 46, FlowDirection = FlowDirection.LeftToRight };
            var saveGeneral = MakeButton("保存常规设置"); saveGeneral.Click += delegate { SaveGeneral(); };
            var firewall = MakeButton("开放防火墙端口…"); firewall.Click += delegate { OpenFirewall(); };
            generalButtons.Controls.Add(saveGeneral); generalButtons.Controls.Add(firewall);
            generalLayout.Controls.Add(generalButtons);
            Status = new Label { Text = "", AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Ink, Font = this.Font };
            generalLayout.Controls.Add(Status);
            general.Controls.Add(generalLayout);

            var gamesPage = new TabPage("游戏追踪") { BackColor = Color.FromArgb(248, 235, 250) };
            var gamesTop = new Label { Text = "勾选要记录的游戏；未勾选的不会发送到副机。Wallpaper Engine 始终排除。", Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 10, 0, 0) };
            Games = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
            Games.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (String.Equals(Convert.ToString(Games.Items[e.Index]), "Wallpaper Engine", StringComparison.OrdinalIgnoreCase)) e.NewValue = CheckState.Unchecked;
            };
            var gameActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(8, 8, 0, 0) };
            var saveGames = MakeButton("保存游戏设置"); saveGames.Click += delegate { SaveGames(); };
            var refresh = MakeButton("刷新列表"); refresh.Click += delegate { ReloadGames(); };
            var addGame = MakeButton("添加游戏 exe…"); addGame.Click += delegate { AddGame(); };
            gameActions.Controls.Add(saveGames); gameActions.Controls.Add(refresh); gameActions.Controls.Add(addGame);
            gamesPage.Controls.Add(Games); gamesPage.Controls.Add(gamesTop); gamesPage.Controls.Add(gameActions);

            var updatePage = new TabPage("软件更新") { BackColor = Color.FromArgb(248, 235, 250) };
            var updateLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18) };
            updateLayout.Controls.Add(new Label { Text = "主机版本：" + State.version, Width = 620, Height = 40, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) });
            var updateActions = new FlowLayoutPanel { Width = 700, Height = 48 };
            var check = MakeButton("检查 GitHub 更新");
            check.Click += delegate { ThreadPool.QueueUserWorkItem(delegate { try { DisplayServer.CheckNativeRelease(); } catch { } }); };
            InstallButton = MakeButton("安装更新"); InstallButton.Enabled = false;
            InstallButton.Click += delegate
            {
                try { HostSensor.InstallUpdate(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法安装", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            updateActions.Controls.Add(check); updateActions.Controls.Add(InstallButton);
            updateLayout.Controls.Add(updateActions);
            UpdateProgress = new ProgressBar { Width = 620, Height = 24, Style = ProgressBarStyle.Continuous };
            updateLayout.Controls.Add(UpdateProgress);
            UpdateMessage = new Label { Width = 680, Height = 90, Text = "" };
            updateLayout.Controls.Add(UpdateMessage);
            updatePage.Controls.Add(updateLayout);

            tabs.TabPages.Add(general); tabs.TabPages.Add(gamesPage); tabs.TabPages.Add(updatePage);
            Controls.Add(tabs);
            RefreshTimer = new System.Windows.Forms.Timer { Interval = 700 };
            RefreshTimer.Tick += delegate { RefreshUpdate(); };
            RefreshTimer.Start();
            FormClosed += delegate { RefreshTimer.Stop(); RefreshTimer.Dispose(); };
            RebuildGames();
            RefreshStatus();
            RefreshUpdate();
        }

        private void RebuildGames()
        {
            Games.Items.Clear();
            var excluded = new HashSet<string>(State.excludedGames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (string name in (State.catalog ?? new string[0]).Concat(new[] { "Wallpaper Engine" }).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                Games.Items.Add(name, !excluded.Contains(name));
        }

        private List<string> CurrentExcluded()
        {
            var excluded = new List<string>();
            foreach (object item in Games.Items)
                if (!Games.GetItemChecked(Games.Items.IndexOf(item))) excluded.Add(Convert.ToString(item));
            return excluded;
        }

        private void SaveGeneral()
        {
            try
            {
                HostSensor.SaveHostSettings(AutoUpdate.Checked, CurrentExcluded());
                HostSensor.SetHostAutostart(StartWithWindows.Checked);
                State = HostSensor.GetSettingsState();
                RefreshStatus();
                MessageBox.Show(this, "主机设置已保存。", "主机设置");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SaveGames()
        {
            try
            {
                HostSensor.SaveHostSettings(AutoUpdate.Checked, CurrentExcluded());
                State = HostSensor.GetSettingsState();
                RefreshStatus();
                MessageBox.Show(this, "游戏追踪设置已保存。", "游戏追踪");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void ReloadGames()
        {
            State = HostSensor.GetSettingsState();
            RebuildGames();
            RefreshStatus();
        }

        private void AddGame()
        {
            using (var dialog = new OpenFileDialog { Filter = "Windows 程序 (*.exe)|*.exe", Title = "选择要追踪的游戏程序", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string label = HostSensor.AddCustomGame(dialog.FileName);
                    State = HostSensor.GetSettingsState();
                    if (!Games.Items.Contains(label)) Games.Items.Add(label, true);
                    MessageBox.Show(this, "已添加“" + label + "”。", "游戏追踪");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "添加失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void OpenFirewall()
        {
            try { HostSensor.OpenFirewallPort(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开防火墙端口", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void RefreshStatus()
        {
            string games = State.currentGames == null || State.currentGames.Length == 0 ? "无" : String.Join("、", State.currentGames);
            Status.Text = "当前主机正在运行的游戏：" + games;
        }

        private void RefreshUpdate()
        {
            var progress = DisplayServer.GetUpdateProgress();
            UpdateMessage.Text = progress.message ?? "";
            bool running = progress.state == "checking" || progress.state == "downloading" || progress.state == "verifying" || progress.state == "preparing" || progress.state == "installing";
            UpdateProgress.Style = running && progress.percent < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (UpdateProgress.Style == ProgressBarStyle.Continuous) UpdateProgress.Value = Math.Max(0, Math.Min(100, progress.percent));
            InstallButton.Enabled = progress.state == "available";
        }
    }
}

