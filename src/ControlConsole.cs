using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace NixLanConsole
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length > 0 && args[0] == "--sensor")
                {
                    bool created;
                    using (var mutex = new Mutex(true, "Local\\NixLanConsole.Host", out created))
                        if (created) HostSensor.Run();
                    return;
                }
                if (args.Length > 0 && args[0] == "--display")
                {
                    bool created;
                    using (var mutex = new Mutex(true, "Local\\NixLanConsole.Display", out created))
                        if (created) DisplayServer.Run();
                    return;
                }
                if (args.Length > 0 && args[0] == "--native-preview")
                {
                    Application.Run(new NativeConsoleWindow());
                    return;
                }
                Application.Run(new RolePicker());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "局域网桌面控制台", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class RolePicker : Form
    {
        public RolePicker()
        {
            Text = "局域网桌面控制台 · 首次设置";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(560, 300); BackColor = Color.FromArgb(250, 236, 250);
            var title = new Label { Text = "选择这台电脑的角色", Font = new Font("Microsoft YaHei", 18, FontStyle.Bold), ForeColor = Color.FromArgb(64, 49, 142), AutoSize = true, Location = new Point(28, 20) };
            var info = new Label { Text = "主机负责发送硬件状态和游戏进程；副机显示控制台并记录游戏时长。\r\n两台电脑都选择一次，之后会在登录 Windows 时自动运行。", Font = new Font("Microsoft YaHei", 10), AutoSize = true, Location = new Point(32, 65) };
            var host = new Button { Text = "设置为主机", Location = new Point(34, 145), Size = new Size(230, 48), Font = new Font("Microsoft YaHei", 12, FontStyle.Bold), BackColor = Color.FromArgb(210, 184, 231) };
            var display = new Button { Text = "设置为副机", Location = new Point(294, 145), Size = new Size(230, 48), Font = new Font("Microsoft YaHei", 12, FontStyle.Bold), BackColor = Color.FromArgb(178, 225, 233) };
            host.Click += delegate { Configure("--sensor", "NixLanConsoleHost"); };
            display.Click += delegate { Configure("--display", "NixLanConsoleDisplay"); };
            var addGame = new Button { Text = "为主机添加直接运行的游戏 exe…", Location = new Point(34, 210), Size = new Size(490, 38), Font = new Font("Microsoft YaHei", 10), BackColor = Color.FromArgb(245, 225, 247) };
            addGame.Click += delegate { AddGame(); };
            var updateLabel = new Label { Text = "副机自动更新：在副机打开此对话并下载新版，程序会自动安装。", Location = new Point(34, 264), Size = new Size(490, 25), Font = new Font("Microsoft YaHei", 9), ForeColor = Color.FromArgb(64, 49, 142) };
            Controls.Add(title); Controls.Add(info); Controls.Add(host); Controls.Add(display); Controls.Add(addGame); Controls.Add(updateLabel);
        }
        private void AddGame()
        {
            using (var dialog = new OpenFileDialog { Filter = "Windows 程序 (*.exe)|*.exe", Title = "选择要追踪的游戏程序", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string label = Path.GetFileNameWithoutExtension(dialog.FileName);
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");
                Directory.CreateDirectory(dir); string file = Path.Combine(dir, "tracked-games.txt");
                string executable = Path.GetFullPath(dialog.FileName);
                var lines = File.Exists(file) ? File.ReadAllLines(file).Where(x => !x.TrimStart().StartsWith(executable + "|", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
                lines.Add(executable + "|" + label); File.WriteAllLines(file, lines, Encoding.UTF8);
                MessageBox.Show(this, "已添加“" + label + "”。主机后台会自动读取新设置。", "游戏追踪", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private void Configure(string role, string taskName)
        {
            string exe = Application.ExecutablePath;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                    key.SetValue(taskName, "\"" + exe + "\" " + role, Microsoft.Win32.RegistryValueKind.String);
                var run = new ProcessStartInfo(exe, role) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                Process.Start(run);
                Hide(); Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法保存或启动后台程序。请确认程序文件夹可读，并尝试将文件夹移到当前用户有权限的位置。\r\n\r\n详细信息：" + ex.Message, "设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class MetricPacket
    {
        public string host;
        public string bootId;
        public string version;
        public long utc;
        public double cpu;
        public double memoryUsed;
        public double memoryTotal;
        public double gpu;
        public double cpuTemp;
        public double gpuTemp;
        public string[] games;
        public string[] catalog;
    }

    internal sealed class ControlPacket
    {
        public string[] excludedGames;
        public bool hostAutoUpdate = true;
    }

    internal sealed class ConsoleSettings
    {
        public bool autoUpdate = true;
        public bool hostAutoUpdate = true;
        public string[] excludedGames = new[] { "Wallpaper Engine" };
    }

    internal sealed class UpdateStatus
    {
        public string state;
        public int percent;
        public string version;
        public string message;
    }

    internal static class HostSensor
    {
        private const int Port = 46701;
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly string BootId = Guid.NewGuid().ToString("N");
        private static PerformanceCounter CpuCounter;
        private static double GpuTemperature = -1;
        private static double CpuTemperature = -1;
        private static DateTime LastTemperatureRead = DateTime.MinValue;
        private static DateTime LastGameConfigRead = DateTime.MinValue;
        private static readonly Dictionary<string, string> GameExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> CustomGameExecutables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object ControlGate = new object();
        private static HashSet<string> RemoteExcludedGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static volatile bool RemoteAutoUpdate = true;
        private static System.Threading.Timer UpdateTimer;
        private static volatile bool GameRunning;
        internal static bool HasGameRunning { get { return GameRunning; } }
        internal static bool AutoUpdateEnabled { get { return RemoteAutoUpdate; } }

        public static void Run()
        {
            Directory.CreateDirectory(DataDir);
            StartTray();
            DiscoverGames();
            LoadCustomGames();
            UpdateTimer = new System.Threading.Timer(DisplayServer.CheckForUpdate, "--sensor", TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));
            try { CpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); CpuCounter.NextValue(); } catch { }
            using (var udp = new UdpClient())
            {
                udp.EnableBroadcast = true;
                while (true)
                {
                    var p = new MetricPacket { host = Environment.MachineName, bootId = BootId, version = BuildInfo.Version, utc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cpu = ReadCpu(), memoryUsed = 0, memoryTotal = 0, gpu = ReadGpuUsage(), cpuTemp = -1, gpuTemp = -1, games = ReadGames(), catalog = GetGameCatalog() };
                    ReadMemory(out p.memoryUsed, out p.memoryTotal);
                    if ((DateTime.UtcNow - LastTemperatureRead).TotalSeconds >= 5) { ReadTemperatures(); LastTemperatureRead = DateTime.UtcNow; }
                    p.cpuTemp = CpuTemperature; p.gpuTemp = GpuTemperature;
                    byte[] bytes = Encoding.UTF8.GetBytes(Json.Serialize(p));
                    try { udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, Port)); } catch { }
                    ReadControl(udp);
                    Thread.Sleep(1000);
                }
            }
        }
        private static void ReadControl(UdpClient udp)
        {
            try
            {
                while (udp.Available > 0)
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] bytes = udp.Receive(ref remote);
                    if (bytes.Length > 8192) continue;
                    var packet = Json.Deserialize<ControlPacket>(Encoding.UTF8.GetString(bytes));
                    if (packet == null) continue;
                    lock (ControlGate) RemoteExcludedGames = new HashSet<string>((packet.excludedGames ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
                    RemoteAutoUpdate = packet.hostAutoUpdate;
                }
            }
            catch { }
        }
        private static bool IsSystemUtility(string path, string title)
        {
            string exe = Path.GetFileNameWithoutExtension(path);
            return String.Equals(title, "Wallpaper Engine", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(exe, @"\Awallpaper(32|64)?\z", RegexOptions.IgnoreCase);
        }
        private static string[] GetGameCatalog()
        {
            return GameExecutables.Concat(CustomGameExecutables).Where(x => !IsSystemUtility(x.Key, x.Value)).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        }
        private static void StartTray()
        {
            var trayThread = new Thread(delegate()
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new HostTrayContext());
            });
            trayThread.IsBackground = true;
            trayThread.SetApartmentState(ApartmentState.STA);
            trayThread.Start();
        }
        private static double ReadCpu() { try { return Math.Round(CpuCounter.NextValue(), 1); } catch { return -1; } }
        private static void ReadMemory(out double used, out double total)
        {
            used = total = 0;
            try { using (var s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem")) foreach (ManagementObject m in s.Get()) { total = Convert.ToDouble(m["TotalVisibleMemorySize"]) / 1024.0 / 1024.0; used = total - Convert.ToDouble(m["FreePhysicalMemory"]) / 1024.0 / 1024.0; } } catch { }
        }
        private static double ReadGpuUsage()
        {
            double sum = 0;
            try { using (var s = new ManagementObjectSearcher("root\\cimv2", "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine")) foreach (ManagementObject m in s.Get()) sum += Convert.ToDouble(m["UtilizationPercentage"]); } catch { return -1; }
            return Math.Min(100, Math.Round(sum, 1));
        }
        private static void ReadTemperatures()
        {
            try { using (var s = new ManagementObjectSearcher("root\\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature")) foreach (ManagementObject m in s.Get()) { double t = Convert.ToDouble(m["CurrentTemperature"]) / 10.0 - 273.15; if (t > 0 && t < 120) { CpuTemperature = Math.Round(t, 0); break; } } } catch { }
            try
            {
                var psi = new ProcessStartInfo("nvidia-smi.exe", "--query-gpu=temperature.gpu --format=csv,noheader,nounits") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) { string result = p.StandardOutput.ReadToEnd(); p.WaitForExit(1500); double t; if (double.TryParse(result.Trim().Split('\n')[0], out t)) GpuTemperature = t; }
            }
            catch { }
        }
        private static string[] ReadGames()
        {
            LoadCustomGames();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> excluded;
            lock (ControlGate) excluded = new HashSet<string>(RemoteExcludedGames, StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string path = Path.GetFullPath(p.MainModule.FileName);
                    string title;
                    if ((CustomGameExecutables.TryGetValue(path, out title) || GameExecutables.TryGetValue(path, out title)) && !IsSystemUtility(path, title) && !excluded.Contains(title)) names.Add(title);
                }
                catch { }
                finally { p.Dispose(); }
            }
            GameRunning = names.Count > 0;
            return names.ToArray();
        }
        private static void DiscoverGames()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common"));
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam")) { var v = k == null ? null : k.GetValue("SteamPath") as string; if (!String.IsNullOrEmpty(v)) roots.Add(Path.Combine(v, "steamapps", "common")); } } catch { }
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                roots.Add(Path.Combine(drive.RootDirectory.FullName, "Steam", "steamapps", "common"));
                roots.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common"));
                string endfield = Path.Combine(drive.RootDirectory.FullName, "Hypergryph Launcher", "games", "Endfield Game", "Endfield.exe");
                if (File.Exists(endfield)) GameExecutables[Path.GetFullPath(endfield)] = "明日方舟：终末地";
            }
            foreach (string root in roots.ToArray())
            {
                try
                {
                    string libraries = Path.Combine(Directory.GetParent(Directory.GetParent(root).FullName).FullName, "steamapps", "libraryfolders.vdf");
                    if (!File.Exists(libraries)) continue;
                    foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\""))
                        roots.Add(Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common"));
                }
                catch { }
            }
            foreach (string root in roots)
            {
                try
                {
                    string manifests = Path.Combine(Directory.GetParent(root).FullName, "appmanifest_*.acf");
                    foreach (string file in Directory.GetFiles(Path.GetDirectoryName(manifests), Path.GetFileName(manifests)))
                    {
                        string appId = Regex.Match(Path.GetFileName(file), @"appmanifest_(\d+)\.acf", RegexOptions.IgnoreCase).Groups[1].Value;
                        if (appId == "431960" || appId == "1812620" || appId == "228980") continue;
                        string data = File.ReadAllText(file); var im = Regex.Match(data, "\\\"name\\\"\\s+\\\"([^\\\"]+)\\\""); var dm = Regex.Match(data, "\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"");
                        if (!im.Success || !dm.Success) continue;
                        string dir = Path.Combine(root, dm.Groups[1].Value); if (!Directory.Exists(dir)) continue;
                        foreach (string exe in Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories))
                        {
                            string lower = exe.ToLowerInvariant(); if (lower.Contains("\\redist\\") || lower.Contains("\\_commonredist\\") || lower.Contains("\\easyanticheat\\") || lower.Contains("\\battleye\\") || lower.Contains("\\anticheat\\")) continue;
                            string proc = Path.GetFileNameWithoutExtension(exe); if (proc.Length < 3 || Regex.IsMatch(proc, "crash|report|unins|setup|updater|cef|webengine|platformprocess", RegexOptions.IgnoreCase)) continue;
                            GameExecutables[Path.GetFullPath(exe)] = im.Groups[1].Value;
                        }
                    }
                }
                catch { }
            }
        }
        private static void LoadCustomGames()
        {
            string f = Path.Combine(DataDir, "tracked-games.txt");
            try
            {
                DateTime write = File.Exists(f) ? File.GetLastWriteTimeUtc(f) : DateTime.MinValue;
                if (write <= LastGameConfigRead) return;
                LastGameConfigRead = write;
                CustomGameExecutables.Clear();
                if (!File.Exists(f)) return;
                foreach (string line in File.ReadAllLines(f))
                {
                    string[] p = line.Split(new[] { '|' }, 2);
                    if (p.Length != 2 || !File.Exists(p[0].Trim())) continue;
                    string path = Path.GetFullPath(p[0].Trim());
                    CustomGameExecutables[path] = String.IsNullOrWhiteSpace(p[1]) ? Path.GetFileNameWithoutExtension(path) : p[1].Trim();
                }
            }
            catch { }
        }
    }

    internal sealed class GameSession
    {
        public string id;
        public string game;
        public long start;
        public long end;
    }
    internal sealed class DashboardState
    {
        public bool online;
        public string host;
        public double cpu, memoryUsed, memoryTotal, gpu, cpuTemp, gpuTemp;
        public string[] games = new string[0];
        public string role = "ame";
        public long roleEvent;
        public long returnInSeconds;
        public GameSession[] sessions;
        public string version;
        public string hostVersion;
        public string[] knownGames;
        public string[] excludedGames;
        public bool autoUpdate;
        public bool hostAutoUpdate;
    }

    internal static class BuildInfo { public const string Version = "1.5.0.0"; }

    internal static class DisplayServer
    {
        private const int UdpPort = 46701;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly object Gate = new object();
        private static readonly List<GameSession> Sessions = new List<GameSession>();
        private static readonly Dictionary<string, GameSession> ActiveSessions = new Dictionary<string, GameSession>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> KnownGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static ConsoleSettings Settings = new ConsoleSettings();
        private static MetricPacket Last;
        private static DateTime LastSeen = DateTime.MinValue;
        private static bool AnyGame;
        private static DateTime ReturnAt = DateTime.MinValue;
        private static long EventId;
        private static string Role = "ame";
        private static TrayContext Tray;
        private static System.Threading.Timer UpdateTimer;
        private static int UpdateStarted;
        private static readonly object UpdateProgressGate = new object();
        private static string UpdateProgressState = "idle";
        private static string UpdateProgressMessage = "每 5 分钟检查 GitHub 最新版本。";
        private static string UpdateProgressVersion = "";
        private static int UpdateProgressPercent = -1;
        private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NixLanConsole");

        private static void SetUpdateProgress(string state, int percent, string version, string message)
        {
            lock (UpdateProgressGate)
            {
                UpdateProgressState = state;
                UpdateProgressPercent = percent;
                UpdateProgressVersion = version ?? "";
                UpdateProgressMessage = message ?? "";
            }
        }

        internal static UpdateStatus GetUpdateProgress()
        {
            lock (UpdateProgressGate)
                return new UpdateStatus { state = UpdateProgressState, percent = UpdateProgressPercent, version = UpdateProgressVersion, message = UpdateProgressMessage };
        }

        public static void Run()
        {
            Directory.CreateDirectory(DataDir); LoadSettings(); LoadSessions();
            UpdateTimer = new System.Threading.Timer(CheckForUpdate, null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));
            Tray = new TrayContext();
            var udpThread = new Thread(ReadUdp) { IsBackground = true }; udpThread.Start();
            Application.Run(Tray);
        }
        internal static void CheckForUpdate(object ignored)
        {
            if (Interlocked.CompareExchange(ref UpdateStarted, 1, 0) != 0) return;
            try
            {
                bool hostMode = String.Equals(ignored as string, "--sensor", StringComparison.Ordinal);
                bool force = String.Equals(ignored as string, "--display-force", StringComparison.Ordinal);
                if (hostMode)
                {
                    if (HostSensor.HasGameRunning) { SetUpdateProgress("waiting", -1, "", "主机游戏运行中，自动更新检查暂缓。"); return; }
                    if (!HostSensor.AutoUpdateEnabled) { SetUpdateProgress("disabled", -1, "", "主机自动更新已关闭。"); return; }
                }
                else
                {
                    lock (Gate)
                    {
                        if (ActiveSessions.Count > 0) { SetUpdateProgress("waiting", -1, "", "副机检测到游戏仍在运行，更新会在游戏退出后进行。"); return; }
                        if (!force && !Settings.autoUpdate) { SetUpdateProgress("disabled", -1, "", "副机自动更新已关闭。"); return; }
                    }
                }
                SetUpdateProgress("checking", 3, "", "正在检查更新版本…");
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Version current; if (!Version.TryParse(BuildInfo.Version, out current)) return;
                Version available = null; string sourceZip = null;
                foreach (string candidate in Directory.Exists(downloads) ? Directory.GetFiles(downloads, "ControlConsole*.zip") : new string[0])
                {
                    Version candidateVersion;
                    if (!ReadPackageVersion(candidate, out candidateVersion) || candidateVersion <= current) continue;
                    if (available == null || candidateVersion > available) { available = candidateVersion; sourceZip = candidate; }
                }
                string githubZip = FindGitHubUpdate(current, available);
                if (githubZip != null)
                {
                    Version githubVersion;
                    if (ReadPackageVersion(githubZip, out githubVersion) && (available == null || githubVersion > available))
                    { available = githubVersion; sourceZip = githubZip; }
                }
                if (sourceZip == null) { SetUpdateProgress("latest", 100, current.ToString(), "当前版本已是最新版本 " + current + "。"); return; }
                SetUpdateProgress("preparing", 96, available.ToString(), "更新包已下载，正在验证并准备安装…");
                string updateDir = Path.Combine(DataDir, "updates"); Directory.CreateDirectory(updateDir);
                string localZip = Path.Combine(updateDir, Path.GetFileName(sourceZip));
                if (!String.Equals(Path.GetFullPath(sourceZip), Path.GetFullPath(localZip), StringComparison.OrdinalIgnoreCase)) File.Copy(sourceZip, localZip, true);
                string script = Path.Combine(BaseDir, "ApplyUpdate.ps1");
                if (!File.Exists(script)) throw new FileNotFoundException("更新脚本不存在", script);
                string localScript = Path.Combine(updateDir, "ApplyUpdate.ps1"); File.Copy(script, localScript, true);
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
                string args = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " + Quote(localScript) + " -TargetDir " + Quote(BaseDir) + " -Archive " + Quote(localZip) + " -ParentPid " + Process.GetCurrentProcess().Id + " -RestartRole " + (hostMode ? "--sensor" : "--display");
                SetUpdateProgress("installing", 99, available.ToString(), "正在替换程序并重启控制台…");
                Process.Start(new ProcessStartInfo(powershell, args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                SetUpdateProgress("failed", -1, "", "更新失败：" + ex.Message);
                try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " update check: " + ex.Message + Environment.NewLine, Encoding.UTF8); } catch { }
            }
            finally { Interlocked.Exchange(ref UpdateStarted, 0); }
        }
        private static string FindGitHubUpdate(Version current, Version localAvailable)
        {
            string config = Path.Combine(BaseDir, "update-source.txt");
            if (!File.Exists(config)) return null;
            string repository = File.ReadAllText(config, Encoding.UTF8).Trim();
            if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z")) return null;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "NixLanConsole/" + BuildInfo.Version;
                    client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    string endpoint = "https://api.github.com/repos/" + repository + "/releases/latest";
                    var release = Json.Deserialize<Dictionary<string, object>>(client.DownloadString(endpoint));
                    Version version;
                    if (release == null || !Version.TryParse(Convert.ToString(release["tag_name"]).TrimStart('v', 'V'), out version) || version <= current || (localAvailable != null && version <= localAvailable)) return null;
                    var assets = release["assets"] as System.Collections.ArrayList;
                    if (assets == null) return null;
                    foreach (object item in assets)
                    {
                        var asset = item as Dictionary<string, object>;
                        if (asset == null || Convert.ToString(asset["name"]) != "ControlConsole-update-" + version + ".zip") continue;
                        Uri url;
                        if (!Uri.TryCreate(Convert.ToString(asset["browser_download_url"]), UriKind.Absolute, out url) || url.Scheme != "https" || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
                        string updateDir = Path.Combine(DataDir, "updates"); Directory.CreateDirectory(updateDir);
                        string path = Path.Combine(updateDir, "ControlConsole-update-" + version + ".zip");
                        string temp = path + ".download";
                        try
                        {
                            SetUpdateProgress("downloading", 0, version.ToString(), "正在下载更新包… 0%");
                            Exception downloadError = null;
                            using (var done = new ManualResetEvent(false))
                            using (var downloadClient = new WebClient())
                            {
                                downloadClient.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                                {
                                    string amount = e.TotalBytesToReceive > 0 ? e.ProgressPercentage + "%" : (e.BytesReceived / 1048576d).ToString("F1") + " MB";
                                    int percent = e.TotalBytesToReceive > 0 ? e.ProgressPercentage : -1;
                                    SetUpdateProgress("downloading", percent, version.ToString(), "正在下载更新包… " + amount);
                                };
                                downloadClient.DownloadFileCompleted += delegate(object sender, System.ComponentModel.AsyncCompletedEventArgs e) { downloadError = e.Error; done.Set(); };
                                downloadClient.DownloadFileAsync(url, temp);
                                if (!done.WaitOne(TimeSpan.FromMinutes(10))) { downloadClient.CancelAsync(); throw new TimeoutException("更新包下载超时。"); }
                            }
                            if (downloadError != null) throw downloadError;
                            SetUpdateProgress("verifying", 97, version.ToString(), "下载完成，正在校验更新包…");
                            string digest = asset.ContainsKey("digest") ? Convert.ToString(asset["digest"]) : "";
                            if (!string.IsNullOrEmpty(digest))
                            {
                                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GitHub 更新包校验格式无效。");
                                using (var file = File.OpenRead(temp)) using (var sha = SHA256.Create())
                                {
                                    string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                                    if (!actual.Equals(digest.Substring(7), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包校验失败，文件可能已损坏。");
                                }
                            }
                            Version embedded;
                            if (!ReadPackageVersion(temp, out embedded) || embedded != version) throw new InvalidDataException("更新包中的版本信息无效。");
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(temp, path);
                            return path;
                        }
                        finally { if (File.Exists(temp)) File.Delete(temp); }
                    }
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " GitHub update check: " + ex.Message + Environment.NewLine, Encoding.UTF8); } catch { }
            }
            return null;
        }
        private static bool ReadPackageVersion(string file, out Version version)
        {
            version = null;
            try
            {
                using (var archive = ZipFile.OpenRead(file))
                {
                    if (archive.GetEntry("ControlConsole.exe") == null || archive.GetEntry("ApplyUpdate.ps1") == null) return false;
                    var entry = archive.GetEntry("version.txt"); if (entry == null) return false;
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        Version embedded; if (!Version.TryParse(reader.ReadToEnd().Trim(), out embedded)) return false;
                        version = embedded; return true;
                    }
                }
            }
            catch { return false; }
        }
        private static string Quote(string s) { return "\"" + (s ?? "").Replace("\"", "\\\"") + "\""; }
        private static void ReadUdp()
        {
            using (var udp = new UdpClient(UdpPort))
            {
                while (true)
                {
                    try
                    {
                        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref remote);
                        MetricPacket p = Json.Deserialize<MetricPacket>(Encoding.UTF8.GetString(data));
                        if (p == null) continue;
                        lock (Gate) Update(p);
                        byte[] reply;
                        lock (Gate) reply = Encoding.UTF8.GetBytes(Json.Serialize(new ControlPacket { excludedGames = Settings.excludedGames, hostAutoUpdate = Settings.hostAutoUpdate }));
                        try { udp.Send(reply, reply.Length, remote); } catch { }
                    }
                    catch { }
                }
            }
        }
        private static void Update(MetricPacket p)
        {
            bool openForNewHost = ShouldOpenForHost(Last, p, DateTime.UtcNow - LastSeen);
            Last = p; LastSeen = DateTime.UtcNow;
            foreach (string name in p.catalog ?? new string[0]) if (!String.IsNullOrWhiteSpace(name)) KnownGames.Add(name);
            foreach (string name in p.games ?? new string[0]) if (!String.IsNullOrWhiteSpace(name)) KnownGames.Add(name);
            var excluded = new HashSet<string>(Settings.excludedGames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            excluded.Add("Wallpaper Engine");
            var current = new HashSet<string>((p.games ?? new string[0]).Where(x => !excluded.Contains(x)), StringComparer.OrdinalIgnoreCase);
            foreach (string name in current)
                if (!ActiveSessions.ContainsKey(name))
                {
                    ActiveSessions[name] = new GameSession { id = Guid.NewGuid().ToString("N"), game = name, start = p.utc, end = 0 };
                    Sessions.Insert(0, ActiveSessions[name]);
                }
            foreach (string name in ActiveSessions.Keys.ToArray())
                if (!current.Contains(name)) { ActiveSessions[name].end = p.utc; ActiveSessions.Remove(name); }
            bool running = current.Count > 0;
            if (running)
            {
                ReturnAt = DateTime.MinValue;
                if (Role == "ame") BeginTransformation();
            }
            else if (AnyGame)
            {
                ReturnAt = DateTime.UtcNow.AddMinutes(10);
                    ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(TimeSpan.FromMinutes(10)); lock (Gate) { if (ReturnAt != DateTime.MinValue && DateTime.UtcNow >= ReturnAt && ActiveSessions.Count == 0) { Role = "ame"; ReturnAt = DateTime.MinValue; EventId++; } } });
            }
            AnyGame = running;
            if (openForNewHost) RestoreDashboard();
            SaveSessions();
        }
        private static bool ShouldOpenForHost(MetricPacket previous, MetricPacket current, TimeSpan gap)
        {
            return previous == null || (!String.IsNullOrEmpty(current.bootId) && !String.Equals(previous.bootId, current.bootId, StringComparison.Ordinal)) || (String.IsNullOrEmpty(current.bootId) && gap.TotalSeconds > 30);
        }
        private static void BeginTransformation()
        {
            Role = "kangel"; EventId++;
        }
        private static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }
        private static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile)) Settings = Json.Deserialize<ConsoleSettings>(File.ReadAllText(SettingsFile)) ?? new ConsoleSettings();
                var excluded = new HashSet<string>(Settings.excludedGames ?? new string[0], StringComparer.OrdinalIgnoreCase);
                excluded.Add("Wallpaper Engine");
                Settings.excludedGames = excluded.ToArray();
            }
            catch { Settings = new ConsoleSettings(); }
        }
        private static string LatestReleaseVersion()
        {
            string config = Path.Combine(BaseDir, "update-source.txt");
            if (!File.Exists(config)) throw new FileNotFoundException("未配置 GitHub 更新地址");
            string repository = File.ReadAllText(config, Encoding.UTF8).Trim();
            if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z")) throw new ArgumentException("GitHub 更新地址无效");
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + repository + "/releases/latest");
            request.UserAgent = "NixLanConsole/" + BuildInfo.Version;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 8000;
            using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
            {
                var release = Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                return Convert.ToString(release["tag_name"]).TrimStart('v', 'V');
            }
        }
        internal static void SaveNativeSettings(bool autoUpdate, bool hostAutoUpdate, IEnumerable<string> excludedGames)
        {
            var names = new HashSet<string>(excludedGames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            names.Add("Wallpaper Engine");
            if (names.Count > 256 || names.Any(x => String.IsNullOrWhiteSpace(x) || x.Length > 100)) throw new ArgumentException("游戏排除列表无效");
            var proposed = new ConsoleSettings { autoUpdate = autoUpdate, hostAutoUpdate = hostAutoUpdate, excludedGames = names.ToArray() };
            lock (Gate)
            {
                File.WriteAllText(SettingsFile, Json.Serialize(proposed), Encoding.UTF8);
                Settings = proposed;
            }
        }
        internal static void EditNativeSession(string id, string game, long end, bool delete)
        {
            lock (Gate)
            {
                var session = Sessions.FirstOrDefault(s => s.id == id);
                if (session == null) throw new InvalidOperationException("找不到这条记录");
                if (ActiveSessions.Values.Contains(session)) throw new InvalidOperationException("游戏仍在运行，请先退出游戏");
                if (!delete && (String.IsNullOrWhiteSpace(game) || game.Length > 100 || end < session.start || end > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000)) throw new ArgumentException("记录内容无效");
                BackupSessions();
                if (delete) Sessions.Remove(session);
                else { session.game = game.Trim(); session.end = end; KnownGames.Add(session.game); }
                SaveSessions();
            }
        }
        internal static void PurgeNativeGame(string game)
        {
            if (String.IsNullOrWhiteSpace(game) || game.Length > 100) throw new ArgumentException("游戏名称无效");
            lock (Gate)
            {
                if (ActiveSessions.ContainsKey(game)) throw new InvalidOperationException("游戏仍在运行，请先退出游戏");
                BackupSessions();
                Sessions.RemoveAll(s => String.Equals(s.game, game, StringComparison.OrdinalIgnoreCase));
                SaveSessions();
            }
        }
        private static void BackupSessions()
        {
            if (File.Exists(SessionFile)) File.Copy(SessionFile, Path.Combine(DataDir, "sessions-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"));
        }
        internal static string CheckNativeRelease()
        {
            SetUpdateProgress("checking", 3, "", "正在查询 GitHub 最新版本…");
            try
            {
                string latest = LatestReleaseVersion();
                Version newest, current;
                bool available = Version.TryParse(latest, out newest) && Version.TryParse(BuildInfo.Version, out current) && newest > current;
                SetUpdateProgress(available ? "available" : "latest", 100, latest, available ? "发现可用更新 " + latest + "。" : "当前已是最新版本 " + BuildInfo.Version + "。");
                return latest;
            }
            catch (Exception ex)
            {
                SetUpdateProgress("failed", -1, "", "检查更新失败：" + ex.Message);
                throw;
            }
        }
        internal static void InstallNativeUpdate()
        {
            lock (Gate) if (ActiveSessions.Count > 0) throw new InvalidOperationException("游戏仍在运行，请退出游戏后安装更新");
            ThreadPool.QueueUserWorkItem(delegate { CheckForUpdate("--display-force"); });
        }
        internal static DashboardState Snapshot()
        {
            lock (Gate)
            {
                bool online = Last != null && (DateTime.UtcNow - LastSeen).TotalSeconds < 5;
                if (!online && (ActiveSessions.Count > 0 || AnyGame))
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    foreach (var s in ActiveSessions.Values) s.end = now;
                    ActiveSessions.Clear(); AnyGame = false; ReturnAt = DateTime.UtcNow.AddMinutes(10); SaveSessions();
                    ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(TimeSpan.FromMinutes(10)); lock (Gate) { if (!AnyGame && ReturnAt != DateTime.MinValue && DateTime.UtcNow >= ReturnAt) { Role = "ame"; ReturnAt = DateTime.MinValue; EventId++; } } });
                }
                return new DashboardState { online = online, host = Last == null ? "等待主机连接" : Last.host, cpu = !online || Last == null ? -1 : Last.cpu, memoryUsed = !online || Last == null ? 0 : Last.memoryUsed, memoryTotal = !online || Last == null ? 0 : Last.memoryTotal, gpu = !online || Last == null ? -1 : Last.gpu, cpuTemp = !online || Last == null ? -1 : Last.cpuTemp, gpuTemp = !online || Last == null ? -1 : Last.gpuTemp, games = !online ? new string[0] : ActiveSessions.Keys.ToArray(), role = Role, roleEvent = EventId, returnInSeconds = ReturnAt == DateTime.MinValue ? 0 : Math.Max(0, (long)(ReturnAt - DateTime.UtcNow).TotalSeconds), sessions = Sessions.Take(1000).Select(s => new GameSession { id = s.id, game = s.game, start = s.start, end = s.end }).ToArray(), version = BuildInfo.Version, hostVersion = Last == null ? "—" : Last.version, knownGames = KnownGames.OrderBy(x => x).ToArray(), excludedGames = (Settings.excludedGames ?? new string[0]).ToArray(), autoUpdate = Settings.autoUpdate, hostAutoUpdate = Settings.hostAutoUpdate };
            }
        }
        private static string SessionFile { get { return Path.Combine(DataDir, "sessions.json"); } }
        private static void LoadSessions()
        {
            try
            {
                string source = SessionFile;
                GameSession[] loaded;
                if (!TryReadSessions(source, out loaded))
                {
                    source = SessionFile + ".previous";
                    if (!TryReadSessions(source, out loaded)) return;
                    Log("session recovery: restored previous valid copy");
                    try { File.Copy(source, SessionFile, true); } catch (Exception ex) { Log("session recovery copy: " + ex.Message); }
                }
                long lastSaved = new DateTimeOffset(File.GetLastWriteTimeUtc(source)).ToUnixTimeMilliseconds();
                Sessions.AddRange(loaded);
                foreach (var session in Sessions) if (!String.IsNullOrWhiteSpace(session.game)) KnownGames.Add(session.game);
                if (CloseInterruptedSessions(Sessions, lastSaved)) SaveSessions();
            }
            catch (Exception ex) { Log("session load: " + ex.Message); }
        }
        private static bool TryReadSessions(string path, out GameSession[] sessions)
        {
            sessions = null;
            if (!File.Exists(path)) return false;
            try
            {
                sessions = Json.Deserialize<GameSession[]>(File.ReadAllText(path, Encoding.UTF8));
                return sessions != null;
            }
            catch (Exception ex) { Log("session read " + Path.GetFileName(path) + ": " + ex.Message); return false; }
        }
        private static bool CloseInterruptedSessions(IEnumerable<GameSession> sessions, long lastSaved)
        {
            bool repaired = false;
            foreach (var session in sessions)
            {
                if (String.IsNullOrEmpty(session.id)) { session.id = Guid.NewGuid().ToString("N"); repaired = true; }
                if (session.end == 0) { session.end = Math.Max(session.start, lastSaved); repaired = true; }
            }
            return repaired;
        }
        private static void SaveSessions()
        {
            string pending = SessionFile + ".pending";
            try
            {
                File.WriteAllText(pending, Json.Serialize(Sessions.Take(1000).ToArray()), Encoding.UTF8);
                if (File.Exists(SessionFile)) File.Replace(pending, SessionFile, SessionFile + ".previous", true);
                else File.Move(pending, SessionFile);
            }
            catch (Exception ex) { Log("session save: " + ex.Message); }
            finally { try { if (File.Exists(pending)) File.Delete(pending); } catch { } }
        }
        private static void Log(string message)
        {
            try { File.AppendAllText(Path.Combine(DataDir, "update.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        private static void RestoreDashboard()
        {
            try { if (Tray != null) Tray.OpenDashboard(); } catch { }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon Icon;
        private readonly Control Dispatcher;
        private NativeConsoleWindow Dashboard;
        public TrayContext()
        {
            Dispatcher = new Control();
            IntPtr dispatcherHandle = Dispatcher.Handle;
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开控制台", null, delegate { OpenDashboard(); });
            menu.Items.Add("退出程序", null, delegate { Environment.Exit(0); });
            Icon = new NotifyIcon { Text = "局域网桌面控制台", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
            Icon.DoubleClick += delegate { OpenDashboard(); };
        }
        public void OpenDashboard()
        {
            if (Dispatcher.InvokeRequired) { try { Dispatcher.BeginInvoke(new MethodInvoker(OpenDashboard)); } catch { } return; }
            if (Dashboard == null || Dashboard.IsDisposed) Dashboard = new NativeConsoleWindow();
            if (!Dashboard.Visible) Dashboard.Show();
            Dashboard.Bounds = Screen.PrimaryScreen.Bounds;
            Dashboard.WindowState = FormWindowState.Normal;
            Dashboard.Activate();
        }
        public void MinimizeDashboard()
        {
            if (Dispatcher.InvokeRequired) { try { Dispatcher.BeginInvoke(new MethodInvoker(MinimizeDashboard)); } catch { } return; }
            if (Dashboard != null && !Dashboard.IsDisposed) Dashboard.Hide();
        }
        public void CloseDashboard() { Environment.Exit(0); }
        protected override void ExitThreadCore() { Icon.Visible = false; Icon.Dispose(); base.ExitThreadCore(); }
    }

    internal sealed class HostTrayContext : ApplicationContext
    {
        private readonly NotifyIcon Icon;
        public HostTrayContext()
        {
            var menu = new ContextMenuStrip();
            var status = new ToolStripMenuItem("主机监控运行中") { Enabled = false };
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出主机监控", null, delegate { Environment.Exit(0); });
            Icon = new NotifyIcon { Text = "局域网桌面控制台 · 主机监控", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        }
        protected override void ExitThreadCore() { Icon.Visible = false; Icon.Dispose(); base.ExitThreadCore(); }
    }
}
