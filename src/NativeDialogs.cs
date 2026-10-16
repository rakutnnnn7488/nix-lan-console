using System;
using System.Collections.Generic;
using System.Drawing;
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
        private readonly ComboBox Range;
        private readonly DateTimePicker From;
        private readonly DateTimePicker To;
        private readonly DataGridView Totals;
        private readonly DataGridView Sessions;
        private readonly Label Summary;
        private DashboardState State;

        public NativeRecordsDialog() : base("游戏运行记录", 940, 680)
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 57, Padding = new Padding(12, 9, 0, 0) };
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

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 7) };
            Totals = MakeGrid(); Totals.Columns.Add("game", "游戏"); Totals.Columns.Add("duration", "时长");
            Sessions = MakeGrid(); Sessions.Columns.Add("game", "游戏"); Sessions.Columns.Add("start", "开始"); Sessions.Columns.Add("end", "结束"); Sessions.Columns.Add("duration", "时长");
            var totalsTab = new TabPage("游戏汇总"); totalsTab.Controls.Add(Totals);
            var sessionsTab = new TabPage("逐次记录"); sessionsTab.Controls.Add(Sessions);
            tabs.TabPages.Add(totalsTab); tabs.TabPages.Add(sessionsTab); Controls.Add(tabs);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(12, 6, 0, 0), FlowDirection = FlowDirection.LeftToRight };
            Summary = new Label { Text = "", Width = 360, Height = 55, TextAlign = ContentAlignment.MiddleLeft };
            var edit = MakeButton("修改选中记录"); edit.Click += delegate { EditSelected(); };
            var remove = MakeButton("删除选中记录"); remove.Click += delegate { DeleteSelected(); };
            bottom.Controls.Add(Summary); bottom.Controls.Add(edit); bottom.Controls.Add(remove); Controls.Add(bottom);
            RefreshRecords();
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

    internal sealed class NativeSettingsDialog : NativeDialog
    {
        private readonly CheckedListBox Games;
        private readonly CheckBox AutoUpdate;
        private readonly CheckBox HostAutoUpdate;
        private readonly TextBox HostAddress;
        private readonly Label UpdateMessage;
        private readonly ProgressBar UpdateProgress;
        private readonly Button InstallButton;
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
            gameActions.Controls.Add(save); gameActions.Controls.Add(purge);
            gamesTab.Controls.Add(Games); gamesTab.Controls.Add(gameTop); gamesTab.Controls.Add(gameActions);

            var versionTab = new TabPage("软件版本");
            var versionLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18) };
            versionLayout.Controls.Add(new Label { Text = "副机版本：" + State.version + "\r\n主机版本：" + (State.online ? State.hostVersion : "主机离线"), Width = 620, Height = 65, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) });
            AutoUpdate = new CheckBox { Text = "副机自动更新", Checked = State.autoUpdate, Width = 600, Height = 38 };
            HostAutoUpdate = new CheckBox { Text = "主机自动更新（游戏运行时暂缓）", Checked = State.hostAutoUpdate, Width = 600, Height = 38 };
            versionLayout.Controls.Add(AutoUpdate); versionLayout.Controls.Add(HostAutoUpdate);
            versionLayout.Controls.Add(new Label { Text = "主机 IPv4 地址（当前主机：192.168.1.7）", Width = 600, Height = 25, Padding = new Padding(0, 5, 0, 0) });
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
            tabs.TabPages.Add(gamesTab); tabs.TabPages.Add(versionTab); Controls.Add(tabs);
            RefreshTimer = new System.Windows.Forms.Timer { Interval = 500 }; RefreshTimer.Tick += delegate { RefreshProgress(); }; RefreshTimer.Start();
            FormClosed += delegate { RefreshTimer.Stop(); RefreshTimer.Dispose(); };
            RefreshProgress();
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
}
