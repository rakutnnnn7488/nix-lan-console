using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NixLanConsoleUpdater
{
    internal sealed class UpdateWindow : Form
    {
        private readonly ProgressBar Progress;
        private readonly Label Status;
        private readonly Button CloseButton;
        private bool Working;
        private readonly string Target;
        private readonly string ArchivePath;
        private readonly int ParentPid;
        private readonly string Role;
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");

        internal UpdateWindow(string target, string archivePath, int parentPid, string role)
        {
            Target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            ArchivePath = Path.GetFullPath(archivePath);
            ParentPid = parentPid;
            Role = role;
            Text = "Nix LAN Console 更新";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ClientSize = new Size(560, 205);
            BackColor = Color.FromArgb(250, 240, 252);
            Font = new Font("Microsoft YaHei UI", 10F);
            Controls.Add(new Label { Text = "正在更新控制台", Location = new Point(24, 18), Size = new Size(500, 38), Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold), ForeColor = Color.FromArgb(62, 48, 140) });
            Status = new Label { Text = "准备安装更新…", Location = new Point(26, 66), Size = new Size(510, 44), ForeColor = Color.FromArgb(75, 63, 119) };
            Progress = new ProgressBar { Location = new Point(26, 118), Size = new Size(508, 24), Minimum = 0, Maximum = 100 };
            CloseButton = new Button { Text = "关闭", Location = new Point(424, 158), Size = new Size(110, 32), Enabled = false };
            CloseButton.Click += delegate { Close(); };
            Controls.Add(Status); Controls.Add(Progress); Controls.Add(CloseButton);
            Shown += delegate { BeginUpdate(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (Working) e.Cancel = true; };
        }

        private void BeginUpdate()
        {
            Working = true;
            var worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e)
            {
                Progress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                Status.Text = Convert.ToString(e.UserState);
            };
            worker.DoWork += delegate { Apply(Target, ArchivePath, ParentPid, Role, worker, false, false); };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                Working = false;
                if (e.Error == null) { Progress.Value = 100; Status.Text = "更新成功，控制台已重新启动。"; Close(); return; }
                Status.Text = "更新失败，已尝试恢复旧版本。";
                CloseButton.Enabled = true;
                MessageBox.Show(this, e.Error.GetBaseException().Message + "\r\n\r\n详情已写入 update.log。", "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            worker.RunWorkerAsync();
        }

        private static void Report(BackgroundWorker worker, int percent, string message)
        {
            if (worker != null) worker.ReportProgress(percent, message);
            Log(message);
        }

        private static void Log(string message)
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " native updater: " + message + Environment.NewLine, Encoding.UTF8); } catch { }
        }

        private static string PackageVersion(string archivePath)
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var versionEntry = archive.GetEntry("version.txt");
                if (versionEntry == null || archive.GetEntry("ControlConsole.exe") == null || archive.GetEntry("Updater.exe") == null) throw new InvalidDataException("更新包缺少必要程序文件。");
                using (var reader = new StreamReader(versionEntry.Open()))
                {
                    string value = reader.ReadToEnd().Trim();
                    Version parsed;
                    if (!Version.TryParse(value, out parsed)) throw new InvalidDataException("更新包版本无效。");
                    return value;
                }
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }

        private static void ExtractUpdate(string archivePath, string stage, BackgroundWorker worker)
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                int count = Math.Max(1, archive.Entries.Count), index = 0;
                string prefix = Path.GetFullPath(stage) + Path.DirectorySeparatorChar;
                foreach (var entry in archive.Entries)
                {
                    string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(x => x == ".." || x == ".")) throw new InvalidDataException("更新包包含无效路径。");
                    string output = Path.GetFullPath(Path.Combine(stage, relative));
                    if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包路径超出安装目录。");
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal)) Directory.CreateDirectory(output);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(output));
                        using (var input = entry.Open()) using (var file = File.Create(output)) input.CopyTo(file);
                    }
                    index++;
                    if (index % 3 == 0 || index == count) Report(worker, 50 + (int)(25.0 * index / count), "正在验证和准备新版文件… " + index + "/" + count);
                }
            }
        }

        private static void WaitForParent(int pid)
        {
            if (pid <= 0) return;
            try
            {
                using (var parent = Process.GetProcessById(pid))
                    if (!parent.WaitForExit(90000)) throw new TimeoutException("旧版本未退出，更新已取消。");
            }
            catch (ArgumentException) { }
        }

        internal static bool TryPrepareManual(out string target, out string archivePath, out int parentPid, out string role, out string error)
        {
            target = archivePath = role = error = null;
            parentPid = 0;
            try
            {
                string installedExe = null;
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                {
                    foreach (string name in new[] { "NixLanConsoleDisplay", "NixLanConsoleHost" })
                    {
                        string command = Convert.ToString(key == null ? null : key.GetValue(name));
                        if (String.IsNullOrWhiteSpace(command)) continue;
                        command = command.Trim();
                        if (command[0] != '"') continue;
                        int end = command.IndexOf('"', 1);
                        if (end <= 1) continue;
                        string candidate = command.Substring(1, end - 1);
                        if (!File.Exists(candidate)) continue;
                        installedExe = Path.GetFullPath(candidate);
                        role = name == "NixLanConsoleDisplay" ? "--display" : "--sensor";
                        break;
                    }
                }
                if (installedExe == null) throw new FileNotFoundException("没有找到已安装的控制台。请先在旧版中设置主机或副机角色。");
                target = Path.GetDirectoryName(installedExe);
                string package = AppDomain.CurrentDomain.BaseDirectory;
                foreach (string name in new[] { "ControlConsole.exe", "Updater.exe", "ApplyUpdate.ps1", "version.txt" })
                    if (!File.Exists(Path.Combine(package, name))) throw new FileNotFoundException("便携包缺少 " + name);
                Version oldVersion, newVersion;
                string oldFile = Path.Combine(target, "version.txt");
                if (File.Exists(oldFile) && Version.TryParse(File.ReadAllText(oldFile).Trim().TrimStart('\uFEFF'), out oldVersion) && Version.TryParse(File.ReadAllText(Path.Combine(package, "version.txt")).Trim().TrimStart('\uFEFF'), out newVersion) && newVersion <= oldVersion)
                    throw new InvalidOperationException("当前安装版本已经是 " + oldVersion + "，无需升级。");
                Directory.CreateDirectory(Path.Combine(DataDir, "updates"));
                archivePath = Path.Combine(DataDir, "updates", "manual-" + Guid.NewGuid().ToString("N") + ".zip");
                using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                    foreach (string name in new[] { "ControlConsole.exe", "Updater.exe", "ApplyUpdate.ps1", "version.txt" })
                        zip.CreateEntryFromFile(Path.Combine(package, name), name, CompressionLevel.Optimal);
                foreach (var process in Process.GetProcessesByName("ControlConsole"))
                {
                    try { if (String.Equals(Path.GetFullPath(process.MainModule.FileName), installedExe, StringComparison.OrdinalIgnoreCase)) { parentPid = process.Id; break; } }
                    catch { }
                    finally { process.Dispose(); }
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static void StartAndVerify(string target, string role, string version)
        {
            string readyFile = Path.Combine(DataDir, "startup-ready.txt");
            if (File.Exists(readyFile)) File.Delete(readyFile);
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(target, "ControlConsole.exe"), role) { UseShellExecute = true, WorkingDirectory = target }))
            {
                if (process == null) throw new InvalidOperationException("Windows 未能启动新版控制台。");
                try
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                    bool ready = false;
                    while (DateTime.UtcNow < deadline)
                    {
                        process.Refresh();
                        if (process.HasExited) throw new InvalidOperationException("新版控制台启动后立即退出。");
                        if (File.Exists(readyFile))
                        {
                            string[] parts = File.ReadAllText(readyFile).Trim().TrimStart('\uFEFF').Split('|');
                            if (parts.Length >= 3 && parts[0] == process.Id.ToString() && parts[1] == version && parts[2] == role) { ready = true; break; }
                        }
                        Thread.Sleep(250);
                    }
                    if (!ready) throw new TimeoutException("新版控制台没有报告启动就绪。");
                    Thread.Sleep(1000);
                    process.Refresh();
                    if (process.HasExited) throw new InvalidOperationException("新版控制台在启动检查后退出。");
                }
                catch
                {
                    try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } } catch { }
                    throw;
                }
            }
        }

        internal static void Apply(string target, string archivePath, int parentPid, string role, BackgroundWorker worker, bool testMode, bool failAfterSwitch)
        {
            target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            archivePath = Path.GetFullPath(archivePath);
            if (target.Length < 4 || (role != "--display" && role != "--sensor")) throw new ArgumentException("更新参数无效。");
            string parentDir = Path.GetDirectoryName(target);
            if (String.IsNullOrEmpty(parentDir) || !Directory.Exists(target)) throw new DirectoryNotFoundException("旧版本目录不存在。");
            if (!File.Exists(Path.Combine(target, "ControlConsole.exe"))) throw new FileNotFoundException("旧版程序不存在。");
            string version = PackageVersion(archivePath);
            string stage = target + ".staging-" + Guid.NewGuid().ToString("N");
            string backup = target + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            bool movedOld = false, movedNew = false;
            try
            {
                Report(worker, 5, "请在右下角托盘退出旧版控制台；退出后自动继续…");
                if (!testMode) WaitForParent(parentPid);
                Report(worker, 15, "正在复制旧版和角色素材…");
                CopyDirectory(target, stage);
                Report(worker, 50, "正在验证并解压更新文件…");
                ExtractUpdate(archivePath, stage, worker);
                if (File.ReadAllText(Path.Combine(stage, "version.txt")).Trim().TrimStart('\uFEFF') != version) throw new InvalidDataException("新版版本信息不一致。");
                Report(worker, 80, "正在切换到新版…");
                Directory.Move(target, backup); movedOld = true;
                Directory.Move(stage, target); movedNew = true;
                if (failAfterSwitch) throw new IOException("模拟新版启动失败");
                if (!testMode)
                {
                    Report(worker, 90, "正在启动并检查新版控制台…");
                    StartAndVerify(target, role, version);
                    string failed = Path.Combine(DataDir, "failed-update.txt");
                    if (File.Exists(failed)) File.Delete(failed);
                }
                Report(worker, 100, "更新成功。旧版备份：" + backup);
            }
            catch (Exception ex)
            {
                Log("失败：" + ex);
                if (!testMode) try { Directory.CreateDirectory(DataDir); File.WriteAllText(Path.Combine(DataDir, "failed-update.txt"), version, Encoding.UTF8); } catch { }
                if (movedNew && Directory.Exists(target)) Directory.Delete(target, true);
                if (movedOld && Directory.Exists(backup)) Directory.Move(backup, target);
                if (!testMode && movedOld)
                    try { Process.Start(new ProcessStartInfo(Path.Combine(target, "ControlConsole.exe"), role) { UseShellExecute = true, WorkingDirectory = target }); Log("旧版已重新启动。"); } catch (Exception restartError) { Log("旧版重启失败：" + restartError); }
                throw;
            }
            finally { if (Directory.Exists(stage)) try { Directory.Delete(stage, true); } catch { } }
        }

        internal static int Test(string target, string archivePath, bool rollback)
        {
            target = Path.GetFullPath(target);
            if (Directory.Exists(target)) return 10;
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "ControlConsole.exe"), "old program");
            File.WriteAllText(Path.Combine(target, "old.marker"), "old version");
            string assetDir = Path.Combine(target, "game-assets"); Directory.CreateDirectory(assetDir);
            File.WriteAllText(Path.Combine(assetDir, "asset.marker"), "keep asset");
            string[] backups = new string[0];
            int result = 0;
            try
            {
                bool failed = false;
                try { Apply(target, archivePath, 0, "--display", null, true, rollback); }
                catch (IOException ex) { if (rollback && ex.Message == "模拟新版启动失败") failed = true; else throw; }
                backups = Directory.GetDirectories(Path.GetDirectoryName(target), Path.GetFileName(target) + ".backup-*");
                if (rollback)
                {
                    if (!failed || !File.Exists(Path.Combine(target, "old.marker")) || File.ReadAllText(Path.Combine(target, "ControlConsole.exe")) != "old program") result = 11;
                }
                else if (!File.Exists(Path.Combine(target, "version.txt")) || !File.Exists(Path.Combine(target, "game-assets", "asset.marker")) || backups.Length != 1 || !File.Exists(Path.Combine(backups[0], "old.marker"))) result = 12;
            }
            catch { result = 13; }
            finally
            {
                if (Directory.Exists(target)) try { Directory.Delete(target, true); } catch { result = 14; }
                foreach (string backup in backups) if (Directory.Exists(backup)) try { Directory.Delete(backup, true); } catch { result = 15; }
            }
            return result;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                string target, archive, role, error; int pid;
                if (!UpdateWindow.TryPrepareManual(out target, out archive, out pid, out role, out error)) { MessageBox.Show(error, "Nix LAN Console 升级", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new UpdateWindow(target, archive, pid, role));
                return;
            }
            if (args.Length == 3 && (args[0] == "--test-install" || args[0] == "--test-rollback"))
            {
                Environment.ExitCode = UpdateWindow.Test(args[1], args[2], args[0] == "--test-rollback");
                return;
            }
            if (args.Length != 5 || args[0] != "--apply") { MessageBox.Show("更新程序参数无效。", "Nix LAN Console 更新", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            int parentPid;
            if (!Int32.TryParse(args[3], out parentPid)) { MessageBox.Show("旧版进程编号无效。", "Nix LAN Console 更新", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UpdateWindow(args[1], args[2], parentPid, args[4]));
        }
    }
}
