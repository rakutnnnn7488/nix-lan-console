using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NixLanConsoleSetup
{
    internal sealed class SetupForm : Form
    {
        private readonly TextBox InstallPath;
        private readonly Button InstallButton;
        private readonly ProgressBar Progress;
        private readonly Label Status;
        private readonly string Version;
        private string[] ExistingRoles;
        private string[] ExistingExecutables;
        private System.Collections.Generic.Dictionary<string, string> OriginalRunEntries;

        public SetupForm()
        {
            Version = ReadEmbeddedVersion();
            Text = "Nix LAN Console 安装程序";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(620, 270);
            BackColor = Color.FromArgb(250, 240, 252);
            Font = new Font("Microsoft YaHei UI", 10F);

            var title = new Label { Text = "安装 Nix LAN Console  " + Version, Location = new Point(24, 20), Size = new Size(570, 36), Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold), ForeColor = Color.FromArgb(62, 48, 140) };
            var description = new Label { Text = "完整安装程序会替换程序文件并保留本机设置和游戏记录。无需先卸载旧版。", Location = new Point(26, 66), Size = new Size(570, 28), ForeColor = Color.FromArgb(75, 63, 119) };
            var pathLabel = new Label { Text = "安装位置", Location = new Point(26, 112), Size = new Size(82, 28), TextAlign = ContentAlignment.MiddleLeft };
            InstallPath = new TextBox { Location = new Point(108, 112), Size = new Size(390, 28), Text = FindInstallPath() };
            var browse = new Button { Text = "浏览…", Location = new Point(508, 110), Size = new Size(86, 32) };
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "选择程序安装目录", SelectedPath = InstallPath.Text, ShowNewFolderButton = true }) if (dialog.ShowDialog(this) == DialogResult.OK) InstallPath.Text = dialog.SelectedPath; };
            Progress = new ProgressBar { Location = new Point(28, 164), Size = new Size(566, 22), Minimum = 0, Maximum = 100 };
            Status = new Label { Text = "将安装到当前用户可写入的位置，不需要管理员权限。", Location = new Point(28, 193), Size = new Size(566, 28), ForeColor = Color.FromArgb(85, 74, 130) };
            InstallButton = new Button { Text = "安装并启动", Location = new Point(440, 226), Size = new Size(154, 34), BackColor = Color.FromArgb(221, 199, 237), ForeColor = Color.FromArgb(62, 48, 140), FlatStyle = FlatStyle.Flat };
            InstallButton.Click += delegate { BeginInstall(); };
            AcceptButton = InstallButton;
            Controls.AddRange(new Control[] { title, description, pathLabel, InstallPath, browse, Progress, Status, InstallButton });
        }

        private static string FindInstallPath()
        {
            foreach (string name in new[] { "NixLanConsoleDisplay", "NixLanConsoleHost" })
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                    {
                        string command = Convert.ToString(key == null ? null : key.GetValue(name));
                        string executable = ExtractExecutable(command);
                        if (!String.IsNullOrEmpty(executable) && File.Exists(executable)) return Path.GetDirectoryName(executable);
                    }
                }
                catch { }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NixLanConsole");
        }

        private static string ReadEmbeddedVersion()
        {
            try
            {
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("NixLanConsoleSetup.PayloadZip"))
                using (var archive = new ZipArchive(resource, ZipArchiveMode.Read))
                using (Stream version = archive.GetEntry("version.txt").Open())
                using (var reader = new StreamReader(version)) return reader.ReadToEnd().Trim();
            }
            catch { return "未知版本"; }
        }

        private static string ExtractExecutable(string command)
        {
            if (String.IsNullOrWhiteSpace(command)) return null;
            command = command.Trim();
            if (command[0] == '"') { int end = command.IndexOf('"', 1); return end > 1 ? command.Substring(1, end - 1) : null; }
            int space = command.IndexOf(' ');
            return space > 0 ? command.Substring(0, space) : command;
        }

        private void BeginInstall()
        {
            string target;
            try
            {
                target = Path.GetFullPath(InstallPath.Text.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (String.IsNullOrWhiteSpace(target) || target.Length < 4) throw new InvalidOperationException("请选择有效的安装目录。");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "安装位置无效", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            ExistingRoles = ReadExistingRoles();
            ExistingExecutables = ReadExistingExecutables();
            OriginalRunEntries = ReadRunEntries();
            InstallButton.Enabled = false; InstallPath.Enabled = false; Progress.Value = 0;
            var worker = new System.ComponentModel.BackgroundWorker { WorkerReportsProgress = true };
            worker.ProgressChanged += delegate(object sender, System.ComponentModel.ProgressChangedEventArgs e) { Progress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage)); Status.Text = Convert.ToString(e.UserState); };
            worker.DoWork += delegate(object sender, System.ComponentModel.DoWorkEventArgs e) { Install(target, worker); };
            worker.RunWorkerCompleted += delegate(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
            {
                if (e.Error != null)
                {
                    InstallButton.Enabled = true; InstallPath.Enabled = true;
                    Status.Text = "安装失败，旧版本已保留或恢复。";
                    MessageBox.Show(this, e.Error.GetBaseException().Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Progress.Value = 100; Status.Text = "安装完成，正在启动程序。";
                Close();
            };
            worker.RunWorkerAsync();
        }

        private void Install(string target, System.ComponentModel.BackgroundWorker worker, bool testMode = false, bool failAfterSwitch = false)
        {
            string parent = Path.GetDirectoryName(target);
            if (String.IsNullOrEmpty(parent)) throw new InvalidOperationException("安装目录必须位于一个有效的磁盘目录中。");
            Directory.CreateDirectory(parent);
            string stage = target + ".installing-" + Guid.NewGuid().ToString("N");
            string backup = target + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            bool movedOld = false, movedNew = false;
            try
            {
                worker.ReportProgress(5, "正在检查旧版本进程…");
                StopProcessesFrom(target);
                foreach (string oldExe in ExistingExecutables) StopProcessesFrom(Path.GetDirectoryName(oldExe));
                worker.ReportProgress(12, "正在解压完整安装文件…");
                Directory.CreateDirectory(stage);
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("NixLanConsoleSetup.PayloadZip"))
                {
                    if (resource == null) throw new InvalidDataException("安装包内缺少完整程序数据。");
                    using (var archive = new ZipArchive(resource, ZipArchiveMode.Read))
                    {
                        int count = Math.Max(1, archive.Entries.Count), index = 0;
                        foreach (var entry in archive.Entries)
                        {
                            string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                            if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(x => x == "..")) throw new InvalidDataException("安装包包含无效路径。");
                            string output = Path.GetFullPath(Path.Combine(stage, relative));
                            string prefix = Path.GetFullPath(stage) + Path.DirectorySeparatorChar;
                            if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装包路径超出了安装目录。");
                            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) Directory.CreateDirectory(output);
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(output));
                                using (Stream input = entry.Open()) using (var file = File.Create(output)) input.CopyTo(file);
                            }
                            index++;
                            if (index % 8 == 0 || index == count) worker.ReportProgress(12 + (int)(72.0 * index / count), "正在解压文件… " + index + "/" + count);
                        }
                    }
                }
                if (!File.Exists(Path.Combine(stage, "ControlConsole.exe")) || !File.Exists(Path.Combine(stage, "versions", Version, "ControlConsole.exe"))) throw new InvalidDataException("安装包未通过完整性检查：缺少程序文件。");
                if (!File.Exists(Path.Combine(stage, "versions", Version, ".complete"))) throw new InvalidDataException("安装包未通过完整性检查：版本目录未完成。");
                if (!File.Exists(Path.Combine(stage, "current.txt"))) throw new InvalidDataException("安装包未通过完整性检查：缺少 current.txt。");
                if (!File.Exists(Path.Combine(stage, "game-assets", "native-role", "角色动画.json"))) throw new InvalidDataException("安装包未通过完整性检查：缺少角色动画素材。");
                worker.ReportProgress(88, "正在切换到新版本…");
                if (Directory.Exists(target)) { Directory.Move(target, backup); movedOld = true; }
                Directory.Move(stage, target); movedNew = true;
                if (testMode)
                {
                    if (failAfterSwitch) throw new IOException("模拟安装后校验失败");
                    worker.ReportProgress(100, "隔离安装验证成功。");
                    return;
                }
                UpdateRunEntries(target);
                worker.ReportProgress(95, "安装完成，正在启动控制台…");
                string exe = Path.Combine(target, "ControlConsole.exe");
                string role = ExistingRoles.FirstOrDefault() ?? "";
                Process started = null;
                try
                {
                    string readyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole", "startup-ready.txt");
                    if (File.Exists(readyFile)) File.Delete(readyFile);
                    started = Process.Start(new ProcessStartInfo(exe, role) { UseShellExecute = true, WorkingDirectory = target });
                    if (started == null) throw new InvalidOperationException("Windows 未能启动安装后的程序。");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(String.IsNullOrEmpty(role) ? 5 : 35);
                    bool ready = String.IsNullOrEmpty(role);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (!String.IsNullOrEmpty(role) && File.Exists(readyFile))
                        {
                            string[] parts = File.ReadAllText(readyFile).Trim().TrimStart('\uFEFF').Split('|');
                            if (parts.Length >= 3 && parts[1] == Version && parts[2] == role) { ready = true; break; }
                        }
                        started.Refresh();
                        if (started.HasExited)
                        {
                            // The launcher exits after it has verified the app; re-check readiness once.
                            if (File.Exists(readyFile))
                            {
                                string[] parts = File.ReadAllText(readyFile).Trim().TrimStart('\uFEFF').Split('|');
                                if (parts.Length >= 3 && parts[1] == Version && parts[2] == role) { ready = true; break; }
                            }
                            throw new InvalidOperationException("新版本启动后立即退出。安装已回滚，请稍后重试。");
                        }
                        System.Threading.Thread.Sleep(250);
                    }
                    if (!ready) throw new InvalidOperationException("程序没有报告启动就绪。安装已回滚，请稍后重试。");
                    started.Dispose(); started = null;
                }
                catch
                {
                    try { if (started != null) { if (!started.HasExited) { started.Kill(); started.WaitForExit(5000); } started.Dispose(); } } catch { }
                    if (movedNew && Directory.Exists(target)) Directory.Delete(target, true);
                    if (movedOld && Directory.Exists(backup)) Directory.Move(backup, target);
                    throw;
                }
                worker.ReportProgress(100, movedOld ? "安装完成；旧版备份保留在安装目录旁。" : "首次安装完成。");
            }
            catch
            {
                RestoreRunEntries(OriginalRunEntries);
                if (movedNew && Directory.Exists(target)) Directory.Delete(target, true);
                if (movedOld && Directory.Exists(backup) && !Directory.Exists(target)) Directory.Move(backup, target);
                throw;
            }
            finally { if (Directory.Exists(stage)) try { Directory.Delete(stage, true); } catch { } }
        }

        internal static int RunInstallTest(string target, bool simulateFailure)
        {
            target = Path.GetFullPath(target);
            if (Directory.Exists(target)) return 10;
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "legacy.marker"), "previous version");
            var form = new SetupForm();
            form.ExistingRoles = new string[0];
            form.ExistingExecutables = new string[0];
            form.OriginalRunEntries = null;
            var worker = new System.ComponentModel.BackgroundWorker { WorkerReportsProgress = true };
            string[] backups = new string[0];
            int result = 0;
            try
            {
                bool injectedFailure = false;
                try { form.Install(target, worker, true, simulateFailure); }
                catch (Exception ex)
                {
                    if (!simulateFailure || ex.Message != "模拟安装后校验失败") throw;
                    injectedFailure = true;
                }
                backups = Directory.GetDirectories(Path.GetDirectoryName(target), Path.GetFileName(target) + ".backup-*");
                if (simulateFailure)
                {
                    if (!injectedFailure || !File.Exists(Path.Combine(target, "legacy.marker")) || File.Exists(Path.Combine(target, "ControlConsole.exe"))) result = 11;
                }
                else
                {
                    if (!File.Exists(Path.Combine(target, "ControlConsole.exe")) || !File.Exists(Path.Combine(target, "versions", form.Version, "ControlConsole.exe")) || !File.Exists(Path.Combine(target, "versions", form.Version, ".complete")) || !File.Exists(Path.Combine(target, "game-assets", "native-role", "角色动画.json"))) result = 12;
                    if (backups.Length != 1 || !File.Exists(Path.Combine(backups[0], "legacy.marker"))) result = 13;
                }
            }
            catch { result = 14; }
            finally
            {
                if (Directory.Exists(target)) try { Directory.Delete(target, true); } catch { result = 15; }
                foreach (string backup in backups) if (Directory.Exists(backup)) try { Directory.Delete(backup, true); } catch { result = 16; }
                form.Dispose();
            }
            return result;
        }

        private static void StopProcessesFrom(string target)
        {
            string root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (Process process in Process.GetProcessesByName("ControlConsole"))
            {
                try
                {
                    string actual = Path.GetFullPath(process.MainModule.FileName);
                    if (actual.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(2500)) process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                catch (ArgumentException) { }
            }
        }

        private static string[] ReadExistingRoles()
        {
            var roles = new System.Collections.Generic.List<string>();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                    foreach (string name in new[] { "NixLanConsoleDisplay", "NixLanConsoleHost" })
                    {
                        string command = Convert.ToString(key == null ? null : key.GetValue(name));
                        if (String.IsNullOrWhiteSpace(command)) continue;
                        string role = name == "NixLanConsoleDisplay" ? "--display" : "--sensor";
                        if (command.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0) roles.Add(role);
                    }
            }
            catch { }
            return roles.ToArray();
        }

        private static string[] ReadExistingExecutables()
        {
            var paths = new System.Collections.Generic.List<string>();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                    foreach (string name in new[] { "NixLanConsoleDisplay", "NixLanConsoleHost" })
                    {
                        string exe = ExtractExecutable(Convert.ToString(key == null ? null : key.GetValue(name)));
                        if (!String.IsNullOrWhiteSpace(exe) && File.Exists(exe) && !paths.Contains(exe, StringComparer.OrdinalIgnoreCase)) paths.Add(exe);
                    }
            }
            catch { }
            return paths.ToArray();
        }

        private static System.Collections.Generic.Dictionary<string, string> ReadRunEntries()
        {
            var entries = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                    foreach (string name in new[] { "NixLanConsoleDisplay", "NixLanConsoleHost" })
                        entries[name] = Convert.ToString(key == null ? null : key.GetValue(name));
            }
            catch { }
            return entries;
        }

        private static void RestoreRunEntries(System.Collections.Generic.Dictionary<string, string> entries)
        {
            if (entries == null) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                    foreach (var entry in entries)
                    {
                        if (String.IsNullOrEmpty(entry.Value)) key.DeleteValue(entry.Key, false);
                        else key.SetValue(entry.Key, entry.Value, RegistryValueKind.String);
                    }
            }
            catch { }
        }

        private static void UpdateRunEntries(string target)
        {
            string exe = Path.Combine(target, "ControlConsole.exe");
            using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            {
                foreach (string name in new[] { "NixLanConsoleDisplay", "NixLanConsoleHost" })
                {
                    string current = Convert.ToString(key.GetValue(name));
                    if (String.IsNullOrWhiteSpace(current)) continue;
                    string role = name == "NixLanConsoleDisplay" ? "--display" : "--sensor";
                    key.SetValue(name, "\"" + exe + "\" " + role, RegistryValueKind.String);
                }
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 2 && (args[0] == "--test-install" || args[0] == "--test-rollback"))
            {
                Environment.ExitCode = SetupForm.RunInstallTest(args[1], args[0] == "--test-rollback");
                return;
            }
            if (args.Length > 0 && args[0] == "--verify")
            {
                try
                {
                    using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("NixLanConsoleSetup.PayloadZip"))
                    {
                        if (resource == null) { Environment.ExitCode = 2; return; }
                        using (var archive = new ZipArchive(resource, ZipArchiveMode.Read))
                        {
                            var versionEntry = archive.GetEntry("version.txt");
                            if (versionEntry == null) { Environment.ExitCode = 4; return; }
                            string versionText;
                            using (var stream = versionEntry.Open()) using (var reader = new StreamReader(stream, Encoding.UTF8)) versionText = reader.ReadToEnd().Trim().TrimStart('\uFEFF');
                            Version version;
                            if (!System.Version.TryParse(versionText, out version)) { Environment.ExitCode = 4; return; }
                            if (archive.GetEntry("ControlConsole.exe") == null
                                || !archive.Entries.Any(e => String.Equals(e.FullName.Replace('\\', '/'), "versions/" + version + "/ControlConsole.exe", StringComparison.OrdinalIgnoreCase))
                                || !archive.Entries.Any(e => String.Equals(e.FullName.Replace('\\', '/'), "versions/" + version + "/.complete", StringComparison.OrdinalIgnoreCase))
                                || archive.GetEntry("current.txt") == null
                                || !archive.Entries.Any(e => String.Equals(e.FullName.Replace('\\', '/'), "game-assets/native-role/角色动画.json", StringComparison.Ordinal))) { Environment.ExitCode = 3; return; }
                            var currentEntry = archive.GetEntry("current.txt");
                            string currentText;
                            using (var stream = currentEntry.Open()) using (var reader = new StreamReader(stream, Encoding.UTF8)) currentText = reader.ReadToEnd().Trim().TrimStart('\uFEFF');
                            if (!String.Equals(currentText, version.ToString(), StringComparison.OrdinalIgnoreCase)) { Environment.ExitCode = 6; return; }
                        }
                    }
                    Environment.ExitCode = 0;
                }
                catch { Environment.ExitCode = 5; }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new SetupForm()); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "安装程序错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
